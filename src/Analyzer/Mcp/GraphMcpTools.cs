using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using GuidGraph.Graph;

namespace GuidGraph.Analyzer.Mcp;

/// <summary>Tells <see cref="GraphMcpTools"/> which database file to query.</summary>
public sealed record GraphMcpOptions(string DbPath, string? ProjectPath = null);

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
    private const int DefaultSummaryTop = 5;
    private const int DefaultUnresolvedLimit = 20;
    private const int MaxSummaryTop = 20;
    private const int SummaryFolderTypes = 4;
    private const int MaxAmbiguousCandidates = 10;
    private const int MaxSignatures = 12;
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
                 "name unique in the project, or a GUID. summary=true returns counts per reference " +
                 "kind and asset type plus the top referrers instead of every row. For many assets at once " +
                 "use search with an 'in' step instead of calling this once per asset.")]
    public string FindReferences(
        [Description("Asset path, file name, or GUID")] string name,
        [Description("Return per-kind and per-type counts with top referrers instead of rows (default false)")]
        bool summary = false,
        [Description("With summary: referrer paths listed per kind, 1-20 (default 5)")] int top = DefaultSummaryTop)
        => WithResolvedAsset(name, (db, asset) =>
        {
            var references = GraphQueries.FindReferences(db, asset.Guid);
            if (summary)
            {
                var perKind = Math.Clamp(top, 1, MaxSummaryTop);
                return Serialize(new
                {
                    Asset = Describe(asset),
                    TotalReferrers = references.Count,
                    ByKind = references
                        .GroupBy(r => r.RefKind)
                        .Select(g => new
                        {
                            Kind = g.Key,
                            Referrers = g.Count(),
                            Refs = g.Sum(r => r.RefCount),
                            Top = g.Take(perKind).Select(r => r.Referrer.Name).ToList(),
                        })
                        .OrderByDescending(k => k.Referrers)
                        .ThenBy(k => k.Kind, StringComparer.Ordinal)
                        .ToList(),
                    ByType = references
                        .GroupBy(r => r.Referrer.AssetType)
                        .Select(g => new { Type = g.Key, Referrers = g.Select(r => r.Referrer.Guid).Distinct().Count() })
                        .OrderByDescending(t => t.Referrers)
                        .ThenBy(t => t.Type, StringComparer.Ordinal)
                        .ToList(),
                });
            }

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

    [McpServerTool(Name = "list_folders")]
    [Description("Lists the folders under a path with how many assets of which types each holds " +
                 "(recursively). Folder names show how the project is organized (Characters/Enemies, " +
                 "UI, Levels, ...) — use them as clues and as path_contains filters in search.")]
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

    [McpServerTool(Name = "summarize_project")]
    [Description("One-call project overview: scenes, build order, top-level folders with their asset types, " +
                 "script counts and a README excerpt (only when the server knows the project folder). " +
                 "Sized by max_chars; cut items carry truncated=true.")]
    public string SummarizeProject(
        [Description("Rough size limit of the answer in characters (default 1500)")] int max_chars = 1500,
        [Description("Most scenes listed (default 20)")] int scene_limit = 20,
        [Description("Folder levels below Assets, 1-3 (default 1)")] int folder_depth = 1,
        [Description("README excerpt length, 0 = none (default 200)")] int readme_chars = 200)
    {
        using var db = OpenDatabase();
        max_chars = Math.Clamp(max_chars, 200, 20000);
        var scenes = GraphQueries.FindAssets(db, "", "Scene", "Assets", false, MaxListedResults)
            .Select(a => a.Name).ToList();
        var sceneTotal = GraphQueries.CountAssets(db, "Scene", "Assets");
        var buildOrder = GraphQueries.BuildSceneOrder(db);
        var folders = GraphQueries.ListFolders(db, "Assets", Math.Clamp(folder_depth, 1, 3))
            .OrderByDescending(f => f.AssetCount)
            .Select(f => new
            {
                f.Path,
                Assets = f.AssetCount,
                Types = f.ByType
                    .OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal)
                    .Take(SummaryFolderTypes).ToDictionary(t => t.Key, t => t.Value),
            })
            .ToList();
        var readme = ReadReadme(Math.Clamp(readme_chars, 0, 2000));

        var sceneCount = Math.Min(Math.Max(scene_limit, 0), scenes.Count);
        var folderCount = folders.Count;
        var readmeLength = readme?.Length ?? 0;
        while (true)
        {
            var json = Serialize(new
            {
                Scenes = scenes.Take(sceneCount).ToList(),
                ScenesTruncated = sceneCount < sceneTotal ? (bool?)true : null,
                BuildOrder = buildOrder.Take(sceneCount).ToList(),
                Folders = folders.Take(folderCount).ToList(),
                FoldersTruncated = folderCount < folders.Count ? (bool?)true : null,
                Scripts = new
                {
                    Assets = GraphQueries.CountAssets(db, "Script", "Assets"),
                    Packages = GraphQueries.CountAssets(db, "Script", "Packages", "PackageCache"),
                },
                Readme = readme is null ? null : readme[..readmeLength],
                ReadmeTruncated = readme is not null && readmeLength < readme.Length ? (bool?)true : null,
            });
            if (json.Length <= max_chars || (sceneCount == 0 && folderCount == 0 && readmeLength == 0))
            {
                return json;
            }

            // Shed the least useful parts first: scenes, then folders, then the README.
            if (sceneCount > 0)
            {
                sceneCount--;
            }
            else if (folderCount > 0)
            {
                folderCount--;
            }
            else
            {
                readmeLength = Math.Max(0, readmeLength - 20);
            }
        }
    }

    /// <summary>First <paramref name="chars"/> characters of the project README, or null.</summary>
    private string? ReadReadme(int chars)
    {
        if (chars <= 0 || options.ProjectPath is not { } root)
        {
            return null;
        }

        foreach (var name in new[] { "README.md", "Readme.md", "readme.md", "README.txt", "README" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                return text.Length > chars ? text[..chars] : text;
            }
        }

        return null;
    }

    [McpServerTool(Name = "expanded_count")]
    [Description("How many objects of a scene or prefab use an asset once nested prefabs are expanded: direct uses " +
                 "plus uses inside nested prefabs multiplied by how often each is placed, minus what instances " +
                 "removed. Works for any asset type (material, mesh, script, prefab, ...).")]
    public string ExpandedCount(
        [Description("Asset to count: path, unique file name or GUID")] string asset,
        [Description("Scene or prefab to count in: path, unique file name or GUID")] string scene,
        [Description("Nesting levels followed, 1-50 (default 10)")] int max_depth = 10)
    {
        using var db = OpenDatabase();
        if (!db.HasPlacements)
        {
            return Serialize(new
            {
                Error = "outdated_db",
                Hint = "This graph was built by an older guidgraph without placement counts. " +
                       "Re-run analyze (it rebuilds the database), then call again.",
            });
        }

        var assets = GraphQueries.ResolveAssets(db, asset);
        if (assets.Count != 1)
        {
            return ResolutionError(asset, assets);
        }

        var containers = GraphQueries.ResolveAssets(db, scene);
        if (containers.Count != 1)
        {
            return ResolutionError(scene, containers);
        }

        var target = assets[0];
        var container = containers[0];
        if (container.AssetType is not ("Scene" or "Prefab"))
        {
            return Serialize(new
            {
                Error = "invalid_scene",
                Name = container.Name,
                Type = container.AssetType,
                Hint = "scene must be a Scene or a Prefab",
            });
        }

        var depth = Math.Clamp(max_depth, 1, 50);
        var result = ExpandedCounter.Count(db, target.Guid, container.Guid, depth);
        return Serialize(new
        {
            Asset = target.Name,
            AssetType = target.AssetType,
            Scene = container.Name,
            result.ExpandedCount,
            MaxDepth = depth,
            result.Direct,
            result.ViaNested,
            result.Removed,
            UnresolvedRemovals = result.UnresolvedRemovals > 0 ? result.UnresolvedRemovals : (long?)null,
            DepthLimited = result.DepthLimited ? (bool?)true : null,
            Unsupported = NullIfEmptyList(new[] { target.AssetType, container.AssetType }
                .Where(t => !OntologyGlossary.IsParsed(t)).Distinct().Order(StringComparer.Ordinal).ToList()),
            Notes = "Counts objects that reference the asset. Overrides that swap one asset for another " +
                    "(m_Modifications) count for the new asset once per instance and are not taken from the old one. " +
                    "unresolved_removals counts removals whose target the graph cannot identify (Unity derives those ids), so the count may be too high by what they removed.",
        });
    }

    private static IReadOnlyList<string>? NullIfEmptyList(List<string> list) => list.Count == 0 ? null : list;

    [McpServerTool(Name = "describe_ontology")]
    [Description("Returns the vocabulary of this project's graph: every class (asset type) with its " +
                 "category and count per source, and every relation (ref kind) with its meaning read " +
                 "out (A -> B) and in (B <- A), whether chaining it is meaningful (transitive), its edge " +
                 "count and the 'FromType->ToType' signatures it actually connects. Use it to pick type " +
                 "and relation names for search.")]
    public string DescribeOntology()
    {
        using var db = OpenDatabase();
        var ontology = GraphQueries.DescribeOntology(db);
        return Serialize(new
        {
            SearchGuide = SearchGuide,
            Classes = ontology.Classes
                .Select(c => new
                {
                    Type = c.AssetType,
                    Category = OntologyGlossary.CategoryOf(c.AssetType),
                    Parsed = OntologyGlossary.IsParsed(c.AssetType) ? null : (bool?)false,
                    c.Count,
                    c.BySource,
                })
                .ToList(),
            Relations = ontology.Relations
                .Select(r => new
                {
                    Kind = r.RefKind,
                    Layer = Meaning(r.RefKind)?.Layer,
                    Out = Meaning(r.RefKind)?.Out,
                    In = Meaning(r.RefKind)?.In,
                    Transitive = Meaning(r.RefKind)?.Transitive,
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

    [McpServerTool(Name = "search")]
    [Description(
        "Ontology search over the project graph. 'from' picks the start (one asset, or assets filtered by " +
        "types/source/path_contains and ranked by keywords matched against file, folder and C# names and doc " +
        "comments); 'steps' follow typed relations in order (omit to list the starts). Empty results are " +
        "relaxed automatically ('relaxed' says how). Matches carry 'path' ('A -[kind]-> B' = A references B), " +
        "clues, doc excerpt, declares and notes; 'relations' explains the kinds used. Relation/type names and " +
        "a worked example: describe_ontology.")]
    public string Search(
        [Description("Start: {asset} or filters {keywords, match_all, types, source, path_contains}")] SearchStart from,
        [Description("Path steps in order (0-8); omit to list the starts")]
        SearchStep[]? steps = null,
        [Description("Keep only unreferenced results")] bool unreferenced = false,
        [Description("Maximum number of matches listed (default 20, max 200)")] int limit = GraphSearch.DefaultLimit,
        [Description("Relax steps when nothing matches (default true)")] bool relax = true)
    {
        using var db = OpenDatabase();
        return AddMeta(GraphSearch.RunJson(db, new SearchQuery
        {
            From = from,
            Steps = steps,
            Unreferenced = unreferenced,
            Limit = limit,
            Relax = relax,
        }));
    }

    [McpServerTool(Name = "unreachable")]
    [Description("Assets that no root reaches through any chain of references, e.g. prefabs no scene uses even " +
                 "indirectly (default: types=[Prefab], roots=[Scene]). One call for the whole project; matches " +
                 "carry their direct referrer count (those referrers are unreachable too) and folder notes. " +
                 "Static references only: loads by name are not seen.")]
    public string Unreachable(
        [Description("Asset types to check (default [\"Prefab\"])")] string[]? types = null,
        [Description("Asset types to start from (default [\"Scene\"])")] string[]? roots = null,
        [Description("Where checked assets and roots live (default Assets)")] string? source = null,
        [Description("Start only from scenes listed in the build settings (default false)")] bool build_only = false,
        [Description("Ref kinds to follow; omit for all")] string[]? kinds = null,
        [Description("Check only assets whose path contains any of these")] string[]? path_contains = null,
        [Description("Maximum number of matches listed (default 50, max 200)")]
        int limit = UnreachableSearch.DefaultLimit)
    {
        using var db = OpenDatabase();
        return AddMeta(UnreachableSearch.RunJson(db, new UnreachableQuery
        {
            Types = types,
            Roots = roots,
            Source = source,
            BuildOnly = build_only,
            Kinds = kinds,
            PathContains = path_contains,
            Limit = limit,
        }));
    }

    [McpServerTool(Name = "list_unresolved")]
    [Description("Lists all unresolved GUIDs (dangling references: no asset with that GUID exists in " +
                 "the project), most-referenced first, with sample referrer paths and, when guessable, a hint with the " +
                 "packages it probably belongs to (package_candidates, basis).")]
    public string ListUnresolved(
        [Description("Most entries listed, 1-200 (default 20); total_unresolved is always the full count")]
        int limit = DefaultUnresolvedLimit)
    {
        using var db = OpenDatabase();
        var unresolved = db.GetUnresolvedGuids();
        var listed = Math.Clamp(limit, 1, MaxListedResults);
        return Serialize(new
        {
            TotalUnresolved = unresolved.Count,
            Unresolved = unresolved
                .Take(listed)
                .Select(u => new
                {
                    u.Guid,
                    u.RefCount,
                    u.ReferrerCount,
                    SampleReferrers = u.SampleReferrers,
                    Hint = GraphQueries.HintForUnresolved(db, u.Guid),
                })
                .ToList(),
            Truncated = unresolved.Count > listed ? (bool?)true : null,
        });
    }

    private const string SearchGuide =
        "search: keywords match ignoring case, width and kana; give synonyms in the user's language and English, " +
        "e.g. [\"ボス\", \"boss\"]. Names, clues and docs come back unchanged. Example, prefabs using Enemy.cs or " +
        "a subclass: from {asset: \"Enemy.cs\"}, steps [{direction: in, kinds: [inherits], max_hops: 10, " +
        "include_self: true}, {direction: in, kinds: [uses_script], types: [Prefab]}]. Other examples: " +
        "from {keywords: [\"boss\"], types: [Script]}; from {types: [Prefab], path_contains: [\"/UI/\"]} with " +
        "unreferenced: true for prefabs nothing references (for ones no scene reaches, use the unreachable tool). " +
        "Step 'direction': out = dependencies, in = referrers.";

    private GraphDatabase OpenDatabase() => GraphDatabase.OpenFile(options.DbPath);

    private string WithResolvedAsset(string name, Func<GraphDatabase, AssetNode, string> answer)
    {
        using var db = OpenDatabase();
        var matches = GraphQueries.ResolveAssets(db, name);
        return matches.Count == 1 ? answer(db, matches[0]) : ResolutionError(name, matches);
    }

    private string ResolutionError(string name, IReadOnlyList<AssetNode> matches) =>
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

    private static RelationMeaning? Meaning(string kind) =>
        OntologyGlossary.Relations.TryGetValue(kind, out var meaning) ? meaning : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static object Describe(AssetNode asset) => new
    {
        Name = asset.Name,
        Guid = asset.Guid == asset.Name ? null : asset.Guid,
        Type = asset.AssetType,
        Source = asset.Source,
        Dangling = asset.Path is null ? (bool?)true : null,
    };

    private string Serialize(object value) => AddMeta(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>Adds the freshness `meta` object to a JSON object answer.</summary>
    private string AddMeta(string json)
    {
        var meta = GraphFreshness.Describe(options.DbPath, options.ProjectPath);
        if (meta is null || JsonNode.Parse(json) is not JsonObject root)
        {
            return json;
        }

        root["meta"] = JsonSerializer.SerializeToNode(meta, JsonOptions);
        return root.ToJsonString(JsonOptions);
    }
}

