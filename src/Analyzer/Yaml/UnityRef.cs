namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// One <c>{fileID: n, guid: g, type: t}</c> reference found in a document, with the
/// property path it was found at (Phase 1b).
/// </summary>
public sealed record UnityRef
{
    public required long FileId { get; init; }

    /// <summary>Target asset GUID; null means a reference within the same file.</summary>
    public string? Guid { get; init; }

    /// <summary>Unity's ref <c>type</c> discriminator (e.g. 2 asset, 3 imported), when present.</summary>
    public int? Type { get; init; }

    /// <summary>Where the reference sits, e.g. <c>m_Script</c>, <c>m_Materials[0]</c>, <c>m_Modification.m_Modifications[3].target</c>.</summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    /// True when the reference was found inside JSON — a .shadergraph/.shadersubgraph
    /// file or a JSON-in-YAML string scalar (Phase 1g). Always carries a guid.
    /// </summary>
    public bool FromEmbeddedJson { get; init; }

    /// <summary>
    /// True for an AddressableAssetGroup entry's bare <c>m_GUID</c> scalar (Addressables
    /// backlog). Always carries a guid; FileId is 0 (the whole asset is addressable).
    /// </summary>
    public bool FromAddressableEntry { get; init; }

    /// <summary>
    /// True for a serialized AssetReference's bare <c>m_AssetGUID</c> scalar (Addressables
    /// backlog). Always carries a guid; FileId is 0.
    /// </summary>
    public bool FromAssetReference { get; init; }

    /// <summary><c>{fileID: 0}</c> with no guid — Unity's null reference.</summary>
    public bool IsNull => FileId == 0 && Guid is null;
}
