namespace UnityAriadne.Analyzer.Storage;

/// <summary>
/// Counters accumulated by <see cref="GraphBuilder"/> during one analysis run,
/// used for the `analyze` console output (Phase 1e).
/// </summary>
public sealed class AnalysisSummary
{
    private readonly Dictionary<string, long> _yamlKindCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _settingsKindCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _codeKindCounts = new(StringComparer.Ordinal);

    public long YamlFileCount { get; internal set; }

    public long ObjectCount { get; internal set; }

    public long ExternalRefCount { get; internal set; }

    public long SameFileRefCount { get; internal set; }

    /// <summary><c>{fileID: 0}</c> refs, skipped before classification and never written.</summary>
    public long NullRefCount { get; internal set; }

    /// <summary>Kind → count over YAML asset refs (not meta/settings).</summary>
    public IReadOnlyDictionary<string, long> YamlKindCounts => _yamlKindCounts;

    public long MetaRefCount { get; internal set; }

    public long MetaFilesWithRefs { get; internal set; }

    public long MetaRegexFallbackCount { get; internal set; }

    /// <summary>embedded_json refs from JSON asset files (.shadergraph/.shadersubgraph, Phase 1g).
    /// JSON-in-YAML refs (.vfx) are counted in <see cref="YamlKindCounts"/> instead.</summary>
    public long JsonRefCount { get; internal set; }

    public long JsonFilesWithRefs { get; internal set; }

    /// <summary>Per-asset pseudo-objects (file_id 0) actually created for .meta/JSON refs —
    /// less than <see cref="MetaFilesWithRefs"/> + <see cref="JsonFilesWithRefs"/> when shared.</summary>
    public long PseudoObjectCount { get; internal set; }

    /// <summary>.cs files parsed by the Phase 2 code scan.</summary>
    public long ScriptFileCount { get; internal set; }

    /// <summary>Types (class/struct/interface/enum/record/delegate) declared across all scripts.</summary>
    public long DeclaredTypeCount { get; internal set; }

    /// <summary>Code-layer refs written (inherits/serialized_field/get_component/resources_load).</summary>
    public long CodeRefCount { get; internal set; }

    /// <summary>Kind → count over code refs.</summary>
    public IReadOnlyDictionary<string, long> CodeKindCounts => _codeKindCounts;

    /// <summary>MonoScript object rows (file_id 11500000) created for scripts with code refs.</summary>
    public long ScriptObjectCount { get; internal set; }

    /// <summary>Type names matching ≥2 distinct in-scope declarations — skipped, never guessed.</summary>
    public long AmbiguousTypeRefCount { get; internal set; }

    /// <summary>Type refs resolving to the referencing file itself — skipped, not written.</summary>
    public long SameFileCodeRefCount { get; internal set; }

    /// <summary>Literal Resources.Load paths with no matching asset.</summary>
    public long UnresolvedResourceLoadCount { get; internal set; }

    /// <summary>Resources.Load calls whose path is not a string literal — unresolvable.</summary>
    public long NonLiteralResourceLoadCount { get; internal set; }

    /// <summary>Literal Addressables.Load* keys matching no group entry address/label.</summary>
    public long UnresolvedAddressablesLoadCount { get; internal set; }

    /// <summary>Addressables.Load* calls whose key is not a string literal.</summary>
    public long NonLiteralAddressablesLoadCount { get; internal set; }

    /// <summary>Kind → count over ProjectSettings refs.</summary>
    public IReadOnlyDictionary<string, long> SettingsKindCounts => _settingsKindCounts;

    public long SettingsRefCount { get; internal set; }

    public long SettingsFileCount { get; internal set; }

    /// <summary>Referenced GUIDs with no .meta anywhere — inserted with source='Unresolved'.</summary>
    public long UnresolvedGuidCount { get; internal set; }

    /// <summary>Referenced built-in Unity GUIDs — inserted with source='BuiltIn'.</summary>
    public long BuiltInGuidCount { get; internal set; }

    public long TotalRefCount =>
        ExternalRefCount + SameFileRefCount + MetaRefCount + SettingsRefCount + JsonRefCount + CodeRefCount;

    internal void CountYamlKind(string kind) =>
        _yamlKindCounts[kind] = _yamlKindCounts.GetValueOrDefault(kind) + 1;

    internal void CountCodeKind(string kind) =>
        _codeKindCounts[kind] = _codeKindCounts.GetValueOrDefault(kind) + 1;

    internal void CountSettingsKind(string kind) =>
        _settingsKindCounts[kind] = _settingsKindCounts.GetValueOrDefault(kind) + 1;
}
