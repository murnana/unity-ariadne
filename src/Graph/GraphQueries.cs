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

/// <summary>One asset class (asset_type) of the ontology, with its population per source.</summary>
public sealed record OntologyClass(string AssetType, long Count, IReadOnlyDictionary<string, long> BySource);

/// <summary>A (domain type → range type) signature observed for a relation.</summary>
public sealed record RelationSignature(string FromType, string ToType, long Edges);

/// <summary>
/// One relation (ref_kind) of the ontology: how many asset-level edges carry it, and
/// which asset types it actually connects in this project (most frequent first).
/// </summary>
public sealed record OntologyRelation(
    string RefKind, long Edges, long RefCount, IReadOnlyList<RelationSignature> Signatures);

/// <summary>The vocabulary of a graph: its classes (asset types) and relations (ref kinds).</summary>
public sealed record Ontology(
    IReadOnlyList<OntologyClass> Classes, IReadOnlyList<OntologyRelation> Relations);

/// <summary>Which way a traversal step follows edges.</summary>
public enum TraversalDirection
{
    /// <summary>Follow edges forward: from an asset to what it references (dependencies).</summary>
    Out,

    /// <summary>Follow edges backward: from an asset to what references it (referrers).</summary>
    In,

    /// <summary>Follow edges either way.</summary>
    Both,
}

/// <summary>
/// One step of a typed path query. Edges are followed in <paramref name="Direction"/>,
/// restricted to <paramref name="Kinds"/> (null/empty = any kind), for 1 to
/// <paramref name="MaxHops"/> hops; only assets whose type is in <paramref name="Types"/>
/// (null/empty = any type) end the step. Intermediate assets of a multi-hop step are not
/// type-checked. <paramref name="IncludeSelf"/> also keeps the step's starting assets
/// (zero hops) when they match <paramref name="Types"/> — e.g. "the class and all its subclasses".
/// </summary>
public sealed record TraversalStep(
    TraversalDirection Direction,
    IReadOnlyCollection<string>? Kinds = null,
    IReadOnlyCollection<string>? Types = null,
    int MaxHops = 1,
    bool IncludeSelf = false);

/// <summary>One hop of a witness path: the edge kind, its direction, and the asset reached.</summary>
public sealed record PathHop(string RefKind, TraversalDirection Direction, string Asset);

/// <summary>An asset reached by a typed path query, with one witness path from a start asset.</summary>
public sealed record TraversalMatch(AssetNode Asset, string Start, IReadOnlyList<PathHop> Path);

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
    public static IReadOnlyList<AssetNode> SearchNodes(GraphDatabase db, string query, int limit) =>
        FindAssets(db, query, assetType: null, source: null, unreferencedOnly: false, limit);

    /// <summary>
    /// The ontology of the graph as it stands: every asset type with its count per source,
    /// and every ref kind with its edge count and the (from type → to type) signatures it
    /// connects. corresponding_source is excluded like everywhere else.
    /// </summary>
    public static Ontology DescribeOntology(GraphDatabase db)
    {
        var bySource = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText = "SELECT asset_type, source, COUNT(*) FROM assets GROUP BY asset_type, source";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                if (!bySource.TryGetValue(type, out var sources))
                {
                    bySource[type] = sources = new Dictionary<string, long>(StringComparer.Ordinal);
                }

                sources[reader.GetString(1)] = reader.GetInt64(2);
            }
        }

        var classes = bySource
            .Select(c => new OntologyClass(c.Key, c.Value.Values.Sum(), c.Value))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.AssetType, StringComparer.Ordinal)
            .ToList();

        var relations = new Dictionary<string, (long Edges, long RefCount, List<RelationSignature> Signatures)>(
            StringComparer.Ordinal);
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText =
                $"""
                SELECT e.ref_kind, f.asset_type, t.asset_type, COUNT(*), SUM(e.ref_count)
                FROM asset_edges e
                JOIN assets f ON f.guid = e.from_guid
                JOIN assets t ON t.guid = e.to_guid
                WHERE e.ref_kind <> '{ExcludedRefKind}'
                GROUP BY e.ref_kind, f.asset_type, t.asset_type
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kind = reader.GetString(0);
                var edges = reader.GetInt64(3);
                relations.TryGetValue(kind, out var entry);
                entry.Signatures ??= [];
                entry.Signatures.Add(new RelationSignature(reader.GetString(1), reader.GetString(2), edges));
                relations[kind] = (entry.Edges + edges, entry.RefCount + reader.GetInt64(4), entry.Signatures);
            }
        }

        return new Ontology(
            classes,
            relations
                .Select(r => new OntologyRelation(
                    r.Key,
                    r.Value.Edges,
                    r.Value.RefCount,
                    r.Value.Signatures
                        .OrderByDescending(s => s.Edges)
                        .ThenBy(s => s.FromType, StringComparer.Ordinal)
                        .ThenBy(s => s.ToType, StringComparer.Ordinal)
                        .ToList()))
                .OrderByDescending(r => r.Edges)
                .ThenBy(r => r.RefKind, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// Typed path query: starting from <paramref name="startGuids"/>, applies each step in
    /// turn (see <see cref="TraversalStep"/>); the assets left after the last step are the
    /// matches, each with the first witness path found (breadth-first, so a shortest one per
    /// step). Kind and type names compare case-insensitively. Results are ordered by name.
    /// </summary>
    public static IReadOnlyList<TraversalMatch> Traverse(
        GraphDatabase db, IReadOnlyCollection<string> startGuids, IReadOnlyList<TraversalStep> steps)
    {
        var (assets, edges) = LoadTypedGraph(db);
        var forward = edges.ToLookup(e => e.From, StringComparer.Ordinal);
        var reverse = edges.ToLookup(e => e.To, StringComparer.Ordinal);

        // guid → (start name, witness path so far)
        var current = new Dictionary<string, (string Start, List<PathHop> Path)>(StringComparer.Ordinal);
        foreach (var guid in startGuids)
        {
            if (assets.TryGetValue(guid, out var start))
            {
                current.TryAdd(guid, (start.Name, []));
            }
        }

        foreach (var step in steps)
        {
            var kinds = ToSet(step.Kinds);
            var types = ToSet(step.Types);
            var maxHops = Math.Clamp(step.MaxHops, 1, 10);
            var next = new Dictionary<string, (string Start, List<PathHop> Path)>(StringComparer.Ordinal);

            if (step.IncludeSelf)
            {
                foreach (var (guid, witness) in current)
                {
                    if (TypeMatches(assets[guid], types))
                    {
                        next.TryAdd(guid, witness);
                    }
                }
            }

            // Multi-source BFS. Each node is expanded once, with the witness it was first
            // given (a source keeps its own); a node is *reached* the first time an edge leads
            // to it — sources included, so "subclasses of any Script" finds scripts too.
            var expanded = new Dictionary<string, (string Start, List<PathHop> Path)>(current, StringComparer.Ordinal);
            var reachedOnce = new HashSet<string>(StringComparer.Ordinal);
            var frontier = current.Keys.OrderBy(g => assets[g].Name, StringComparer.Ordinal).ToList();
            for (var hop = 1; hop <= maxHops && frontier.Count > 0; hop++)
            {
                var discovered = new List<string>();
                foreach (var guid in frontier)
                {
                    var witness = expanded[guid];
                    foreach (var (neighbor, kind, direction) in Adjacent(guid, step.Direction, kinds))
                    {
                        if (!reachedOnce.Add(neighbor))
                        {
                            continue;
                        }

                        var reached = (witness.Start,
                            new List<PathHop>(witness.Path) { new(kind, direction, assets[neighbor].Name) });
                        if (expanded.TryAdd(neighbor, reached))
                        {
                            discovered.Add(neighbor);
                        }

                        if (TypeMatches(assets[neighbor], types))
                        {
                            next.TryAdd(neighbor, reached);
                        }
                    }
                }

                frontier = discovered;
            }

            current = next;
            if (current.Count == 0)
            {
                break;
            }
        }

        return current
            .Select(c => new TraversalMatch(assets[c.Key], c.Value.Start, c.Value.Path))
            .OrderBy(m => m.Asset.Name, StringComparer.Ordinal)
            .ToList();

        IEnumerable<(string Neighbor, string Kind, TraversalDirection Direction)> Adjacent(
            string guid, TraversalDirection direction, HashSet<string>? kinds)
        {
            if (direction != TraversalDirection.In)
            {
                foreach (var edge in forward[guid].Where(e => kinds is null || kinds.Contains(e.Kind)))
                {
                    yield return (edge.To, edge.Kind, TraversalDirection.Out);
                }
            }

            if (direction != TraversalDirection.Out)
            {
                foreach (var edge in reverse[guid].Where(e => kinds is null || kinds.Contains(e.Kind)))
                {
                    yield return (edge.From, edge.Kind, TraversalDirection.In);
                }
            }
        }

        static HashSet<string>? ToSet(IReadOnlyCollection<string>? values) =>
            values is null || values.Count == 0
                ? null
                : new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        static bool TypeMatches(AssetNode asset, HashSet<string>? types) =>
            types is null || types.Contains(asset.AssetType);
    }

    /// <summary>
    /// Typed node search: assets of the given type and/or source whose path contains
    /// <paramref name="query"/> (empty = any path; an exact GUID also matches), shortest
    /// paths first. With <paramref name="unreferencedOnly"/>, only assets that no edge points
    /// at (corresponding_source does not count).
    /// </summary>
    public static IReadOnlyList<AssetNode> FindAssets(
        GraphDatabase db, string query, string? assetType, string? source, bool unreferencedOnly, int limit)
    {
        var results = new List<AssetNode>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT guid, path, asset_type, source FROM assets a
            WHERE (guid = $name OR path LIKE '%' || $escaped || '%' ESCAPE '\' COLLATE NOCASE
                   OR ($name = '' AND path IS NULL))
              AND ($type IS NULL OR asset_type = $type COLLATE NOCASE)
              AND ($source IS NULL OR source = $source COLLATE NOCASE)
              AND (NOT $unreferenced OR NOT EXISTS (
                    SELECT 1 FROM asset_edges e
                    WHERE e.to_guid = a.guid AND e.ref_kind <> '{ExcludedRefKind}'))
            ORDER BY path IS NULL, length(path), path, guid
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$name", query);
        command.Parameters.AddWithValue("$escaped", EscapeLike(query));
        command.Parameters.AddWithValue("$type", (object?)assetType ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
        command.Parameters.AddWithValue("$unreferenced", unreferencedOnly);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadAssetNode(reader));
        }

        return results;
    }

    /// <summary>GUIDs of every asset of the given type (case-insensitive).</summary>
    public static IReadOnlyList<string> AssetGuidsOfType(GraphDatabase db, string assetType)
    {
        var results = new List<string>();
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT guid FROM assets WHERE asset_type = $type COLLATE NOCASE";
        command.Parameters.AddWithValue("$type", assetType);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static (Dictionary<string, AssetNode> Assets, List<(string From, string To, string Kind)> Edges)
        LoadTypedGraph(GraphDatabase db)
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

        var edges = new List<(string From, string To, string Kind)>();
        using (var command = db.Connection.CreateCommand())
        {
            // ORDER BY keeps witness paths deterministic.
            command.CommandText =
                $"""
                SELECT from_guid, to_guid, ref_kind FROM asset_edges
                WHERE ref_kind <> '{ExcludedRefKind}'
                ORDER BY from_guid, to_guid, ref_kind
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                edges.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        return (assets, edges);
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
