using UnityAriadne.Analyzer.Scanning;
using UnityAriadne.Analyzer.Yaml;

namespace UnityAriadne.Analyzer.Code;

/// <summary>One .cs file to scan: its GUID, project-relative path and source text.</summary>
public sealed record ScriptSource(string Guid, string Path, string Text);

/// <summary>A resolved code-layer reference from one script asset (Phase 2).</summary>
/// <param name="ToFileId">11500000 (the target's MonoScript object) for script targets, 0 for Resources.Load / Addressables asset targets.</param>
public sealed record CodeRef(string ToGuid, long ToFileId, string PropertyPath, string Kind);

/// <summary>All written refs of one script, in source order.</summary>
/// <param name="ObjectName">Name for the MonoScript object row: the file's first non-nested declared type.</param>
public sealed record ScriptRefs(string Guid, string? ObjectName, IReadOnlyList<CodeRef> Refs);

public sealed class CodeScanResult
{
    /// <summary>Scripts that produced at least one resolved ref, ordered by script path.</summary>
    public required IReadOnlyList<ScriptRefs> Scripts { get; init; }

    public required int ScriptFileCount { get; init; }

    public required int DeclaredTypeCount { get; init; }

    /// <summary>Type names matching ≥2 distinct in-scope declarations — skipped, never guessed.</summary>
    public required int AmbiguousTypeRefCount { get; init; }

    /// <summary>Resolved to a type declared in the referencing file itself — not a dependency, skipped.</summary>
    public required int SameFileRefCount { get; init; }

    /// <summary>Literal Resources.Load paths that matched no asset under any Resources/ folder.</summary>
    public required int UnresolvedResourceLoadCount { get; init; }

    public required int NonLiteralResourceLoadCount { get; init; }

    /// <summary>Literal Addressables.Load* keys matching no group entry's address or label.</summary>
    public required int UnresolvedAddressablesLoadCount { get; init; }

    public required int NonLiteralAddressablesLoadCount { get; init; }
}

/// <summary>
/// Phase 2 orchestration: parses every .cs the GUID dictionary knows, builds a declared-type
/// index, and resolves type-name occurrences to script GUIDs — syntax only, so a name resolves
/// iff it matches project/package source; DLL-only types (UnityEngine.*) simply produce no edge.
/// </summary>
public static class CodeScanner
{
    /// <summary>Unity's fileID of a .cs asset's MonoScript main object (what m_Script refs point at).</summary>
    public const long MonoScriptFileId = 11500000;

    /// <summary>Unity class ID of MonoScript.</summary>
    public const int MonoScriptClassId = 115;

    public static CodeScanResult Scan(string projectRoot, GuidDictionary dictionary)
    {
        var scripts = dictionary.Entries
            .Where(e => e.AssetType == "Script" && e.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .Select(e => (Entry: e, FullPath: Path.Combine(projectRoot, e.Path)))
            .Where(x => File.Exists(x.FullPath))
            .Select(x => new ScriptSource(x.Entry.Guid, x.Entry.Path, File.ReadAllText(x.FullPath)))
            .ToList();
        return Scan(scripts, dictionary.Entries,
            AddressableGroupScanner.BuildKeyIndex(projectRoot, dictionary.Entries));
    }

    public static CodeScanResult Scan(
        IReadOnlyList<ScriptSource> scripts, IEnumerable<AssetEntry> allAssets,
        IReadOnlyDictionary<string, List<string>>? addressablesIndex = null)
    {
        // Parsing is pure and dominates the cost (8k+ files with PackageCache) — parallelize;
        // everything after runs in deterministic script order.
        var models = new CSharpSourceModel[scripts.Count];
        Parallel.For(0, scripts.Count, i => models[i] = CSharpSourceParser.Parse(scripts[i].Text));

        var typeIndex = BuildTypeIndex(scripts, models);
        var resourcesIndex = BuildResourcesIndex(allAssets);

        var results = new List<ScriptRefs>();
        int ambiguous = 0, sameFile = 0, unresolvedLoads = 0, nonLiteralLoads = 0;
        int unresolvedAddressables = 0, nonLiteralAddressables = 0;
        for (var i = 0; i < scripts.Count; i++)
        {
            var (script, model) = (scripts[i], models[i]);
            nonLiteralLoads += model.NonLiteralResourceLoads;
            nonLiteralAddressables += model.NonLiteralAddressablesLoads;
            var refs = new List<CodeRef>();

            foreach (var site in model.TypeRefs)
            {
                foreach (var guid in ResolveType(site, model, typeIndex, ref ambiguous))
                {
                    if (guid == script.Guid)
                    {
                        sameFile++;
                    }
                    else
                    {
                        refs.Add(new CodeRef(guid, MonoScriptFileId, site.PropertyPath, site.Kind));
                    }
                }
            }

            foreach (var load in model.ResourceLoads)
            {
                var key = load.ResourcePath.Trim('/').ToLowerInvariant();
                if (resourcesIndex.TryGetValue(key, out var guids))
                {
                    refs.AddRange(guids.Select(guid =>
                        new CodeRef(guid, ToFileId: 0, load.PropertyPath, RefKind.ResourcesLoad)));
                }
                else
                {
                    unresolvedLoads++;
                }
            }

            // Addressables keys are case-sensitive exact matches against the address
            // and label tables of every group (Addressables backlog).
            foreach (var load in model.AddressablesLoads)
            {
                if (addressablesIndex is not null && addressablesIndex.TryGetValue(load.Key, out var guids))
                {
                    refs.AddRange(guids.Select(guid =>
                        new CodeRef(guid, ToFileId: 0, load.PropertyPath, RefKind.AddressablesLoad)));
                }
                else
                {
                    unresolvedAddressables++;
                }
            }

            if (refs.Count > 0)
            {
                results.Add(new ScriptRefs(
                    script.Guid,
                    model.DeclaredTypes.FirstOrDefault(t => !t.IsNested)?.Name,
                    refs));
            }
        }

        return new CodeScanResult
        {
            Scripts = results,
            ScriptFileCount = scripts.Count,
            DeclaredTypeCount = models.Sum(m => m.DeclaredTypes.Count),
            AmbiguousTypeRefCount = ambiguous,
            SameFileRefCount = sameFile,
            UnresolvedResourceLoadCount = unresolvedLoads,
            NonLiteralResourceLoadCount = nonLiteralLoads,
            UnresolvedAddressablesLoadCount = unresolvedAddressables,
            NonLiteralAddressablesLoadCount = nonLiteralAddressables,
        };
    }

    private static Dictionary<string, List<(DeclaredType Type, string Guid)>> BuildTypeIndex(
        IReadOnlyList<ScriptSource> scripts, CSharpSourceModel[] models)
    {
        var index = new Dictionary<string, List<(DeclaredType, string)>>(StringComparer.Ordinal);
        for (var i = 0; i < scripts.Count; i++)
        {
            foreach (var type in models[i].DeclaredTypes)
            {
                if (!index.TryGetValue(type.Name, out var list))
                {
                    index[type.Name] = list = [];
                }

                list.Add((type, scripts[i].Guid));
            }
        }

        return index;
    }

    /// <summary>
    /// Resolves one type-name occurrence to declaring-script GUIDs. Qualified names match by
    /// full-name suffix (covers both namespace and outer-type qualifiers); unqualified names
    /// match non-nested types whose namespace is in scope (enclosing chain, global, or a using).
    /// Exactly one distinct full name may match — several files with that full name (partial
    /// types) all get an edge; two different types sharing the name are ambiguous and skipped.
    /// </summary>
    private static IEnumerable<string> ResolveType(
        TypeRefSite site, CSharpSourceModel model,
        Dictionary<string, List<(DeclaredType Type, string Guid)>> typeIndex, ref int ambiguous)
    {
        var text = site.TypeText;
        var firstDot = text.IndexOf('.');
        var firstSegment = firstDot < 0 ? text : text[..firstDot];
        if (model.UsingAliases.TryGetValue(firstSegment, out var aliasTarget))
        {
            text = firstDot < 0 ? aliasTarget : aliasTarget + text[firstDot..];
        }

        List<(DeclaredType Type, string Guid)> matches;
        var lastDot = text.LastIndexOf('.');
        if (lastDot >= 0)
        {
            if (!typeIndex.TryGetValue(text[(lastDot + 1)..], out var candidates))
            {
                return [];
            }

            matches = candidates
                .Where(c => c.Type.FullName == text
                            || c.Type.FullName.EndsWith("." + text, StringComparison.Ordinal))
                .ToList();
        }
        else
        {
            if (!typeIndex.TryGetValue(text, out var candidates))
            {
                return [];
            }

            var scopes = new HashSet<string>(model.Usings, StringComparer.Ordinal) { "" };
            for (var scope = site.EnclosingNamespace; scope.Length > 0;)
            {
                scopes.Add(scope);
                var dot = scope.LastIndexOf('.');
                scope = dot < 0 ? "" : scope[..dot];
            }

            matches = candidates
                .Where(c => !c.Type.IsNested && scopes.Contains(c.Type.Namespace))
                .ToList();
        }

        if (matches.Count == 0)
        {
            return [];
        }

        if (matches.Select(m => m.Type.FullName).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            ambiguous++;
            return [];
        }

        return matches.Select(m => m.Guid).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Load-path → GUIDs over every asset under a Resources/ folder: extension stripped,
    /// lower-cased (the editor resolves case-insensitively). Folder assets are included,
    /// so Resources.LoadAll("Dir") gets an edge to the folder node.
    /// </summary>
    private static Dictionary<string, List<string>> BuildResourcesIndex(IEnumerable<AssetEntry> allAssets)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var asset in allAssets)
        {
            var segments = asset.Path.Split('/');
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] != "Resources")
                {
                    continue;
                }

                var relative = string.Join('/', segments[(i + 1)..]);
                if (asset.AssetType != "Folder")
                {
                    var extension = Path.GetExtension(relative);
                    relative = relative[..^extension.Length];
                }

                var key = relative.ToLowerInvariant();
                if (!index.TryGetValue(key, out var guids))
                {
                    index[key] = guids = [];
                }

                if (!guids.Contains(asset.Guid))
                {
                    guids.Add(asset.Guid);
                }
            }
        }

        return index;
    }
}
