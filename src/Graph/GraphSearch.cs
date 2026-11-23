using System.ComponentModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuidGraph.Graph;

/// <summary>Where a search starts: one named asset, or every asset matching the filters.</summary>
public sealed record SearchStart
{
    [Description("One asset (path, unique file name or GUID); exclusive with filters")]
    [JsonPropertyName("asset")]
    public string? Asset { get; init; }

    [Description("Concept keywords with synonyms in the user's language and English")]
    [JsonPropertyName("keywords")]
    public string[]? Keywords { get; init; }

    [Description("Require every keyword (default: any, ranked)")]
    [JsonPropertyName("match_all")]
    public bool? MatchAll { get; init; }

    [Description("Start asset types, e.g. [\"Script\"]")]
    [JsonPropertyName("types")]
    public string[]? Types { get; init; }

    [Description("Assets, Packages, PackageCache, ProjectSettings or BuiltIn")]
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [Description("Keep starts whose path contains any of these, e.g. [\"/UI/\"]")]
    [JsonPropertyName("path_contains")]
    public string[]? PathContains { get; init; }
}

/// <summary>One hop pattern of a search, as clients send it.</summary>
public sealed record SearchStep
{
    [Description("out = dependencies, in = referrers, both")]
    [JsonPropertyName("direction")]
    public required string Direction { get; init; }

    [Description("Ref kinds to follow, e.g. [\"inherits\"]; omit for any")]
    [JsonPropertyName("kinds")]
    public string[]? Kinds { get; init; }

    [Description("Asset types the step must end on, e.g. [\"Prefab\"]")]
    [JsonPropertyName("types")]
    public string[]? Types { get; init; }

    [Description("Max hops (default 1, max 10)")]
    [JsonPropertyName("max_hops")]
    public int? MaxHops { get; init; }

    [Description("Also keep the step's starting assets (zero hops)")]
    [JsonPropertyName("include_self")]
    public bool? IncludeSelf { get; init; }

    [Description("Keep end assets whose path contains any of these")]
    [JsonPropertyName("path_contains")]
    public string[]? PathContains { get; init; }
}

/// <summary>A whole search: start, path steps, and result options (the JSON of `guidgraph search --query`).</summary>
public sealed record SearchQuery
{
    [JsonPropertyName("from")]
    public SearchStart? From { get; init; }

    [JsonPropertyName("steps")]
    public SearchStep[]? Steps { get; init; }

    [JsonPropertyName("unreferenced")]
    public bool? Unreferenced { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("relax")]
    public bool? Relax { get; init; }
}

/// <summary>
/// The integrated ontology search behind the MCP <c>search</c> tool and <c>guidgraph search</c>:
/// finds start candidates (a named asset, or assets filtered by type/source/path and ranked by
/// keyword clues — file and folder names, C# type names and XML doc comments), follows typed
/// path steps (<see cref="GraphQueries.Traverse"/>), relaxes the steps when nothing matches,
/// and annotates every result with its evidence: the reference path, the keyword clues and doc
/// excerpt (of the match itself, or of the start its path came from), declared types and folder-convention notes, plus a glossary
/// of the relations the paths use. Output is compact snake_case JSON for both front ends.
/// Explanations are English (the calling agent puts them in the user's language); asset names,
/// keywords, clues and doc excerpts are passed through unchanged in whatever script they use.
/// </summary>
public static class GraphSearch
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 200;
    public const int MaxSteps = 8;
    private const int MaxListedStarts = 10;
    private const int RelaxedMaxHops = 5;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Runs <paramref name="query"/> and returns the JSON answer (an <c>error</c> object on bad input).</summary>
    public static string RunJson(GraphDatabase db, SearchQuery query, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(Run(db, query), options ?? JsonOptions);

    /// <summary>Runs <paramref name="query"/>; the result is an anonymous object shaped for JSON.</summary>
    public static object Run(GraphDatabase db, SearchQuery query)
    {
        var origin = query.From;
        var stepsIn = query.Steps ?? [];
        if (origin is null)
        {
            return Error("invalid_query", "Give 'from': an asset, or keywords/types/source/path_contains.");
        }

        if (stepsIn.Length > MaxSteps)
        {
            return Error("invalid_steps", $"Give at most {MaxSteps} steps.");
        }

        var steps = new List<TraversalStep>();
        foreach (var step in stepsIn)
        {
            if (!Enum.TryParse<TraversalDirection>(step.Direction, ignoreCase: true, out var direction)
                || !Enum.IsDefined(direction) || int.TryParse(step.Direction, out _))
            {
                return Error("invalid_direction", $"Unknown direction '{step.Direction}'. Use out (dependencies), in (referrers) or both.");
            }

            steps.Add(new TraversalStep(direction, step.Kinds, step.Types, step.MaxHops ?? 1,
                step.IncludeSelf ?? false, step.PathContains));
        }

        var hasFilters = origin.Keywords is { Length: > 0 } || origin.Types is { Length: > 0 }
                         || !string.IsNullOrWhiteSpace(origin.Source) || origin.PathContains is { Length: > 0 };
        if (string.IsNullOrWhiteSpace(origin.Asset) == !hasFilters)
        {
            return Error("invalid_start", "Give either from.asset or filters (keywords, types, source, path_contains), not both.");
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var relax = query.Relax ?? true;
        var ontology = GraphQueries.DescribeOntology(db);
        var warnings = UnknownTerms(steps, origin.Types, ontology);
        if (!db.HasCodeSymbols && origin.Keywords is { Length: > 0 })
        {
            warnings.Add("this graph was built by an older guidgraph without C# type names and doc comments; " +
                "only file and folder names were searched — re-run analyze to add them");
        }

        var relaxed = new List<string>();

        // 1. Start candidates, most relevant first.
        IReadOnlyList<KeywordMatch> keywordStarts = [];
        List<AssetNode> starts;
        if (!string.IsNullOrWhiteSpace(origin.Asset))
        {
            var resolved = GraphQueries.ResolveAssets(db, origin.Asset);
            if (resolved.Count != 1)
            {
                return resolved.Count == 0
                    ? Error("not_found", $"No asset matched '{origin.Asset}'. Use from.keywords to search by name instead.")
                    : new
                    {
                        Error = "ambiguous",
                        Hint = "Several assets matched. Repeat with one of the candidate paths.",
                        Candidates = resolved.Take(MaxListedStarts).Select(a => a.Name).ToList(),
                    };
            }

            starts = [resolved[0]];
        }
        else
        {
            (starts, keywordStarts) = FindStarts(db, origin, origin.MatchAll ?? false);
            if (starts.Count == 0 && relax && origin.Keywords is { Length: > 1 } && origin.MatchAll == true)
            {
                (starts, keywordStarts) = FindStarts(db, origin, matchAll: false);
                relaxed.Add("start: accepted assets hitting any keyword instead of all");
            }

            if (starts.Count == 0 && relax && origin.PathContains is { Length: > 0 }
                && (origin.Keywords is { Length: > 0 } || origin.Types is { Length: > 0 }))
            {
                (starts, keywordStarts) = FindStarts(db, origin with { PathContains = null }, origin.MatchAll ?? false);
                relaxed.Add("start: dropped path_contains");
            }
        }

        // 2. Path steps, relaxed step by step while nothing matches.
        var matches = Evaluate(db, starts, steps);
        var variants = relax && starts.Count > 0 ? RelaxationsOf(steps) : [];
        foreach (var (description, variant) in variants)
        {
            if (Filter(db, matches, query.Unreferenced).Count > 0)
            {
                break;
            }

            matches = Evaluate(db, starts, variant);
            relaxed.Add(description);
        }

        var results = Filter(db, matches, query.Unreferenced);
        if (results.Count == 0 && relaxed.Count > 0)
        {
            relaxed.Add("nothing matched even after relaxing; check the direction of each step");
        }

        // 3. Evidence and annotations.
        var symbols = GraphQueries.LoadCodeSymbols(db);
        var clueOf = keywordStarts.ToDictionary(k => k.Asset.Guid, StringComparer.Ordinal);
        var usedKinds = new SortedSet<string>(StringComparer.Ordinal);
        var listed = results.Take(limit).Select(m =>
        {
            foreach (var hop in m.Path)
            {
                usedKinds.Add(hop.RefKind);
            }

            clueOf.TryGetValue(m.Start.Guid, out var startClues);
            symbols.TryGetValue(m.Asset.Guid, out var ownSymbols);
            var notes = OntologyGlossary.PathNotes(m.Asset);
            var clues = startClues?.Clues.Select(c => c.ToString()).ToList();
            var reached = m.Path.Count > 0;
            return new
            {
                Name = m.Asset.Name,
                Type = m.Asset.AssetType,
                m.Asset.Source,
                Path = reached ? FormatPath(m) : null,
                // Keyword evidence belongs to the asset it was found on: the match itself when
                // there are no steps, otherwise the start the path came from.
                From = reached ? m.Start.Name : null,
                FromClues = reached ? clues : null,
                FromDoc = reached ? startClues?.DocSnippet : null,
                Clues = reached ? null : clues,
                Doc = reached ? null : startClues?.DocSnippet,
                Declares = ownSymbols is null ? null : NullIfEmpty(GraphQueries.DeclaredTypes(ownSymbols).Take(3).ToList()),
                Notes = NullIfEmpty(notes),
            };
        }).ToList();

        return new
        {
            Start = new
            {
                Count = starts.Count,
                Assets = starts.Take(MaxListedStarts).Select(s => s.Name).ToList(),
            },
            TotalMatches = results.Count,
            Matches = listed,
            Truncated = results.Count > limit ? (bool?)true : null,
            Relations = usedKinds.Count == 0
                ? null
                : usedKinds.ToDictionary(k => k, k => OntologyGlossary.Relations.TryGetValue(k, out var meaning)
                    ? $"A -[{k}]-> B: {meaning.Out}"
                    : $"A -[{k}]-> B"),
            Relaxed = NullIfEmpty(relaxed),
            Warnings = NullIfEmpty(warnings),
        };
    }

    private static (List<AssetNode> Starts, IReadOnlyList<KeywordMatch> KeywordStarts) FindStarts(
        GraphDatabase db, SearchStart origin, bool matchAll)
    {
        var types = origin.Types is { Length: > 0 }
            ? new HashSet<string>(origin.Types, StringComparer.OrdinalIgnoreCase)
            : null;
        var candidates = GraphQueries.FindAssets(db, "", null, null, unreferencedOnly: false, int.MaxValue)
            .Where(a => types is null || types.Contains(a.AssetType))
            .Where(a => string.IsNullOrWhiteSpace(origin.Source)
                        || a.Source.Equals(origin.Source, StringComparison.OrdinalIgnoreCase))
            .Where(a => GraphQueries.PathMatches(a, origin.PathContains))
            .ToList();
        if (origin.Keywords is not { Length: > 0 })
        {
            return (candidates.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), []);
        }

        var ranked = GraphQueries.RankByKeywords(db, candidates, origin.Keywords, matchAll);
        return (ranked.Select(r => r.Asset).ToList(), ranked);
    }

    /// <summary>No steps: the starts themselves are the matches.</summary>
    private static IReadOnlyList<TraversalMatch> Evaluate(
        GraphDatabase db, List<AssetNode> starts, IReadOnlyList<TraversalStep> steps) =>
        steps.Count == 0
            ? starts.Select(s => new TraversalMatch(s, s, [])).ToList()
            : GraphQueries.Traverse(db, starts.Select(s => s.Guid).ToList(), steps);

    /// <summary>Cumulative relaxations, mildest first: path filters, then relation kinds, then reach.</summary>
    private static List<(string Description, List<TraversalStep> Steps)> RelaxationsOf(List<TraversalStep> steps)
    {
        var variants = new List<(string, List<TraversalStep>)>();
        var current = steps;
        if (current.Any(s => s.PathContains is { Count: > 0 }))
        {
            current = current.Select(s => s with { PathContains = null }).ToList();
            variants.Add(("steps: dropped path_contains", current));
        }

        if (current.Any(s => s.Kinds is { Count: > 0 }))
        {
            current = current.Select(s => s with { Kinds = null }).ToList();
            variants.Add(("steps: followed any relation instead of the given kinds", current));
        }

        if (current.Any(s => s.MaxHops < RelaxedMaxHops))
        {
            current = current.Select(s => s with { MaxHops = Math.Max(s.MaxHops, RelaxedMaxHops) }).ToList();
            variants.Add(($"steps: raised max_hops to {RelaxedMaxHops}", current));
        }

        return variants;
    }

    private static IReadOnlyList<TraversalMatch> Filter(
        GraphDatabase db, IReadOnlyList<TraversalMatch> matches, bool? unreferenced)
    {
        if (unreferenced != true)
        {
            return matches;
        }

        var referenced = GraphQueries.ReferencedGuids(db);
        return matches.Where(m => !referenced.Contains(m.Asset.Guid)).ToList();
    }

    private static List<string> UnknownTerms(
        IEnumerable<TraversalStep> steps, IEnumerable<string>? startTypes, Ontology ontology)
    {
        var knownKinds = ontology.Relations.Select(r => r.RefKind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownTypes = ontology.Classes.Select(c => c.AssetType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stepList = steps.ToList();
        var unknownKinds = stepList.SelectMany(s => s.Kinds ?? []).Where(k => !knownKinds.Contains(k));
        var unknownTypes = stepList.SelectMany(s => s.Types ?? []).Concat(startTypes ?? [])
            .Where(t => !knownTypes.Contains(t));
        return unknownKinds.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => $"ref kind '{k}' has no edges in this graph (see describe_ontology)")
            .Concat(unknownTypes.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => $"asset type '{t}' has no assets in this graph (see describe_ontology)"))
            .ToList();
    }

    /// <summary>"A -[kind]-> B" (A references B) / "A &lt;-[kind]- B" (B references A).</summary>
    public static string FormatPath(TraversalMatch match)
    {
        var builder = new StringBuilder(match.Start.Name);
        foreach (var hop in match.Path)
        {
            builder.Append(hop.Direction == TraversalDirection.Out ? $" -[{hop.RefKind}]-> " : $" <-[{hop.RefKind}]- ");
            builder.Append(hop.Asset);
        }

        return builder.ToString();
    }

    private static object Error(string error, string hint) => new { Error = error, Hint = hint };

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list;
}
