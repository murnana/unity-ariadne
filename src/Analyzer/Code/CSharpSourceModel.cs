namespace UnityAriadne.Analyzer.Code;

/// <summary>A type declared in a scanned .cs file (class/struct/interface/enum/record/delegate).</summary>
/// <param name="Name">Simple name without generic arity (<c>Foo</c> for <c>Foo&lt;T&gt;</c>).</param>
/// <param name="FullName">Namespace + declaring-type chain + name, dotted.</param>
/// <param name="Namespace">Namespace chain only ("" for the global namespace).</param>
/// <param name="IsNested">Declared inside another type; unqualified names never resolve to these.</param>
public sealed record DeclaredType(string Name, string FullName, string Namespace, bool IsNested);

/// <summary>
/// A declaration worth searching for (schema v3 code_symbols): every declared type, plus
/// members that carry an XML documentation comment.
/// </summary>
/// <param name="Symbol">Dotted name: namespace + type chain (+ member), e.g. <c>Game.Enemy.TakeDamage</c>.</param>
/// <param name="Kind">class / struct / interface / enum / record / delegate / method / constructor /
/// property / indexer / field / event / enum_member / operator.</param>
/// <param name="Doc">Plain text of the doc comment's &lt;summary&gt; and &lt;remarks&gt;, or null.</param>
public sealed record CodeSymbol(string Symbol, string Kind, string? Doc);

/// <summary>
/// One type-name occurrence the parser extracted (Phase 2), before resolution against the
/// project-wide declared-type index.
/// </summary>
/// <param name="TypeText">Dotted name as written, generic args stripped (<c>IInputProvider</c>, <c>Cinemachine.CinemachineCamera</c>).</param>
/// <param name="PropertyPath">Where it occurred, e.g. <c>RTSCameraController.currentSettings</c>.</param>
/// <param name="Kind">One of the code-layer <see cref="Yaml.RefKind"/> values.</param>
/// <param name="EnclosingNamespace">Namespace chain at the occurrence, for scope-based disambiguation.</param>
public sealed record TypeRefSite(string TypeText, string PropertyPath, string Kind, string EnclosingNamespace);

/// <summary>A Resources.Load/LoadAll/LoadAsync call with a literal path argument.</summary>
public sealed record ResourceLoadSite(string ResourcePath, string PropertyPath);

/// <summary>An Addressables.LoadAssetAsync-family call with a literal key argument.</summary>
public sealed record AddressablesLoadSite(string Key, string PropertyPath);

/// <summary>Everything <see cref="CSharpSourceParser"/> extracts from one .cs file.</summary>
public sealed class CSharpSourceModel
{
    public List<DeclaredType> DeclaredTypes { get; } = [];

    public List<TypeRefSite> TypeRefs { get; } = [];

    /// <summary>Declared types and documented members, in source order.</summary>
    public List<CodeSymbol> Symbols { get; } = [];

    public List<ResourceLoadSite> ResourceLoads { get; } = [];

    /// <summary>Imported namespaces (all using directives in the file, any nesting level).</summary>
    public HashSet<string> Usings { get; } = new(StringComparer.Ordinal);

    /// <summary>Alias usings: alias → dotted target (<c>Touch</c> → <c>UnityEngine.InputSystem.EnhancedTouch.Touch</c>).</summary>
    public Dictionary<string, string> UsingAliases { get; } = new(StringComparer.Ordinal);

    /// <summary>Resources.Load-family calls whose path argument is not a string literal.</summary>
    public int NonLiteralResourceLoads { get; internal set; }

    public List<AddressablesLoadSite> AddressablesLoads { get; } = [];

    /// <summary>Addressables.Load*-family calls whose key argument is not a string literal
    /// (typically an AssetReference variable — those assets are visible to the asset
    /// layer as m_AssetGUID fields instead).</summary>
    public int NonLiteralAddressablesLoads { get; internal set; }
}
