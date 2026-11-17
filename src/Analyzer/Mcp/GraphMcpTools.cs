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
/// The MCP tools of the serve command. Each call opens the database
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
    private const int MaxSignatures = 12;
    private const int MaxSteps = 8;
    private const int MaxFolderTypes = 6;

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
    [Description("Finds assets by path substring (case-insensitive) or exact GUID, shortest paths " +
                 "first, optionally restricted to one asset type (class) and/or source. Use it to find " +
                 "the exact path of a name, or to list instances of a class (e.g. type=Scene, empty " +
                 "query). unreferenced=true keeps only assets nothing references. Call " +
                 "describe_ontology for the type and source names present in this project.")]
    public string SearchNodes(
        [Description("Substring of an asset path, or a GUID. Empty matches every path")] string query = "",
        [Description("Asset type to keep, e.g. Prefab, Material, Script (case-insensitive)")] string? type = null,
        [Description("Source to keep: Assets, Packages, PackageCache, ProjectSettings, BuiltIn or Unresolved")]
        string? source = null,
        [Description("Keep only assets that no other asset references")] bool unreferenced = false,
        [Description("Maximum number of results (default 20, max 100)")] int limit = 20)
    {
        using var db = OpenDatabase();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        // Fetch one extra row so truncation is detectable.
        var matches = GraphQueries.FindAssets(
            db, query, NullIfBlank(type), NullIfBlank(source), unreferenced, clampedLimit + 1);
        return Serialize(new
        {
            Query = query,
            Type = NullIfBlank(type),
            Source = NullIfBlank(source),
            Unreferenced = unreferenced ? (bool?)true : null,
            Matches = matches
                .Take(clampedLimit)
                .Select(Describe)
                .ToList(),
            Truncated = matches.Count > clampedLimit ? (bool?)true : null,
        });
    }

    [McpServerTool(Name = "search_keywords")]
    [Description("Keyword search over every clue that names or describes an asset: file name, the " +
                 "folder names on its path, and for C# scripts the declared type names, documented " +
                 "member names and XML doc comment text (<summary>/<remarks>). Each keyword is a " +
                 "case-insensitive substring; results are ranked by how many keywords hit, and say " +
                 "where each hit (file name > type name > folder / member name > doc comment). Use it " +
                 "to find candidates by concept (e.g. [\"enemy\", \"spawn\"] or Japanese words in doc " +
                 "comments) before a typed traverse.")]
    public string SearchKeywords(
        [Description("Keywords; each is a case-insensitive substring. Whitespace splits a keyword")] string[] keywords,
        [Description("Asset type to keep, e.g. Script, Prefab (case-insensitive)")] string? type = null,
        [Description("Source to keep: Assets, Packages, PackageCache, ...")] string? source = null,
        [Description("Keep only assets that hit every keyword")] bool match_all = false,
        [Description("Maximum number of results (default 20, max 100)")] int limit = 20)
    {
        using var db = OpenDatabase();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var matches = GraphQueries.SearchByKeywords(
            db, keywords ?? [], NullIfBlank(type), NullIfBlank(source), match_all);
        return Serialize(new
        {
            Keywords = keywords,
            TotalMatches = matches.Count,
            Matches = matches
                .Take(clampedLimit)
                .Select(m => new
                {
                    Name = m.Asset.Name,
                    Type = m.Asset.AssetType,
                    m.Asset.Source,
                    m.MatchedKeywords,
                    m.Score,
                    m.Clues,
                    Declares = m.DeclaredTypes.Count > 0 ? m.DeclaredTypes : null,
                    Doc = m.DocSnippet,
                })
                .ToList(),
            Truncated = matches.Count > clampedLimit ? (bool?)true : null,
            Warning = db.HasCodeSymbols
                ? null
                : "This graph was built by an older ariadne without C# type names and doc comments; " +
                  "only file and folder names were searched. Re-run analyze (update-graph) to add them.",
        });
    }

    [McpServerTool(Name = "list_folders")]
    [Description("Lists the folders under a path with how many assets of which types each holds " +
                 "(recursively). Folder names show how the project is organized (Characters/Enemies, " +
                 "UI, Levels, ...) — use them as clues and as path_contains filters in traverse.")]
    public string ListFolders(
        [Description("Folder to list, e.g. Assets or Assets/Characters. Empty lists the top level")] string path = "Assets",
        [Description("How many folder levels down to list (default 1, max 4)")] int depth = 1)
    {
        using var db = OpenDatabase();
        var folders = GraphQueries.ListFolders(db, path ?? "", Math.Clamp(depth, 1, 4));
        return Serialize(new
        {
            Path = path,
            TotalFolders = folders.Count,
            Folders = folders
                .Take(MaxListedResults)
                .Select(f => new
                {
                    f.Path,
                    Assets = f.AssetCount,
                    Types = f.ByType
                        .OrderByDescending(t => t.Value)
                        .ThenBy(t => t.Key, StringComparer.Ordinal)
                        .Take(MaxFolderTypes)
                        .ToDictionary(t => t.Key, t => t.Value),
                })
                .ToList(),
            Truncated = folders.Count > MaxListedResults ? (bool?)true : null,
        });
    }

    [McpServerTool(Name = "describe_ontology")]
    [Description("Returns the vocabulary of this project's graph: every class (asset type) with its " +
                 "count per source, and every relation (ref kind) with its edge count and the " +
                 "'FromType->ToType' signatures it actually connects. Call it first to plan a typed " +
                 "query with traverse or search_nodes.")]
    public string DescribeOntology()
    {
        using var db = OpenDatabase();
        var ontology = GraphQueries.DescribeOntology(db);
        return Serialize(new
        {
            Classes = ontology.Classes
                .Select(c => new { Type = c.AssetType, c.Count, c.BySource })
                .ToList(),
            Relations = ontology.Relations
                .Select(r => new
                {
                    Kind = r.RefKind,
                    r.Edges,
                    r.RefCount,
                    Signatures = r.Signatures
                        .Take(MaxSignatures)
                        .Select(sig => $"{sig.FromType}->{sig.ToType}: {sig.Edges}")
                        .ToList(),
                    MoreSignatures = r.Signatures.Count > MaxSignatures
                        ? (int?)(r.Signatures.Count - MaxSignatures)
                        : null,
                })
                .ToList(),
        });
    }

    [McpServerTool(Name = "traverse")]
    [Description("Typed path query over the graph. Starts at one asset (start) or at every asset of " +
                 "a class (start_type, optionally narrowed by start_path_contains), then applies the " +
                 "steps in order; each step follows edges of the given kinds in the given direction for " +
                 "up to max_hops hops and keeps only assets of the given types (and path_contains " +
                 "folder/file-name clues). Returns the assets left after the last step, each with " +
                 "one witness path ('A -[kind]-> B' = A references B, 'A <-[kind]- B' = B references " +
                 "A). Example: prefabs using a subclass of Enemy.cs = start Enemy.cs, steps " +
                 "[{direction:in, kinds:[inherits], max_hops:10, include_self:true}, " +
                 "{direction:in, kinds:[uses_script], types:[Prefab]}].")]
    public string Traverse(
        [Description("Path steps, applied in order (1-8 steps)")] TraverseStep[] steps,
        [Description("Start asset: path, file name, or GUID. Give this or start_type")] string? start = null,
        [Description("Start from every asset of this type instead, e.g. Scene")] string? start_type = null,
        [Description("With start_type: keep only start assets whose path contains any of these " +
                     "(case-insensitive), e.g. [\"Enemies/\"]")] string[]? start_path_contains = null)
    {
        if (steps is null || steps.Length is 0 or > MaxSteps)
        {
            return Error("invalid_steps", $"Give 1-{MaxSteps} steps.");
        }

        var parsedSteps = new List<TraversalStep>();
        foreach (var step in steps)
        {
            if (!Enum.TryParse<TraversalDirection>(step.Direction, ignoreCase: true, out var direction)
                || !Enum.IsDefined(direction))
            {
                return Error("invalid_direction",
                    $"Unknown direction '{step.Direction}'. Use out (dependencies), in (referrers) or both.");
            }

            parsedSteps.Add(new TraversalStep(
                direction, step.Kinds, step.Types, step.MaxHops ?? 1, step.IncludeSelf ?? false, step.PathContains));
        }

        start = NullIfBlank(start);
        start_type = NullIfBlank(start_type);
        if ((start is null) == (start_type is null))
        {
            return Error("invalid_start", "Give exactly one of start and start_type.");
        }

        using var db = OpenDatabase();
        string startLabel;
        IReadOnlyCollection<string> startGuids;

        if (start is not null)
        {
            var resolved = GraphQueries.ResolveAssets(db, start);
            if (resolved.Count != 1)
            {
                return ResolutionError(start, resolved);
            }

            startLabel = resolved[0].Name;
            startGuids = [resolved[0].Guid];
        }
        else
        {
            startLabel = $"type:{start_type}";
            startGuids = GraphQueries.AssetGuidsOfType(db, start_type!, start_path_contains);
        }

        var ontology = GraphQueries.DescribeOntology(db);
        var warnings = UnknownTerms(parsedSteps, start_type, ontology);
        var matches = GraphQueries.Traverse(db, startGuids, parsedSteps);
        return Serialize(new
        {
            Start = startLabel,
            StartCount = start_type is null ? (int?)null : startGuids.Count,
            TotalMatches = matches.Count,
            Matches = matches
                .Take(MaxListedResults)
                .Select(m => new
                {
                    Name = m.Asset.Name,
                    Type = m.Asset.AssetType,
                    m.Asset.Source,
                    Dangling = m.Asset.Path is null ? (bool?)true : null,
                    Path = FormatPath(m),
                })
                .ToList(),
            Truncated = matches.Count > MaxListedResults ? (bool?)true : null,
            Warnings = warnings.Count > 0 ? warnings : null,
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
        return matches.Count == 1 ? answer(db, matches[0]) : ResolutionError(name, matches);
    }

    private static string ResolutionError(string name, IReadOnlyList<AssetNode> matches) =>
        matches.Count == 0
            ? Serialize(new
            {
                Error = "not_found",
                Name = name,
                Hint = "No asset matched this path, file name, or GUID. Try search_nodes with a substring.",
            })
            : Serialize(new
            {
                Error = "ambiguous",
                Name = name,
                CandidateCount = matches.Count,
                Candidates = matches.Take(MaxAmbiguousCandidates).Select(m => m.Name).ToList(),
                Hint = "Multiple assets matched. Call again with one of the candidate paths.",
            });

    private static string Error(string error, string hint) => Serialize(new { Error = error, Hint = hint });

    /// <summary>
    /// Kinds and types the steps name that do not occur in this graph. Not an error — the
    /// project may simply have none — but usually a typo, so the caller is told.
    /// </summary>
    private static List<string> UnknownTerms(
        IEnumerable<TraversalStep> steps, string? startType, Ontology ontology)
    {
        var knownKinds = ontology.Relations.Select(r => r.RefKind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownTypes = ontology.Classes.Select(c => c.AssetType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownKinds = steps.SelectMany(s => s.Kinds ?? []).Where(k => !knownKinds.Contains(k));
        var unknownTypes = steps.SelectMany(s => s.Types ?? [])
            .Append(startType)
            .OfType<string>()
            .Where(t => !knownTypes.Contains(t));
        return unknownKinds.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => $"ref kind '{k}' has no edges in this graph")
            .Concat(unknownTypes.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => $"asset type '{t}' has no assets in this graph"))
            .Select(w => w + " (see describe_ontology)")
            .ToList();
    }

    private static string FormatPath(TraversalMatch match)
    {
        var builder = new System.Text.StringBuilder(match.Start);
        foreach (var hop in match.Path)
        {
            builder.Append(hop.Direction == TraversalDirection.Out
                ? $" -[{hop.RefKind}]-> "
                : $" <-[{hop.RefKind}]- ");
            builder.Append(hop.Asset);
        }

        return builder.ToString();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

/// <summary>One step of the traverse tool, as the MCP client sends it.</summary>
public sealed record TraverseStep
{
    [Description("out = follow references to what the asset uses (dependencies); in = to what uses " +
                 "the asset (referrers); both = either way")]
    [JsonPropertyName("direction")]
    public required string Direction { get; init; }

    [Description("Ref kinds (relations) to follow, e.g. [\"inherits\"]. Omit for any kind")]
    [JsonPropertyName("kinds")]
    public string[]? Kinds { get; init; }

    [Description("Asset types (classes) an asset must have to end this step, e.g. [\"Prefab\"]. " +
                 "Omit for any type. Intermediate assets of a multi-hop step are not checked")]
    [JsonPropertyName("types")]
    public string[]? Types { get; init; }

    [Description("Follow up to this many hops (default 1, max 10) — e.g. 10 with kinds [inherits] " +
                 "walks the whole class hierarchy")]
    [JsonPropertyName("max_hops")]
    public int? MaxHops { get; init; }

    [Description("Also keep the assets this step starts from (zero hops) if their type matches — " +
                 "e.g. 'the class and all its subclasses'")]
    [JsonPropertyName("include_self")]
    public bool? IncludeSelf { get; init; }

    [Description("Keep only end assets whose path contains any of these substrings (case-insensitive) " +
                 "— folder or file-name clues, e.g. [\"/UI/\", \"Boss\"]")]
    [JsonPropertyName("path_contains")]
    public string[]? PathContains { get; init; }
}
