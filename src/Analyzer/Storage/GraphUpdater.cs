using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Settings;
using UnityAriadne.Graph;

namespace UnityAriadne.Analyzer.Storage;

/// <summary>What one incremental update did, for the `analyze` console output.</summary>
public sealed class UpdateSummary
{
    /// <summary>New GUIDs, plus previously unresolved GUIDs that now have a .meta.</summary>
    public int AddedCount { get; internal set; }

    /// <summary>Assets whose file hash changed — content was deleted and re-analyzed.</summary>
    public int ChangedCount { get; internal set; }

    /// <summary>Assets whose path/type/source changed with identical content (renames) —
    /// row updated in place, no re-analysis.</summary>
    public int MovedCount { get; internal set; }

    /// <summary>GUIDs no longer on disk. Their rows become Unresolved while still
    /// referenced, and are dropped once nothing references them.</summary>
    public int RemovedCount { get; internal set; }

    public int UnchangedCount { get; internal set; }

    public bool SettingsReanalyzed { get; internal set; }

    public bool CodeLayerReanalyzed { get; internal set; }

    /// <summary>Counters of the re-analyzed subset only (not whole-database totals).</summary>
    public AnalysisSummary Analysis { get; } = new();

    public bool NothingChanged =>
        AddedCount == 0 && ChangedCount == 0 && MovedCount == 0 && RemovedCount == 0
        && !SettingsReanalyzed && !CodeLayerReanalyzed;
}

/// <summary>
/// Phase 4b incremental update: diffs the current on-disk state against an existing
/// database via assets.file_hash and re-analyzes only what changed, in one transaction.
/// The invariant (verified by GraphUpdaterTests): after Update the database is
/// row-for-row identical to a fresh <see cref="GraphBuilder.Build"/> of the same tree.
/// </summary>
/// <remarks>
/// Per-asset granularity is sound for the asset layer because an asset's rows derive
/// only from its own file and .meta. It is NOT sound for two global layers, which are
/// therefore redone wholesale when their inputs change:
/// <list type="bullet">
/// <item>ProjectSettings — small (≈10 files); redone when any settings file hash
/// changes (tracked in settings_files, which also covers files without refs).</item>
/// <item>The Phase 2 code layer — type-name resolution depends on the global
/// declared-type index (a new type elsewhere can make a name ambiguous),
/// Resources.Load resolution on the global asset-path set, and Addressables key
/// resolution on the global address/label tables, so it is redone when any .cs
/// changed, the set of asset paths changed at all, or an AddressableAssetGroup's
/// content changed.</item>
/// </list>
/// </remarks>
public static class GraphUpdater
{
    private sealed record AssetRow(long Id, string? Path, string AssetType, string Source, string? FileHash);

    public static UpdateSummary Update(string projectRoot, GuidDictionary dictionary, GraphDatabase db)
    {
        var summary = new UpdateSummary();
        using var writer = db.CreateWriter();

        var existing = LoadAssets(db);
        var assetIds = existing.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.Ordinal);
        var hashes = GraphBuilder.ComputeHashes(projectRoot, dictionary);
        var previouslyAddressableGroups = LoadAddressableGroupIds(db);

        var toAnalyze = new List<AssetEntry>();
        var assetSetChanged = false;
        var scriptsChanged = false;
        var addressablesChanged = false;

        foreach (var entry in dictionary.Entries)
        {
            var hash = hashes[entry.Guid];
            if (!existing.TryGetValue(entry.Guid, out var row))
            {
                assetIds[entry.Guid] = writer.AddAsset(entry.Guid, entry.Path, entry.AssetType, entry.Source, hash);
                summary.AddedCount++;
                assetSetChanged = true;
                scriptsChanged |= IsScript(entry.Path);
                toAnalyze.Add(entry);
            }
            else if (row.Path is null)
            {
                // Was a dangling target (EnsureTargetAsset row); the asset now exists.
                writer.UpdateAsset(row.Id, entry.Path, entry.AssetType, entry.Source, hash);
                summary.AddedCount++;
                assetSetChanged = true;
                scriptsChanged |= IsScript(entry.Path);
                toAnalyze.Add(entry);
            }
            else if (row.FileHash != hash)
            {
                writer.DeleteAssetContent(row.Id);
                writer.UpdateAsset(row.Id, entry.Path, entry.AssetType, entry.Source, hash);
                summary.ChangedCount++;
                assetSetChanged |= row.Path != entry.Path;
                scriptsChanged |= IsScript(entry.Path) || IsScript(row.Path);
                // A pure content change doesn't touch the path set, but editing an
                // AddressableAssetGroup changes the address/label tables that
                // addressables_load resolution reads.
                addressablesChanged |= previouslyAddressableGroups.Contains(row.Id);
                toAnalyze.Add(entry);
            }
            else if (row.Path != entry.Path || row.AssetType != entry.AssetType || row.Source != entry.Source)
            {
                writer.UpdateAsset(row.Id, entry.Path, entry.AssetType, entry.Source, hash);
                summary.MovedCount++;
                assetSetChanged = true;
                scriptsChanged |= IsScript(entry.Path) || IsScript(row.Path);
            }
            else
            {
                summary.UnchangedCount++;
            }
        }

        // Scanned sources whose GUID vanished from disk. The row is kept as Unresolved
        // because other assets may still reference the GUID; DeleteUnreferencedTargets
        // drops it below if nothing does.
        foreach (var (guid, row) in existing)
        {
            if (row.Source is "Unresolved" or "BuiltIn" or GraphBuilder.ProjectSettingsSource
                || dictionary.Find(guid) is not null)
            {
                continue;
            }

            writer.DeleteAssetContent(row.Id);
            writer.UpdateAsset(row.Id, path: null, "Unknown", "Unresolved", fileHash: null);
            summary.RemovedCount++;
            assetSetChanged = true;
            scriptsChanged |= IsScript(row.Path);
        }

        foreach (var entry in toAnalyze)
        {
            GraphBuilder.WriteAssetContent(projectRoot, entry, writer, assetIds, summary.Analysis);
        }

        // The other direction of the group-edit case: a re-analyzed asset (added or
        // changed) now carries addressable entries.
        addressablesChanged |=
            summary.Analysis.YamlKindCounts.GetValueOrDefault(Yaml.RefKind.AddressableEntry) > 0;

        if (SettingsFilesChanged(projectRoot, db))
        {
            foreach (var row in existing.Values.Where(r => r.Source == GraphBuilder.ProjectSettingsSource))
            {
                writer.DeleteAssetContent(row.Id);
                writer.DeleteAsset(row.Id);
            }

            writer.ClearSettingsFiles();
            GraphBuilder.WriteProjectSettings(projectRoot, writer, assetIds, summary.Analysis);
            summary.SettingsReanalyzed = true;
        }

        if (scriptsChanged || assetSetChanged || addressablesChanged)
        {
            writer.DeleteCodeLayer();
            GraphBuilder.WriteCodeRefs(projectRoot, dictionary, writer, assetIds, summary.Analysis);
            summary.CodeLayerReanalyzed = true;
        }

        writer.DeleteUnreferencedTargets();
        writer.Commit();
        return summary;
    }

    private static bool IsScript(string? path) =>
        path is not null && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>Asset ids that produced addressable_entry refs in the previous state —
    /// i.e. the AddressableAssetGroup assets the code layer's key index was built from.</summary>
    private static HashSet<long> LoadAddressableGroupIds(GraphDatabase db)
    {
        var ids = new HashSet<long>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT o.asset_id FROM refs r
            JOIN objects o ON o.id = r.from_object_id
            WHERE r.ref_kind = 'addressable_entry'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static bool SettingsFilesChanged(string projectRoot, GraphDatabase db)
    {
        var recorded = LoadSettingsFiles(db);
        var current = ProjectSettingsScanner.ListSettingsFiles(projectRoot);
        if (recorded.Count != current.Count)
        {
            return true;
        }

        foreach (var file in current)
        {
            var relativePath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
            if (!recorded.TryGetValue(relativePath, out var hash) || hash != ContentHasher.HashFile(file))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, AssetRow> LoadAssets(GraphDatabase db)
    {
        var rows = new Dictionary<string, AssetRow>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT guid, id, path, asset_type, source, file_hash FROM assets";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows[reader.GetString(0)] = new AssetRow(
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        return rows;
    }

    private static Dictionary<string, string> LoadSettingsFiles(GraphDatabase db)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT path, file_hash FROM settings_files";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows[reader.GetString(0)] = reader.GetString(1);
        }

        return rows;
    }
}
