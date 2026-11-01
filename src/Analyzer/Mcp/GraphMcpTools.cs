using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using UnityAriadne.Graph;

namespace UnityAriadne.Analyzer.Mcp;

/// <summary>Tells <see cref="GraphMcpTools"/> which database file to query.</summary>
public sealed record GraphMcpOptions(string DbPath);

/// <summary>
/// The five MCP tools of the serve command (Phase 4). Each call opens the database
/// read-only and closes it again: a single shared SqliteConnection is not safe under
/// concurrent tool calls, and a fresh open always sees the current file even after
/// `analyze` deleted and rebuilt it. Results are compact JSON; list-shaped answers are
/// capped so a hub asset cannot blow up the context window of the calling agent.
/// </summary>
[McpServerToolType]
public sealed class GraphMcpTools(GraphMcpOptions options)
{
    private const int MaxListedResults = 200;
    private const int MaxAmbiguousCandidates = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Same rationale as the jsonl export: keep non-ASCII asset paths readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "find_references")]
    [Description("Lists every asset that directly references the given asset (reverse dependencies) " +
                 "with reference kind and count. Accepts a project-relative path (Assets/...), a file " +
                 "name unique in the project, or a GUID.")]
    public string FindReferences(
        [Description("Asset path, file name, or GUID")] string name)
        => WithResolvedAsset(name, (db, asset) =>
        {
            var references = GraphQueries.FindReferences(db, asset.Guid);
            return Serialize(new
            {
                Asset = Describe(asset),
                TotalReferrers = references.Count,
                References = references
                    .Take(MaxListedResults)
                    .Select(r => new
                    {
                        From = r.Referrer.Name,
                        Kind = r.RefKind,
                        RefCount = r.RefCount,
                        Source = r.Referrer.Source,
                    })
                    .ToList(),
                Truncated = references.Count > MaxListedResults ? (bool?)true : null,
            });
        });

    [McpServerTool(Name = "impact_analysis")]
    [Description("Walks reverse dependencies transitively: everything that would be affected by a " +
                 "change to the given asset, grouped by distance (1 = direct referrer). 'via' names " +
                 "the asset one hop closer through which each result was first reached.")]
    public string ImpactAnalysis(
        [Description("Asset path, file name, or GUID")] string name,
        [Description("Maximum number of hops to walk (default 3, max 10)")] int depth = 3)
        => WithResolvedAsset(name, (db, asset) =>
        {
            var clampedDepth = Math.Clamp(depth, 1, 10);
            var impacted = GraphQueries.ImpactAnalysis(db, asset.Guid, clampedDepth);
            return Serialize(new
            {
                Asset = Describe(asset),
                Depth = clampedDepth,
                TotalImpacted = impacted.Count,
                Impacted = impacted
                    .Take(MaxListedResults)
                    .Select(i => new { Path = i.Asset.Name, i.Distance, i.Via })
                    .ToList(),
                Truncated = impacted.Count > MaxListedResults ? (bool?)true : null,
            });
        });

    [McpServerTool(Name = "get_neighbors")]
    [Description("Returns the neighborhood of an asset: all assets within N hops in either direction " +
                 "and every edge among them. Hop 1 shows direct dependencies (outgoing) and referrers " +
                 "(incoming) in one picture.")]
    public string GetNeighbors(
        [Description("Asset path, file name, or GUID")] string name,
        [Description("Neighborhood radius in hops (default 1, max 3)")] int hops = 1)
        => WithResolvedAsset(name, (db, asset) =>
        {
            var clampedHops = Math.Clamp(hops, 1, 3);
            var neighborhood = GraphQueries.GetNeighbors(db, asset.Guid, clampedHops);
            return Serialize(new
            {
                Asset = Describe(asset),
                Hops = clampedHops,
                TotalAssets = neighborhood.Assets.Count,
                TotalEdges = neighborhood.Edges.Count,
                Assets = neighborhood.Assets
                    .Take(MaxListedResults)
                    .Select(a => new { Name = a.Name, Type = a.AssetType, a.Source })
                    .ToList(),
                Edges = neighborhood.Edges
                    .Take(MaxListedResults)
                    .Select(e => new { From = e.FromName, To = e.ToName, Kind = e.RefKind, e.RefCount })
                    .ToList(),
                Truncated = neighborhood.Assets.Count > MaxListedResults
                            || neighborhood.Edges.Count > MaxListedResults ? (bool?)true : null,
            });
        });

    [McpServerTool(Name = "search_nodes")]
    [Description("Case-insensitive substring search over asset paths (or an exact GUID), shortest " +
                 "paths first. Use this to find the exact path when a name is ambiguous or unknown.")]
    public string SearchNodes(
        [Description("Substring of an asset path, or a GUID")] string query,
        [Description("Maximum number of results (default 20, max 100)")] int limit = 20)
    {
        using var db = OpenDatabase();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        // Fetch one extra row so truncation is detectable.
        var matches = GraphQueries.SearchNodes(db, query, clampedLimit + 1);
        return Serialize(new
        {
            Query = query,
            Matches = matches
                .Take(clampedLimit)
                .Select(Describe)
                .ToList(),
            Truncated = matches.Count > clampedLimit ? (bool?)true : null,
        });
    }

    [McpServerTool(Name = "list_unresolved")]
    [Description("Lists all unresolved GUIDs (dangling references: no asset with that GUID exists in " +
                 "the project), most-referenced first, with sample referrer paths.")]
    public string ListUnresolved()
    {
        using var db = OpenDatabase();
        var unresolved = db.GetUnresolvedGuids();
        return Serialize(new
        {
            TotalUnresolved = unresolved.Count,
            Unresolved = unresolved
                .Take(MaxListedResults)
                .Select(u => new
                {
                    u.Guid,
                    u.RefCount,
                    u.ReferrerCount,
                    SampleReferrers = u.SampleReferrers,
                })
                .ToList(),
            Truncated = unresolved.Count > MaxListedResults ? (bool?)true : null,
        });
    }

    private GraphDatabase OpenDatabase() => GraphDatabase.OpenFile(options.DbPath);

    private string WithResolvedAsset(string name, Func<GraphDatabase, AssetNode, string> answer)
    {
        using var db = OpenDatabase();
        var matches = GraphQueries.ResolveAssets(db, name);
        return matches.Count switch
        {
            1 => answer(db, matches[0]),
            0 => Serialize(new
            {
                Error = "not_found",
                Name = name,
                Hint = "No asset matched this path, file name, or GUID. Try search_nodes with a substring.",
            }),
            _ => Serialize(new
            {
                Error = "ambiguous",
                Name = name,
                CandidateCount = matches.Count,
                Candidates = matches.Take(MaxAmbiguousCandidates).Select(m => m.Name).ToList(),
                Hint = "Multiple assets matched. Call again with one of the candidate paths.",
            }),
        };
    }

    private static object Describe(AssetNode asset) => new
    {
        Name = asset.Name,
        Guid = asset.Guid == asset.Name ? null : asset.Guid,
        Type = asset.AssetType,
        Source = asset.Source,
        Dangling = asset.Path is null ? (bool?)true : null,
    };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
