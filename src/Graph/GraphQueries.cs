using Microsoft.Data.Sqlite;

namespace GuidGraph.Graph;

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
/// type-checked. <paramref name="PathContains"/> further restricts the step's end assets to
/// paths containing any of the given substrings (case-insensitive) — folder or file-name clues.
/// <paramref name="IncludeSelf"/> also keeps the step's starting assets
/// (zero hops) when they match <paramref name="Types"/> — e.g. "the class and all its subclasses".
/// </summary>
public sealed record TraversalStep(
    TraversalDirection Direction,
    IReadOnlyCollection<string>? Kinds = null,
    IReadOnlyCollection<string>? Types = null,
    int MaxHops = 1,
    bool IncludeSelf = false,
    IReadOnlyCollection<string>? PathContains = null);

/// <summary>
/// An asset found by keyword search. <paramref name="Clues"/> says where each keyword hit
/// (file name, folder, type name, member name, doc comment), best hit per keyword.
/// </summary>
public sealed record KeywordMatch(
    AssetNode Asset, int MatchedKeywords, int Score, IReadOnlyList<KeywordClue> Clues,
    IReadOnlyList<string> DeclaredTypes, string? DocSnippet);

/// <summary>Where a keyword hit an asset.</summary>
public enum ClueKind
{
    FileName,
    TypeName,
    Folder,
    MemberName,
    DocComment,
}

/// <summary>
/// One keyword hit: the keyword, where it hit, and what it hit — the file name, the folder
/// name, or the symbol (with its <paramref name="SymbolKind"/>: class, method, ...).
/// </summary>
public sealed record KeywordClue(string Term, ClueKind Kind, string Target, string? SymbolKind = null)
{
    /// <summary>"boss → file name: Boss.cs", "enemies → folder: Enemies", "damage → method: X.TakeDamage", "怒り → doc of X".</summary>
    public override string ToString() => Kind switch
    {
        ClueKind.FileName => $"{Term} → file name: {Target}",
        ClueKind.Folder => $"{Term} → folder: {Target}",
        ClueKind.TypeName or ClueKind.MemberName => $"{Term} → {SymbolKind}: {Target}",
        _ => $"{Term} → doc of {Target}",
    };
}

/// <summary>A folder under the listed root, with how many assets of which types it holds (recursively).</summary>
public sealed record FolderSummary(string Path, long AssetCount, IReadOnlyDictionary<string, long> ByType);

/// <summary>One hop of a witness path: the edge kind, its direction, and the asset reached.</summary>
public sealed record PathHop(string RefKind, TraversalDirection Direction, string Asset);

/// <summary>An asset reached by a typed path query, with one witness path from the start asset it came from.</summary>
public sealed record TraversalMatch(AssetNode Asset, AssetNode Start, IReadOnlyList<PathHop> Path);

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

    /// <summary>
    /// Role of each prefab among <paramref name="guids"/>: "wrapper" = it contains at least one nested
    /// PrefabInstance (class 1001) and nothing of its own beyond GameObjects and Transforms (stripped
    /// placeholders of the nested prefabs do not count); "leaf" = anything else. Non-prefab guids are absent.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PrefabRoles(GraphDatabase db, IReadOnlyCollection<string> guids)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (guids.Count == 0)
        {
            return roles;
        }

        using var command = db.Connection.CreateCommand();
        var names = guids.Select((_, i) => $"$g{i}").ToList();
        command.CommandText =
            $"""
            SELECT a.guid,
                   SUM(o.class_id = 1001),
                   SUM(o.class_id NOT IN (1, 4, 224, 1001) AND o.is_stripped = 0)
            FROM assets a JOIN objects o ON o.asset_id = a.id
            WHERE a.asset_type = 'Prefab' AND a.guid IN ({string.Join(",", names)})
            GROUP BY a.guid
            """;
        var index = 0;
        foreach (var guid in guids)
        {
            command.Parameters.AddWithValue(names[index++], guid);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var nested = reader.GetInt64(1);
            var own = reader.GetInt64(2);
            roles[reader.GetString(0)] = nested > 0 && own == 0 ? "wrapper" : "leaf";
        }

        return roles;
    }

    /// <summary>A guess at which package an unresolved GUID belongs to, and what the guess rests on.</summary>
    public sealed record PackageHint(IReadOnlyList<string> PackageCandidates, string Basis);

    private const int MaxPackageCandidates = 3;

    /// <summary>
    /// Where an unresolved GUID might live, or null when nothing points anywhere. Two bases, tried in order:
    /// "referrer_package" — referrers that sit inside packages (the GUID was probably in that package or one it
    /// depends on); "referrer_dependencies" — referrers in Assets/ that also use assets of some packages, which are
    /// the likeliest owners of what is missing. Only a guess: the packages named are not verified to contain it.
    /// </summary>
    public static PackageHint? HintForUnresolved(GraphDatabase db, string guid)
    {
        var own = PackageCounts(db,
            """
            SELECT DISTINCT ref.path
            FROM refs r JOIN objects o ON o.id = r.from_object_id JOIN assets ref ON ref.id = o.asset_id
            WHERE r.to_guid = $guid AND ref.path IS NOT NULL
            """, guid);
        if (own.Count > 0)
        {
            return new PackageHint(own, "referrer_package");
        }

        var used = PackageCounts(db,
            """
            SELECT target.path
            FROM refs r JOIN objects o ON o.id = r.from_object_id
            JOIN assets target ON target.guid = r.to_guid
            WHERE target.path IS NOT NULL AND o.asset_id IN (
                SELECT o2.asset_id FROM refs r2 JOIN objects o2 ON o2.id = r2.from_object_id WHERE r2.to_guid = $guid)
            """, guid);
        return used.Count > 0 ? new PackageHint(used, "referrer_dependencies") : null;
    }

    /// <summary>Package names (Packages/name or Library/PackageCache/name@hash) of the paths a query returns, most frequent first.</summary>
    private static List<string> PackageCounts(GraphDatabase db, string sql, string guid)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guid", guid);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var package = PackageNameOf(reader.GetString(0));
            if (package is not null)
            {
                counts[package] = counts.GetValueOrDefault(package) + 1;
            }
        }

        return counts
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(MaxPackageCandidates).Select(c => c.Key).ToList();
    }

    /// <summary>"com.unity.ugui" for Packages/com.unity.ugui/... or Library/PackageCache/com.unity.ugui@abc/...; null otherwise.</summary>
    public static string? PackageNameOf(string path)
    {
        const string cache = "Library/PackageCache/";
        const string embedded = "Packages/";
        var rest = path.StartsWith(cache, StringComparison.Ordinal) ? path[cache.Length..]
            : path.StartsWith(embedded, StringComparison.Ordinal) ? path[embedded.Length..]
            : null;
        if (rest is null)
        {
            return null;
        }

        var name = rest.Split('/')[0];
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    /// <summary>Number of assets of <paramref name="assetType"/> whose source is any of <paramref name="sources"/>.</summary>
    public static long CountAssets(GraphDatabase db, string assetType, params string[] sources)
    {
        using var command = db.Connection.CreateCommand();
        var placeholders = string.Join(",", sources.Select((_, i) => $"$s{i}"));
        command.CommandText =
            $"SELECT COUNT(*) FROM assets WHERE asset_type = $type AND source IN ({placeholders})";
        command.Parameters.AddWithValue("$type", assetType);
        for (var i = 0; i < sources.Length; i++)
        {
            command.Parameters.AddWithValue($"$s{i}", sources[i]);
        }

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>
    /// Scenes of the build settings (EditorBuildSettings m_Scenes) in build order. The enabled flag is
    /// not stored in the graph, so disabled entries are listed too. Unresolved scenes are skipped.
    /// </summary>
    public static IReadOnlyList<string> BuildSceneOrder(GraphDatabase db)
    {
        var scenes = new List<(int Index, string Path)>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT r.property_path, a.path
            FROM refs r JOIN assets a ON a.guid = r.to_guid
            WHERE r.ref_kind = 'build_settings' AND r.property_path LIKE 'm\_Scenes[%' ESCAPE '\'
              AND a.path IS NOT NULL
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var property = reader.GetString(0);
            var open = property.IndexOf('[');
            var close = property.IndexOf(']');
            if (open >= 0 && close > open && int.TryParse(property[(open + 1)..close], out var index))
            {
                scenes.Add((index, reader.GetString(1)));
            }
        }

        return scenes.OrderBy(s => s.Index).Select(s => s.Path).ToList();
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
    /// matches, each with the first witness path found. The search is breadth-first and
    /// visits starts in the given order, so an earlier start wins ties — callers order
    /// starts by relevance. Kind and type names compare case-insensitively. Matches come
    /// back in discovery order (by start order, then hop distance).
    /// </summary>
    public static IReadOnlyList<TraversalMatch> Traverse(
        GraphDatabase db, IReadOnlyList<string> startGuids, IReadOnlyList<TraversalStep> steps)
    {
        var (assets, edges) = LoadTypedGraph(db);
        var forward = edges.ToLookup(e => e.From, StringComparer.Ordinal);
        var reverse = edges.ToLookup(e => e.To, StringComparer.Ordinal);

        // Ordered frontier of (guid, start guid, witness path so far).
        var current = new List<(string Guid, string Start, List<PathHop> Path)>();
        var seenStarts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var guid in startGuids)
        {
            if (assets.ContainsKey(guid) && seenStarts.Add(guid))
            {
                current.Add((guid, guid, []));
            }
        }

        foreach (var step in steps)
        {
            var kinds = ToSet(step.Kinds);
            var types = ToSet(step.Types);
            var pathParts = step.PathContains?.Where(p => p.Length > 0).ToList();
            var maxHops = Math.Clamp(step.MaxHops, 1, 10);
            var next = new List<(string Guid, string Start, List<PathHop> Path)>();
            var inNext = new HashSet<string>(StringComparer.Ordinal);

            if (step.IncludeSelf)
            {
                foreach (var entry in current)
                {
                    if (EndMatches(assets[entry.Guid], types, pathParts) && inNext.Add(entry.Guid))
                    {
                        next.Add(entry);
                    }
                }
            }

            // Multi-source BFS. Each node is expanded once, with the witness it was first
            // given (a source keeps its own); a node is *reached* the first time an edge leads
            // to it — sources included, so "subclasses of any Script" finds scripts too.
            var expanded = current.ToDictionary(c => c.Guid, c => (c.Start, c.Path), StringComparer.Ordinal);
            var reachedOnce = new HashSet<string>(StringComparer.Ordinal);
            var frontier = current.Select(c => c.Guid).ToList();
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

                        var path = new List<PathHop>(witness.Path) { new(kind, direction, assets[neighbor].Name) };
                        if (expanded.TryAdd(neighbor, (witness.Start, path)))
                        {
                            discovered.Add(neighbor);
                        }

                        if (EndMatches(assets[neighbor], types, pathParts) && inNext.Add(neighbor))
                        {
                            next.Add((neighbor, witness.Start, path));
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
            .Select(c => new TraversalMatch(assets[c.Guid], assets[c.Start], c.Path))
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

        static bool EndMatches(AssetNode asset, HashSet<string>? types, List<string>? pathParts) =>
            (types is null || types.Contains(asset.AssetType))
            && PathMatches(asset, pathParts);
    }

    /// <summary>GUIDs that at least one asset-level edge points at (corresponding_source does not count).</summary>
    public static HashSet<string> ReferencedGuids(GraphDatabase db)
    {
        var guids = new HashSet<string>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = $"SELECT DISTINCT to_guid FROM asset_edges WHERE ref_kind <> '{ExcludedRefKind}'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            guids.Add(reader.GetString(0));
        }

        return guids;
    }

    /// <summary>True when no parts are given, or the asset's path contains any of them (folded, see <see cref="TextMatch"/>).</summary>
    internal static bool PathMatches(AssetNode asset, IReadOnlyCollection<string>? parts) =>
        parts is null || parts.Count == 0
        || (asset.Path is not null
            && parts.Any(p => p.Length > 0 && TextMatch.Contains(asset.Path, p)));

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

    /// <summary>
    /// GUIDs of every asset of the given type (case-insensitive), in path order, optionally only those
    /// whose path contains any of <paramref name="pathContains"/> (case-insensitive).
    /// </summary>
    public static IReadOnlyList<string> AssetGuidsOfType(
        GraphDatabase db, string assetType, IReadOnlyCollection<string>? pathContains = null)
    {
        var parts = pathContains?.Where(p => p.Length > 0).ToList() ?? [];
        var results = new List<string>();
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT guid, path FROM assets WHERE asset_type = $type COLLATE NOCASE ORDER BY path IS NULL, path, guid";
        command.Parameters.AddWithValue("$type", assetType);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (parts.Count == 0
                || (path is not null && parts.Any(p => TextMatch.Contains(path, p))))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private const int FileNameWeight = 4;
    private const int TypeNameWeight = 3;
    private const int FolderWeight = 2;
    private const int MemberNameWeight = 2;
    private const int DocWeight = 1;
    private const int DocSnippetRadius = 60;

    private static readonly HashSet<string> TypeSymbolKinds = new(StringComparer.Ordinal)
    {
        "class", "struct", "interface", "enum", "record", "delegate", "type",
    };

    /// <summary>
    /// Keyword search over everything that names or describes an asset: its file name,
    /// the folders on its path, and — for scripts — declared type names, documented member
    /// names and XML doc comment text (code_symbols). Each keyword is a case-insensitive
    /// substring and counts once, at its strongest hit (file name 4, type name 3, folder 2,
    /// member name 2, doc comment 1). Ranked by number of keywords hit, then score, then
    /// shorter path. <paramref name="matchAll"/> keeps only assets hitting every keyword.
    /// </summary>
    public static IReadOnlyList<KeywordMatch> SearchByKeywords(
        GraphDatabase db, IReadOnlyList<string> keywords, string? assetType, string? source, bool matchAll)
    {
        var candidates = new List<AssetNode>();
        using (var command = db.Connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT guid, path, asset_type, source FROM assets
                WHERE path IS NOT NULL
                  AND ($type IS NULL OR asset_type = $type COLLATE NOCASE)
                  AND ($source IS NULL OR source = $source COLLATE NOCASE)
                """;
            command.Parameters.AddWithValue("$type", (object?)assetType ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                candidates.Add(ReadAssetNode(reader));
            }
        }

        return RankByKeywords(db, candidates, keywords, matchAll);
    }

    /// <summary>Splits keywords on whitespace, drops blanks and duplicates (case-insensitive).</summary>
    public static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string> keywords) =>
        keywords
            .SelectMany(k => k.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The keyword ranking of <see cref="SearchByKeywords"/> over a given candidate set
    /// (unresolved assets never match: they have no name).
    /// </summary>
    public static IReadOnlyList<KeywordMatch> RankByKeywords(
        GraphDatabase db, IEnumerable<AssetNode> candidates, IReadOnlyList<string> keywords, bool matchAll)
    {
        var terms = NormalizeKeywords(keywords).ToList();
        if (terms.Count == 0)
        {
            return [];
        }

        var symbols = LoadCodeSymbols(db);
        var results = new List<KeywordMatch>();
        foreach (var asset in candidates.Where(a => a.Path is not null))
        {
            symbols.TryGetValue(asset.Guid, out var assetSymbols);
            var match = MatchKeywords(asset, assetSymbols ?? [], terms);
            if (match is not null && (!matchAll || match.MatchedKeywords == terms.Count))
            {
                results.Add(match);
            }
        }

        return results
            .OrderByDescending(m => m.MatchedKeywords)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.Asset.Name.Length)
            .ThenBy(m => m.Asset.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>code_symbols grouped by asset GUID, in source order (empty before schema v3).</summary>
    internal static Dictionary<string, List<(string Symbol, string Kind, string? Doc)>> LoadCodeSymbols(GraphDatabase db)
    {
        var symbols = new Dictionary<string, List<(string Symbol, string Kind, string? Doc)>>(StringComparer.Ordinal);
        if (!db.HasCodeSymbols)
        {
            return symbols;
        }

        using var command = db.Connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.guid, s.symbol, s.kind, s.doc FROM code_symbols s
            JOIN assets a ON a.id = s.asset_id
            ORDER BY s.rowid
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            if (!symbols.TryGetValue(guid, out var list))
            {
                symbols[guid] = list = [];
            }

            list.Add((reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return symbols;
    }

    /// <summary>Names of the types a script declares (code_symbols), in source order.</summary>
    internal static IReadOnlyList<string> DeclaredTypes(List<(string Symbol, string Kind, string? Doc)> symbols) =>
        symbols.Where(s => TypeSymbolKinds.Contains(s.Kind)).Select(s => s.Symbol).ToList();

    private static KeywordMatch? MatchKeywords(
        AssetNode asset, List<(string Symbol, string Kind, string? Doc)> symbols, List<string> terms)
    {
        var path = asset.Path!;
        var slash = path.LastIndexOf('/');
        var fileName = path[(slash + 1)..];
        var folders = slash < 0 ? [] : path[..slash].Split('/');

        // Fold every searchable text once per asset (TextMatch: width, case, kana).
        var foldedFileName = TextMatch.Fold(fileName);
        var foldedFolders = folders.Select(TextMatch.Fold).ToList();
        var foldedSymbols = symbols
            .Select(s => (Name: TextMatch.Fold(s.Symbol[(s.Symbol.LastIndexOf('.') + 1)..]),
                Doc: s.Doc is null ? null : TextMatch.Fold(s.Doc)))
            .ToList();

        var clues = new List<KeywordClue>();
        var matched = 0;
        var score = 0;
        string? snippet = null;
        foreach (var term in terms)
        {
            var foldedTerm = TextMatch.Fold(term);
            var weight = 0;
            KeywordClue? clue = null;
            if (TextMatch.IndexOf(foldedFileName, foldedTerm) >= 0)
            {
                (weight, clue) = (FileNameWeight, new KeywordClue(term, ClueKind.FileName, fileName));
            }

            for (var i = 0; i < symbols.Count; i++)
            {
                var (symbol, kind, doc) = symbols[i];
                var (name, foldedDoc) = foldedSymbols[i];
                var isType = TypeSymbolKinds.Contains(kind);
                var docIndex = foldedDoc is null ? -1 : TextMatch.IndexOf(foldedDoc, foldedTerm);
                if (isType && weight < TypeNameWeight && TextMatch.IndexOf(name, foldedTerm) >= 0)
                {
                    (weight, clue) = (TypeNameWeight, new KeywordClue(term, ClueKind.TypeName, symbol, kind));
                }
                else if (!isType && weight < MemberNameWeight && TextMatch.IndexOf(name, foldedTerm) >= 0)
                {
                    (weight, clue) = (MemberNameWeight, new KeywordClue(term, ClueKind.MemberName, symbol, kind));
                }
                else if (docIndex >= 0 && weight < DocWeight)
                {
                    (weight, clue) = (DocWeight, new KeywordClue(term, ClueKind.DocComment, symbol, kind));
                }

                if (docIndex >= 0 && snippet is null)
                {
                    snippet = $"{symbol}: {Excerpt(TextMatch.Display(doc!), docIndex, foldedTerm.Length)}";
                }
            }

            if (weight < FolderWeight)
            {
                var folderIndex = foldedFolders.FindIndex(f => TextMatch.IndexOf(f, foldedTerm) >= 0);
                if (folderIndex >= 0)
                {
                    (weight, clue) = (FolderWeight, new KeywordClue(term, ClueKind.Folder, folders[folderIndex]));
                }
            }

            if (clue is not null)
            {
                matched++;
                score += weight;
                clues.Add(clue);
            }
        }

        if (matched == 0)
        {
            return null;
        }

        var declared = symbols.Where(s => TypeSymbolKinds.Contains(s.Kind)).Select(s => s.Symbol).Take(3).ToList();
        return new KeywordMatch(asset, matched, score, clues, declared, snippet);
    }

    /// <summary>
    /// The doc text around a hit. <paramref name="doc"/> is the NFKC (display) form, in which
    /// <paramref name="index"/> (found in the folded form, same length) is valid.
    /// </summary>
    private static string Excerpt(string doc, int index, int length)
    {
        var start = Math.Max(0, index - DocSnippetRadius);
        var end = Math.Min(doc.Length, index + length + DocSnippetRadius);
        return (start > 0 ? "…" : "") + doc[start..end] + (end < doc.Length ? "…" : "");
    }

    /// <summary>
    /// The folder taxonomy under <paramref name="root"/> ("" = the top level: Assets,
    /// Packages, ProjectSettings, …): every folder <paramref name="depth"/> levels down,
    /// with the number of (non-folder) assets below it and their counts per type.
    /// </summary>
    public static IReadOnlyList<FolderSummary> ListFolders(GraphDatabase db, string root, int depth)
    {
        var prefix = root.Trim('/');
        var groups = new SortedDictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT path, asset_type FROM assets WHERE path IS NOT NULL AND asset_type <> 'Folder'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            string rest;
            if (prefix.Length == 0)
            {
                rest = path;
            }
            else if (path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                rest = path[(prefix.Length + 1)..];
            }
            else
            {
                continue;
            }

            var segments = rest.Split('/');
            if (segments.Length < 2)
            {
                continue; // a file directly in root, not inside a sub-folder
            }

            var folder = string.Join("/", segments.Take(Math.Min(depth, segments.Length - 1)));
            var key = prefix.Length == 0 ? folder : $"{path[..prefix.Length]}/{folder}";
            if (!groups.TryGetValue(key, out var byType))
            {
                groups[key] = byType = new Dictionary<string, long>(StringComparer.Ordinal);
            }

            var type = reader.GetString(1);
            byType[type] = byType.GetValueOrDefault(type) + 1;
        }

        return groups
            .Select(g => new FolderSummary(g.Key, g.Value.Values.Sum(), g.Value))
            .ToList();
    }

    internal static (Dictionary<string, AssetNode> Assets, List<(string From, string To, string Kind)> Edges)
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
