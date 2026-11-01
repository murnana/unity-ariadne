using Microsoft.Data.Sqlite;

namespace UnityAriadne.Graph;

/// <summary>
/// Bulk-insert half of <see cref="GraphDatabase"/> (Phase 1e): one transaction over the
/// whole analysis with prepared commands per table. Disposing without <see cref="Commit"/>
/// rolls everything back, so a failed run never leaves a half-written database.
/// Phase 4b adds the update/delete commands used by incremental updates.
/// </summary>
public sealed class GraphWriter : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _insertAsset;
    private readonly SqliteCommand _insertObject;
    private readonly SqliteCommand _insertRef;
    private readonly SqliteCommand _updateAsset;
    private readonly SqliteCommand _deleteAssetRefs;
    private readonly SqliteCommand _deleteAssetObjects;
    private readonly SqliteCommand _deleteAsset;
    private readonly SqliteCommand _setSettingsFile;
    private bool _committed;

    internal GraphWriter(SqliteConnection connection)
    {
        _connection = connection;
        _transaction = connection.BeginTransaction();
        _insertAsset = Prepare(connection,
            "INSERT INTO assets (guid, path, asset_type, source, file_hash) VALUES ($guid, $path, $assetType, $source, $fileHash) RETURNING id",
            "$guid", "$path", "$assetType", "$source", "$fileHash");
        _insertObject = Prepare(connection,
            "INSERT INTO objects (asset_id, file_id, class_id, name, is_stripped) VALUES ($assetId, $fileId, $classId, $name, $isStripped) RETURNING id",
            "$assetId", "$fileId", "$classId", "$name", "$isStripped");
        _insertRef = Prepare(connection,
            "INSERT INTO refs (from_object_id, to_guid, to_file_id, property_path, ref_kind) VALUES ($fromObjectId, $toGuid, $toFileId, $propertyPath, $refKind)",
            "$fromObjectId", "$toGuid", "$toFileId", "$propertyPath", "$refKind");
        _updateAsset = Prepare(connection,
            "UPDATE assets SET path = $path, asset_type = $assetType, source = $source, file_hash = $fileHash WHERE id = $id",
            "$path", "$assetType", "$source", "$fileHash", "$id");
        _deleteAssetRefs = Prepare(connection,
            "DELETE FROM refs WHERE from_object_id IN (SELECT id FROM objects WHERE asset_id = $assetId)",
            "$assetId");
        _deleteAssetObjects = Prepare(connection,
            "DELETE FROM objects WHERE asset_id = $assetId",
            "$assetId");
        _deleteAsset = Prepare(connection,
            "DELETE FROM assets WHERE id = $id",
            "$id");
        _setSettingsFile = Prepare(connection,
            "INSERT OR REPLACE INTO settings_files (path, file_hash) VALUES ($path, $fileHash)",
            "$path", "$fileHash");
    }

    /// <returns>The assets.id of the inserted row.</returns>
    public long AddAsset(string guid, string? path, string assetType, string source, string? fileHash = null)
    {
        SetParameters(_insertAsset, guid, path, assetType, source, fileHash);
        return (long)_insertAsset.ExecuteScalar()!;
    }

    /// <returns>The objects.id of the inserted row.</returns>
    public long AddObject(long assetId, long fileId, int classId, string? name, bool isStripped)
    {
        SetParameters(_insertObject, assetId, fileId, classId, name, isStripped ? 1 : 0);
        return (long)_insertObject.ExecuteScalar()!;
    }

    /// <param name="toGuid">Target asset GUID; null means a reference within the same file.</param>
    public void AddRef(long fromObjectId, string? toGuid, long toFileId, string propertyPath, string refKind)
    {
        SetParameters(_insertRef, fromObjectId, toGuid, toFileId, propertyPath, refKind);
        _insertRef.ExecuteNonQuery();
    }

    /// <summary>Rewrites an existing assets row in place (rename, resolve, unresolve).</summary>
    public void UpdateAsset(long id, string? path, string assetType, string source, string? fileHash)
    {
        SetParameters(_updateAsset, path, assetType, source, fileHash, id);
        _updateAsset.ExecuteNonQuery();
    }

    /// <summary>Deletes all objects of an asset and every ref originating from them.
    /// Incoming refs from other assets are kept — dangling targets are handled by
    /// <see cref="DeleteUnreferencedTargets"/> at the end of an update.</summary>
    public void DeleteAssetContent(long assetId)
    {
        SetParameters(_deleteAssetRefs, assetId);
        _deleteAssetRefs.ExecuteNonQuery();
        SetParameters(_deleteAssetObjects, assetId);
        _deleteAssetObjects.ExecuteNonQuery();
    }

    /// <summary>Deletes an assets row; call <see cref="DeleteAssetContent"/> first.</summary>
    public void DeleteAsset(long id)
    {
        SetParameters(_deleteAsset, id);
        _deleteAsset.ExecuteNonQuery();
    }

    /// <summary>Records the hash of one scanned ProjectSettings file (Phase 4b).</summary>
    public void SetSettingsFile(string path, string fileHash)
    {
        SetParameters(_setSettingsFile, path, fileHash);
        _setSettingsFile.ExecuteNonQuery();
    }

    public void ClearSettingsFiles() => Execute("DELETE FROM settings_files");

    /// <summary>
    /// Deletes every Phase 2 MonoScript object (and its refs) so the code layer can be
    /// rewritten wholesale — code-ref resolution is global (declared-type index,
    /// Resources/ path set), so it is never updated per file.
    /// </summary>
    public void DeleteCodeLayer()
    {
        Execute(
            """
            DELETE FROM refs WHERE from_object_id IN (
              SELECT o.id FROM objects o
              JOIN assets a ON a.id = o.asset_id
              WHERE o.file_id = 11500000 AND o.class_id = 115 AND a.asset_type = 'Script');
            DELETE FROM objects WHERE id IN (
              SELECT o.id FROM objects o
              JOIN assets a ON a.id = o.asset_id
              WHERE o.file_id = 11500000 AND o.class_id = 115 AND a.asset_type = 'Script');
            """);
    }

    /// <summary>
    /// Drops Unresolved/BuiltIn rows that lost their last incoming ref during an update.
    /// They only exist because something referenced them (<c>EnsureTargetAsset</c>).
    /// </summary>
    public void DeleteUnreferencedTargets()
    {
        Execute(
            """
            DELETE FROM assets
            WHERE source IN ('Unresolved', 'BuiltIn')
              AND NOT EXISTS (SELECT 1 FROM refs r WHERE r.to_guid = assets.guid)
            """);
    }

    public void Commit()
    {
        _transaction.Commit();
        _committed = true;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Prepare(SqliteConnection connection, string sql, params string[] parameterNames)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var name in parameterNames)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        return command;
    }

    private static void SetParameters(SqliteCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters[i].Value = values[i] ?? DBNull.Value;
        }
    }

    public void Dispose()
    {
        _insertAsset.Dispose();
        _insertObject.Dispose();
        _insertRef.Dispose();
        _updateAsset.Dispose();
        _deleteAssetRefs.Dispose();
        _deleteAssetObjects.Dispose();
        _deleteAsset.Dispose();
        _setSettingsFile.Dispose();
        if (!_committed)
        {
            _transaction.Rollback();
        }

        _transaction.Dispose();
    }
}
