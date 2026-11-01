using Microsoft.Data.Sqlite;

namespace UnityAriadne.Graph;

/// <summary>One unresolved GUID with how it is referenced (for the report command).</summary>
/// <param name="Guid">The GUID no .meta file was found for.</param>
/// <param name="RefCount">Total refs rows pointing at it.</param>
/// <param name="ReferrerCount">Distinct assets referencing it.</param>
/// <param name="SampleReferrers">Up to the requested number of referrer asset paths.</param>
public sealed record UnresolvedGuidInfo(
    string Guid, long RefCount, long ReferrerCount, IReadOnlyList<string> SampleReferrers);

/// <summary>
/// The SQLite graph store (Phase 1e): owns the connection, creates the schema
/// (documented in docs/manual/schema.md), hands out a <see cref="GraphWriter"/> for bulk writes,
/// and answers the queries behind the report command.
/// </summary>
public sealed class GraphDatabase : IDisposable
{
    /// <summary>
    /// Stored in PRAGMA user_version; bump on any schema change. A database written by
    /// another version is rebuilt from scratch instead of updated incrementally.
    /// </summary>
    public const int SchemaVersion = 2;

    private const string Schema =
        """
        CREATE TABLE assets (
          id INTEGER PRIMARY KEY,
          guid TEXT UNIQUE NOT NULL,
          path TEXT,                 -- NULL for unresolved GUIDs
          asset_type TEXT NOT NULL,  -- Scene/Prefab/Script/ScriptableObject/Material/...
          source TEXT NOT NULL,      -- Assets/Packages/PackageCache/ProjectSettings/BuiltIn/Unresolved
          file_hash TEXT             -- Phase 4b change detection; NULL for Unresolved/BuiltIn/ProjectSettings
        );

        CREATE TABLE objects (        -- one YAML document = one fileID
          id INTEGER PRIMARY KEY,
          asset_id INTEGER NOT NULL REFERENCES assets(id),
          file_id INTEGER NOT NULL,
          class_id INTEGER NOT NULL,  -- Unity class ID, e.g. !u!114
          name TEXT,                  -- m_Name (if present)
          is_stripped INTEGER DEFAULT 0,
          UNIQUE(asset_id, file_id)
        );

        CREATE TABLE refs (           -- raw references (1 reference = 1 row)
          id INTEGER PRIMARY KEY,
          from_object_id INTEGER NOT NULL REFERENCES objects(id),
          to_guid TEXT,               -- external reference; NULL means same-file reference
          to_file_id INTEGER NOT NULL,
          property_path TEXT NOT NULL,-- e.g. m_Script, m_Materials[0], m_Modifications[3].target
          ref_kind TEXT NOT NULL      -- uses_script/source_prefab/prefab_instance/generic/...
        );

        CREATE INDEX idx_objects_asset ON objects(asset_id);
        CREATE INDEX idx_refs_from_object ON refs(from_object_id);
        CREATE INDEX idx_refs_to_guid ON refs(to_guid);

        CREATE VIEW asset_edges AS    -- aggregated at asset level (for jsonl export)
          SELECT DISTINCT a1.guid AS from_guid, r.to_guid,
                 r.ref_kind, COUNT(*) AS ref_count
          FROM refs r
          JOIN objects o ON o.id = r.from_object_id
          JOIN assets a1 ON a1.id = o.asset_id
          WHERE r.to_guid IS NOT NULL
          GROUP BY a1.guid, r.to_guid, r.ref_kind;

        CREATE VIEW unresolved_guids AS
          SELECT guid FROM assets WHERE path IS NULL;

        CREATE TABLE settings_files (  -- Phase 4b: every scanned ProjectSettings/*.asset,
          path TEXT PRIMARY KEY,       -- including ones without refs (those get no assets row,
          file_hash TEXT NOT NULL      -- but must still participate in change detection)
        );
        """;

    public SqliteConnection Connection { get; }

    private GraphDatabase(SqliteConnection connection) => Connection = connection;

    /// <summary>Backing store for `analyze` without --db: same code path, no file.</summary>
    public static GraphDatabase CreateInMemory() =>
        CreateEmpty(new SqliteConnectionStringBuilder { DataSource = ":memory:", Pooling = false });

    /// <summary>
    /// Creates a fresh database file, replacing any previous one — `analyze` is a full
    /// rebuild every run (incremental updates are Phase 4).
    /// </summary>
    public static GraphDatabase CreateFile(string path)
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        // Pooling off: a pooled handle from a previous run would still see the
        // deleted file's inode, and Dispose must actually release the new file.
        return CreateEmpty(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false });
    }

    /// <summary>Opens an existing database read-only (for report/export).</summary>
    public static GraphDatabase OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"database not found: {path}", path);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return new GraphDatabase(connection);
    }

    /// <summary>
    /// Opens an existing database read-write for an incremental update (Phase 4b).
    /// Returns null when the file does not exist, is not a SQLite database, or was
    /// written with a different <see cref="SchemaVersion"/> — the caller should fall
    /// back to a full rebuild via <see cref="CreateFile"/>.
    /// </summary>
    public static GraphDatabase? TryOpenForUpdate(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());

        try
        {
            connection.Open();
            var db = new GraphDatabase(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            if ((long)command.ExecuteScalar()! != SchemaVersion)
            {
                db.Dispose();
                return null;
            }

            db.Execute("PRAGMA foreign_keys = ON");
            return db;
        }
        catch (SqliteException)
        {
            connection.Dispose();
            return null;
        }
    }

    private static GraphDatabase CreateEmpty(SqliteConnectionStringBuilder connectionString)
    {
        var connection = new SqliteConnection(connectionString.ToString());
        connection.Open();
        var db = new GraphDatabase(connection);
        db.Execute("PRAGMA foreign_keys = ON");
        db.Execute(Schema);
        db.Execute($"PRAGMA user_version = {SchemaVersion}");
        return db;
    }

    /// <summary>One transaction with prepared inserts; dispose without Commit rolls back.</summary>
    public GraphWriter CreateWriter() => new(Connection);

    /// <summary>
    /// Every unresolved GUID with its incoming-reference stats, most-referenced first.
    /// </summary>
    public IReadOnlyList<UnresolvedGuidInfo> GetUnresolvedGuids(int maxSampleReferrers = 3)
    {
        var results = new List<UnresolvedGuidInfo>();

        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.guid, COUNT(r.id), COUNT(DISTINCT o.asset_id)
            FROM assets a
            LEFT JOIN refs r ON r.to_guid = a.guid
            LEFT JOIN objects o ON o.id = r.from_object_id
            WHERE a.path IS NULL
            GROUP BY a.guid
            ORDER BY COUNT(r.id) DESC, a.guid
            """;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                results.Add(new UnresolvedGuidInfo(
                    reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), []));
            }
        }

        return results
            .Select(info => info with { SampleReferrers = GetReferrerPaths(info.Guid, maxSampleReferrers) })
            .ToList();
    }

    private IReadOnlyList<string> GetReferrerPaths(string guid, int limit)
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT a.path
            FROM refs r
            JOIN objects o ON o.id = r.from_object_id
            JOIN assets a ON a.id = o.asset_id
            WHERE r.to_guid = $guid AND a.path IS NOT NULL
            ORDER BY a.path
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$guid", guid);
        command.Parameters.AddWithValue("$limit", limit);

        var paths = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
