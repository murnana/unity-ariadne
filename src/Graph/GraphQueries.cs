using Microsoft.Data.Sqlite;

namespace UnityAriadne.Graph;

/// <summary>One asset row as query results present it.</summary>
public sealed record AssetNode(string Guid, string? Path, string AssetType, string Source)
{
    /// <summary>Display name: the project path, or the GUID while unresolved.</summary>
    public string Name => Path ?? Guid;
}

/// <summary>One asset-level incoming edge (who references the target and how).</summary>
public sealed record IncomingReference(AssetNode Referrer, string RefKind, long RefCount);

/// <summary>One asset-level outgoing edge (what the source references and how).</summary>
public sealed record OutgoingReference(AssetNode Target, string RefKind, long RefCount);

/// <summary>
/// An asset that (transitively) references the impact-analysis target.
/// <paramref name="Distance"/> is the number of asset-level hops;
/// <paramref name="Via"/> is the asset one hop closer through which it was first reached
/// (the target itself at distance 1).
/// </summary>
public sealed record ImpactedAsset(AssetNode Asset, int Distance, string Via);

/// <summary>One edge of the neighborhood subgraph returned by GetNeighbors.</summary>
public sealed record NeighborEdge(string FromName, string ToName, string RefKind, long RefCount);

/// <summary>
/// The neighborhood of an asset: every asset reachable within N undirected hops,
/// plus all asset-level edges among those assets (the induced subgraph).
/// </summary>
public sealed record Neighborhood(
    IReadOnlyList<AssetNode> Assets, IReadOnlyList<NeighborEdge> Edges);

/// <summary>
/// Read-only asset-level graph queries for the MCP server (Phase 4). All queries work
/// on the asset_edges aggregation and exclude corresponding_source bookkeeping refs,
/// mirroring the memory.jsonl export (the Phase 1c decision).
/// </summary>
public static class GraphQueries
{
    private const string ExcludedRefKind = "corresponding_source";

    /// <summary>
    /// Resolves a user-supplied name to asset rows. Tried in order, first hit wins:
    /// exact GUID, exact path, then case-insensitive path with a segment-boundary
    /// suffix match (so "Fog.mat" or "Materials/Fog.mat" work). Multiple hits mean
    /// the name was ambiguous — callers should list the candidates.
    /// </summary>
    public static IReadOnlyList<AssetNode> ResolveAssets(GraphDatabase db, string name)
    {
        var byGuid = QueryAssets(db, "WHERE guid = $name", name);
        if (byGuid.Count > 0)
        {
            return byGuid;
        }

        var byPath = QueryAssets(db, "WHERE path = $name", name);
        if (byPath.Count > 0)
        {
            return byPath;
        }

        return QueryAssets(db,
            """
            WHERE path = $name COLLATE NOCASE
               OR path LIKE '%/' || $escaped ESCAPE '\' COLLATE NOCASE
            """,
            name);
    }

    /// <summary>All assets that directly reference <paramref name="guid"/>, most refs first.</summary>
    public static IReadOnlyList<IncomingReference> FindReferences(GraphDatabase db, string guid)
    {
        var results = new List<IncomingReference>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT a.guid, a.path, a.asset_type, a.source, e.ref_kind, e.ref_count
            FROM asset_edges e
            JOIN assets a ON a.guid = e.from_guid
            WHERE e.to_guid = $guid AND e.ref_kind <> '{ExcludedRefKind}'
            ORDER BY e.ref_count DESC, a.path, e.ref_kind
            """;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new IncomingReference(ReadAssetNode(reader), reader.GetString(4), reader.GetInt64(5)));
        }

        return results;
    }

    /// <summary>All assets that <paramref name="guid"/> directly references, most refs first.</summary>
    public static IReadOnlyList<OutgoingReference> FindDependencies(GraphDatabase db, string guid)
    {
        var results = new List<OutgoingReference>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT a.guid, a.path, a.asset_type, a.source, e.ref_kind, e.ref_count
            FROM asset_edges e
            JOIN assets a ON a.guid = e.to_guid
            WHERE e.from_guid = $guid AND e.ref_kind <> '{ExcludedRefKind}'
            ORDER BY e.ref_count DESC, a.path, e.ref_kind
            """;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new OutgoingReference(ReadAssetNode(reader), reader.GetString(4), reader.GetInt64(5)));
        }

        return results;
    }

    /// <summary>
    /// Breadth-first walk over *reverse* edges: everything that would be affected by a
    /// change to <paramref name="guid"/>, up to <paramref name="maxDepth"/> hops away.
    /// Results are ordered by distance, then path.
    /// </summary>
    public static IReadOnlyList<ImpactedAsset> ImpactAnalysis(GraphDatabase db, string guid, int maxDepth)
    {
        var (assets, _, reverse) = LoadGraph(db);
        if (!assets.ContainsKey(guid))
        {
            return [];
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { guid };
        var frontier = new List<string> { guid };
        var results = new List<ImpactedAsset>();

        for (var distance = 1; distance <= maxDepth && frontier.Count > 0; distance++)
        {
            var discovered = new List<(string Guid, string Via)>();
            foreach (var current in frontier)
            {
                if (!reverse.TryGetValue(current, out var referrers))
                {
                    continue;
                }

                foreach (var referrer in referrers)
                {
                    if (visited.Add(referrer))
                    {
                        discovered.Add((referrer, assets[current].Name));
                    }
                }
            }

            results.AddRange(discovered
                .Select(d => new ImpactedAsset(assets[d.Guid], distance, d.Via))
                .OrderBy(i => i.Asset.Name, StringComparer.Ordinal));
            frontier = discovered.Select(d => d.Guid).ToList();
        }

        return results;
    }

    /// <summary>
    /// Every asset within <paramref name="hops"/> undirected hops of
    /// <paramref name="guid"/> (including itself), plus all edges among them.
    /// </summary>
    public static Neighborhood GetNeighbors(GraphDatabase db, string guid, int hops)
    {
        var (assets, forward, reverse) = LoadGraph(db);
        if (!assets.ContainsKey(guid))
        {
            return new Neighborhood([], []);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { guid };
        var frontier = new List<string> { guid };

        for (var distance = 1; distance <= hops && frontier.Count > 0; distance++)
        {
            var discovered = new List<string>();
            foreach (var current in frontier)
            {
                foreach (var neighbor in EnumerateNeighbors(current, forward, reverse))
                {
                    if (visited.Add(neighbor))
                    {
                        discovered.Add(neighbor);
                    }
                }
            }

            frontier = discovered;
        }

        var nodes = visited
            .Select(g => assets[g])
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
        var edges = ReadEdgesAmong(db, visited, assets);
        return new Neighborhood(nodes, edges);
    }

    /// <summary>
    /// Case-insensitive substring search over asset paths (or an exact GUID match),
    /// shortest paths first. LIKE wildcards in the query are treated literally.
    /// </summary>
    public static IReadOnlyList<AssetNode> SearchNodes(GraphDatabase db, string query, int limit)
    {
        var results = new List<AssetNode>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT guid, path, asset_type, source FROM assets
            WHERE guid = $name OR path LIKE '%' || $escaped || '%' ESCAPE '\' COLLATE NOCASE
            ORDER BY length(path), path
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$name", query);
        command.Parameters.AddWithValue("$escaped", EscapeLike(query));
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadAssetNode(reader));
        }

        return results;
    }

    private static IEnumerable<string> EnumerateNeighbors(
        string guid,
        Dictionary<string, List<string>> forward,
        Dictionary<string, List<string>> reverse)
    {
        if (forward.TryGetValue(guid, out var targets))
        {
            foreach (var target in targets)
            {
                yield return target;
            }
        }

        if (reverse.TryGetValue(guid, out var referrers))
        {
            foreach (var referrer in referrers)
            {
                yield return referrer;
            }
        }
    }

    /// <summary>
    /// Loads the whole asset-level graph into adjacency lists. asset_edges is a few
    /// thousand rows even for the full fixture with PackageCache, so per-call loading
    /// stays in the milliseconds and the server never holds a stale cache.
    /// </summary>
    private static (Dictionary<string, AssetNode> Assets,
        Dictionary<string, List<string>> Forward,
        Dictionary<string, List<string>> Reverse) LoadGraph(GraphDatabase db)
    {
        var assets = new Dictionary<string, AssetNode>(StringComparer.Ordinal);
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText = "SELECT guid, path, asset_type, source FROM assets";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var node = ReadAssetNode(reader);
                assets[node.Guid] = node;
            }
        }

        var forward = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText =
                $"""
                SELECT DISTINCT from_guid, to_guid FROM asset_edges
                WHERE ref_kind <> '{ExcludedRefKind}'
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var from = reader.GetString(0);
                var to = reader.GetString(1);
                Add(forward, from, to);
                Add(reverse, to, from);
            }
        }

        return (assets, forward, reverse);

        static void Add(Dictionary<string, List<string>> adjacency, string key, string value)
        {
            if (!adjacency.TryGetValue(key, out var list))
            {
                adjacency[key] = list = [];
            }

            list.Add(value);
        }
    }

    private static IReadOnlyList<NeighborEdge> ReadEdgesAmong(
        GraphDatabase db, HashSet<string> guids, Dictionary<string, AssetNode> assets)
    {
        var edges = new List<NeighborEdge>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT from_guid, to_guid, ref_kind, ref_count FROM asset_edges
            WHERE ref_kind <> '{ExcludedRefKind}'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var from = reader.GetString(0);
            var to = reader.GetString(1);
            if (guids.Contains(from) && guids.Contains(to))
            {
                edges.Add(new NeighborEdge(
                    assets[from].Name, assets[to].Name, reader.GetString(2), reader.GetInt64(3)));
            }
        }

        return edges
            .OrderBy(e => e.FromName, StringComparer.Ordinal)
            .ThenBy(e => e.ToName, StringComparer.Ordinal)
            .ThenBy(e => e.RefKind, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<AssetNode> QueryAssets(GraphDatabase db, string whereClause, string name)
    {
        var results = new List<AssetNode>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT guid, path, asset_type, source FROM assets
            {whereClause}
            ORDER BY path
            """;
        command.Parameters.AddWithValue("$name", name);
        if (whereClause.Contains("$escaped"))
        {
            command.Parameters.AddWithValue("$escaped", EscapeLike(name));
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadAssetNode(reader));
        }

        return results;
    }

    private static AssetNode ReadAssetNode(SqliteDataReader reader) =>
        new(reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));

    /// <summary>Asset paths may legitimately contain % or _ — treat them literally.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
