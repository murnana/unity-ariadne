using System.Text.Json;

namespace GuidGraph.Graph;

/// <summary>Arguments of the MCP <c>unreachable</c> tool; null or empty means the default.</summary>
public sealed record UnreachableQuery
{
    public string[]? Types { get; init; }
    public string[]? Roots { get; init; }
    public string? Source { get; init; }
    public bool BuildOnly { get; init; }
    public string[]? Kinds { get; init; }
    public string[]? PathContains { get; init; }
    public int Limit { get; init; } = UnreachableSearch.DefaultLimit;
}

/// <summary>
/// Assets that no root (scenes by default) reaches through any chain of references. One breadth-first pass over
/// the asset-level edges answers a whole-project question that would otherwise need every candidate listed and
/// checked one by one. Only static references exist in the graph, so the answer always says what it cannot see.
/// </summary>
public static class UnreachableSearch
{
    public const int DefaultLimit = 50;
    private const int MaxReferrerSamples = 3;

    private const string LimitNote =
        "static references only: loads by name or path (Resources.Load, Addressables keys, scene or asset names in " +
        "strings, reflection) are not detected, so an unreachable asset is not proven unused";

    private const string BuildOnlyNote =
        "build_only uses every scene listed in the build settings, disabled entries included";

    /// <summary>Runs the query and returns the JSON answer (an <c>error</c> object when there is no root).</summary>
    public static string RunJson(GraphDatabase db, UnreachableQuery query)
    {
        var types = OrDefault(query.Types, "Prefab");
        var rootTypes = OrDefault(query.Roots, "Scene");
        var source = string.IsNullOrWhiteSpace(query.Source) ? "Assets" : query.Source;
        var warnings = GraphSearch.UnknownTerms(
            [new TraversalStep(TraversalDirection.Out, query.Kinds, types, 1)],
            rootTypes, GraphQueries.DescribeOntology(db));

        var roots = new List<AssetNode>();
        foreach (var type in rootTypes)
        {
            var found = GraphQueries.FindAssets(db, "", type, source, unreferencedOnly: false, int.MaxValue);
            if (query.BuildOnly && type.Equals("Scene", StringComparison.OrdinalIgnoreCase))
            {
                var inBuild = GraphQueries.BuildSceneOrder(db).ToHashSet(StringComparer.Ordinal);
                found = found.Where(a => a.Path is not null && inBuild.Contains(a.Path)).ToList();
            }

            roots.AddRange(found);
        }

        var buildOnlyApplies = query.BuildOnly && rootTypes.Contains("Scene", StringComparer.OrdinalIgnoreCase);
        if (query.BuildOnly && !buildOnlyApplies)
        {
            warnings.Add("build_only was ignored: it only narrows Scene roots");
        }

        if (roots.Count == 0)
        {
            return Serialize(new
            {
                Error = "no_roots",
                Roots = rootTypes,
                Hint = buildOnlyApplies
                    ? "No root asset: no scene of this source is listed in the build settings; retry with build_only false."
                    : "No asset of these root types in this source; pick roots and source from describe_ontology.",
                Warnings = NullIfEmpty(warnings),
            });
        }

        var kinds = query.Kinds is { Length: > 0 }
            ? new HashSet<string>(query.Kinds, StringComparer.OrdinalIgnoreCase)
            : null;
        var (assets, edges) = GraphQueries.LoadTypedGraph(db);
        var forward = edges
            .Where(e => kinds is null || kinds.Contains(e.Kind))
            .ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);

        var reached = new HashSet<string>(roots.Select(r => r.Guid), StringComparer.Ordinal);
        var pending = new Queue<string>(reached);
        while (pending.TryDequeue(out var guid))
        {
            foreach (var next in forward[guid])
            {
                if (reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        var typeSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        var candidates = assets.Values
            .Where(a => typeSet.Contains(a.AssetType)
                        && a.Source.Equals(source, StringComparison.OrdinalIgnoreCase)
                        && GraphQueries.PathMatches(a, query.PathContains))
            .ToList();
        var unreached = candidates
            .Where(a => !reached.Contains(a.Guid))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();

        var listedGuids = unreached.Select(a => a.Guid).ToHashSet(StringComparer.Ordinal);
        var referrers = edges
            .Where(e => listedGuids.Contains(e.To) && e.From != e.To && (kinds is null || kinds.Contains(e.Kind)))
            .ToLookup(e => e.To, e => e.From, StringComparer.Ordinal);
        var unsupported = types.Concat(rootTypes)
            .Where(t => !OntologyGlossary.IsParsed(t)).Distinct().Order(StringComparer.Ordinal).ToList();

        var limit = Math.Clamp(query.Limit, 1, GraphSearch.MaxLimit);
        var count = Math.Min(limit, unreached.Count);
        while (true)
        {
            var allWarnings = new List<string>(warnings);
            if (count < Math.Min(limit, unreached.Count))
            {
                allWarnings.Add($"output capped at about {GraphSearch.MaxOutputChars / 1000}k characters: showing the " +
                                $"first {count} matches; narrow the query (types, path_contains)");
            }

            var json = Serialize(new
            {
                Types = types,
                Roots = new { Types = rootTypes, Count = roots.Count, BuildOnly = buildOnlyApplies ? (bool?)true : null },
                Candidates = candidates.Count,
                Reachable = candidates.Count - unreached.Count,
                Unreachable = unreached.Count,
                Matches = unreached.Take(count).Select(asset =>
                {
                    var from = referrers[asset.Guid].Distinct()
                        .Select(g => assets[g].Name).Order(StringComparer.Ordinal).ToList();
                    return new
                    {
                        Name = asset.Name,
                        Type = asset.AssetType,
                        Referrers = from.Count,
                        ReferrerSamples = NullIfEmpty(from.Take(MaxReferrerSamples).ToList()),
                        Notes = NullIfEmpty(OntologyGlossary.PathNotes(asset)),
                    };
                }).ToList(),
                Truncated = count < unreached.Count ? (bool?)true : null,
                Unsupported = NullIfEmpty(unsupported),
                Warnings = NullIfEmpty(allWarnings),
                Notes = buildOnlyApplies ? new[] { LimitNote, BuildOnlyNote } : [LimitNote],
            });
            if (json.Length <= GraphSearch.MaxOutputChars || count <= 1)
            {
                return json;
            }

            count = Math.Max(1, (int)(count * 0.97 * GraphSearch.MaxOutputChars / json.Length));
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, GraphSearch.JsonOptions);

    private static string[] OrDefault(string[]? values, string fallback) =>
        values is { Length: > 0 } ? values : [fallback];

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list;
}
