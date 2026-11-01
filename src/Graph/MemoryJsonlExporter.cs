using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityAriadne.Graph;

/// <summary>Options for <see cref="MemoryJsonlExporter"/>.</summary>
public sealed record ExportOptions
{
    /// <summary>
    /// When false (the default), Library/PackageCache assets appear only as targets of
    /// references from the project's own assets, and their outgoing references are
    /// dropped — otherwise the fixture-sized graph balloons from ~1.3k to ~14.6k nodes.
    /// Embedded packages under Packages/ are always full nodes.
    /// </summary>
    public bool IncludePackageCache { get; init; }
}

/// <summary>What an export produced (for the console summary).</summary>
/// <param name="SkippedPackageCacheAssetCount">PackageCache assets left out entirely
/// (neither a full node nor a referenced target); 0 when IncludePackageCache is on.</param>
public sealed record ExportSummary(int EntityCount, int RelationCount, int SkippedPackageCacheAssetCount);

/// <summary>
/// Exports the asset-level graph as memory.jsonl, the line format of the MCP memory
/// server (Phase 1f): one <c>{"type":"entity",...}</c> line per asset, then one
/// <c>{"type":"relation",...}</c> line per asset_edges row. Entity names are project
/// paths (the GUID for unresolved assets); observations carry guid, source and
/// aggregate reference counts. Output is sorted, so identical graphs export identical
/// bytes.
/// </summary>
public static class MemoryJsonlExporter
{
    /// <summary>
    /// m_CorrespondingSourceObject bookkeeping refs — 1,684 in the fixture — say nothing
    /// at asset level that source_prefab/prefab_instance don't already say, so the export
    /// drops them (per the Phase 1c decision; they stay in SQLite).
    /// </summary>
    private const string ExcludedRefKind = "corresponding_source";

    private static readonly string[] FullNodeSources = ["Assets", "Packages", "ProjectSettings"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Keep non-ASCII asset paths (the fixture has Cyrillic ones) human-readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record AssetRow(string Guid, string? Path, string AssetType, string Source)
    {
        public string Name => Path ?? Guid;
    }

    private sealed record EdgeRow(AssetRow From, AssetRow To, string RefKind, long RefCount);

    private sealed record EntityLine(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("entityType")] string EntityType,
        [property: JsonPropertyName("observations")] IReadOnlyList<string> Observations);

    private sealed record RelationLine(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("relationType")] string RelationType,
        [property: JsonPropertyName("ref_count")] long RefCount);

    public static ExportSummary Export(GraphDatabase db, TextWriter output, ExportOptions? options = null)
    {
        options ??= new ExportOptions();

        var assets = ReadAssets(db);
        var edges = ReadEdges(db, assets)
            .Where(e => IsFullNode(e.From, options))
            .OrderBy(e => e.From.Name, StringComparer.Ordinal)
            .ThenBy(e => e.To.Name, StringComparer.Ordinal)
            .ThenBy(e => e.RefKind, StringComparer.Ordinal)
            .ToList();

        var included = CollectIncludedAssets(assets.Values, edges, options);
        var (outgoing, incoming) = AggregateCounts(edges);

        foreach (var asset in included)
        {
            var line = new EntityLine("entity", asset.Name, asset.AssetType, BuildObservations(asset, outgoing, incoming));
            output.WriteLine(JsonSerializer.Serialize(line, JsonOptions));
        }

        foreach (var edge in edges)
        {
            var line = new RelationLine("relation", edge.From.Name, edge.To.Name, edge.RefKind, edge.RefCount);
            output.WriteLine(JsonSerializer.Serialize(line, JsonOptions));
        }

        // Only count assets the PackageCache rule dropped, not edge-less folders —
        // this drives the console hint suggesting --include-packages.
        var includedGuids = included.Select(a => a.Guid).ToHashSet(StringComparer.Ordinal);
        var skippedPackageCache = assets.Values.Count(a =>
            a.Source == "PackageCache" && !IsFullNode(a, options) && !includedGuids.Contains(a.Guid));
        return new ExportSummary(included.Count, edges.Count, skippedPackageCache);
    }

    private static bool IsFullNode(AssetRow asset, ExportOptions options) =>
        FullNodeSources.Contains(asset.Source)
        || (options.IncludePackageCache && asset.Source == "PackageCache");

    /// <summary>
    /// Full nodes keep unreferenced assets visible (that is what unused-asset queries
    /// need), except Folder assets without a single edge — pure noise. Everything else
    /// (PackageCache by default, BuiltIn, Unresolved) is included only when some
    /// exported relation touches it.
    /// </summary>
    private static List<AssetRow> CollectIncludedAssets(
        IEnumerable<AssetRow> assets, List<EdgeRow> edges, ExportOptions options)
    {
        var connected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            connected.Add(edge.From.Guid);
            connected.Add(edge.To.Guid);
        }

        return assets
            .Where(a => IsFullNode(a, options)
                ? a.AssetType != "Folder" || connected.Contains(a.Guid)
                : connected.Contains(a.Guid))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static (Dictionary<string, (long Refs, HashSet<string> Partners)> Outgoing,
        Dictionary<string, (long Refs, HashSet<string> Partners)> Incoming) AggregateCounts(List<EdgeRow> edges)
    {
        var outgoing = new Dictionary<string, (long, HashSet<string>)>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, (long, HashSet<string>)>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            Add(outgoing, edge.From.Guid, edge.To.Guid, edge.RefCount);
            Add(incoming, edge.To.Guid, edge.From.Guid, edge.RefCount);
        }

        return (outgoing, incoming);

        static void Add(Dictionary<string, (long Refs, HashSet<string> Partners)> counts,
            string key, string partner, long refCount)
        {
            if (!counts.TryGetValue(key, out var entry))
            {
                entry = (0, new HashSet<string>(StringComparer.Ordinal));
            }

            entry.Refs += refCount;
            entry.Partners.Add(partner);
            counts[key] = entry;
        }
    }

    private static IReadOnlyList<string> BuildObservations(
        AssetRow asset,
        Dictionary<string, (long Refs, HashSet<string> Partners)> outgoing,
        Dictionary<string, (long Refs, HashSet<string> Partners)> incoming)
    {
        var observations = new List<string>();
        if (asset.Path is not null && asset.Guid != asset.Path)
        {
            observations.Add($"guid: {asset.Guid}");
        }

        observations.Add($"source: {asset.Source}");
        if (asset.Path is null)
        {
            observations.Add("dangling reference: no asset with this GUID exists in the project");
        }

        if (outgoing.TryGetValue(asset.Guid, out var outCounts))
        {
            observations.Add($"outgoing: {outCounts.Refs} refs to {outCounts.Partners.Count} assets");
        }

        if (incoming.TryGetValue(asset.Guid, out var inCounts))
        {
            observations.Add($"incoming: {inCounts.Refs} refs from {inCounts.Partners.Count} assets");
        }

        return observations;
    }

    private static Dictionary<string, AssetRow> ReadAssets(GraphDatabase db)
    {
        var assets = new Dictionary<string, AssetRow>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT guid, path, asset_type, source FROM assets";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new AssetRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
            assets[row.Guid] = row;
        }

        return assets;
    }

    private static IEnumerable<EdgeRow> ReadEdges(GraphDatabase db, Dictionary<string, AssetRow> assets)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT from_guid, to_guid, ref_kind, ref_count
            FROM asset_edges
            WHERE ref_kind <> $excludedKind
            """;
        command.Parameters.AddWithValue("$excludedKind", ExcludedRefKind);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new EdgeRow(
                assets[reader.GetString(0)],
                assets[reader.GetString(1)],
                reader.GetString(2),
                reader.GetInt64(3));
        }
    }
}
