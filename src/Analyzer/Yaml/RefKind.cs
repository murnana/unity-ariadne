namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// Values of the <c>refs.ref_kind</c> column (Phase 1c). See the classification table
/// in CLAUDE.md.
/// </summary>
public static class RefKind
{
    /// <summary>Component → script (<c>m_Script</c>).</summary>
    public const string UsesScript = "uses_script";

    /// <summary>Prefab variant → its base prefab (root PrefabInstance of a .prefab, or legacy <c>m_ParentPrefab</c>).</summary>
    public const string SourcePrefab = "source_prefab";

    /// <summary>Scene/prefab → a prefab nested in it (<c>m_SourcePrefab</c> of a non-root PrefabInstance).</summary>
    public const string PrefabInstance = "prefab_instance";

    /// <summary>Override target of a prefab instance (<c>m_Modifications[*].target</c>).</summary>
    public const string PrefabModification = "prefab_modification";

    /// <summary>Instance object → the source-prefab object it was instantiated from (<c>m_CorrespondingSourceObject</c>).</summary>
    public const string CorrespondingSource = "corresponding_source";

    /// <summary>Reference inside a .meta file, e.g. ModelImporter ExternalObjects (Phase 1d).</summary>
    public const string MetaExternal = "meta_external";

    /// <summary>Scene listed in EditorBuildSettings.asset (Phase 1d).</summary>
    public const string BuildSettings = "build_settings";

    /// <summary>
    /// Reference found inside JSON: a .shadergraph/.shadersubgraph file, or JSON embedded
    /// in a YAML string scalar such as .vfx <c>m_SerializableObject</c> (Phase 1g).
    /// </summary>
    public const string EmbeddedJson = "embedded_json";

    /// <summary>Any other <c>{fileID, guid}</c> reference.</summary>
    public const string Generic = "generic";

    /// <summary>Code layer (Phase 2): class/interface → base type in another script.</summary>
    public const string Inherits = "inherits";

    /// <summary>Code layer (Phase 2): Unity-serialized field (public or [SerializeField]/[SerializeReference]) whose type is another script.</summary>
    public const string SerializedField = "serialized_field";

    /// <summary>Code layer (backlog): any other field (private without attribute, or static/const/readonly) whose type is another script.</summary>
    public const string NonserializedField = "nonserialized_field";

    /// <summary>Code layer (backlog): editor script → the type it inspects ([CustomEditor(typeof(X))]).</summary>
    public const string CustomEditor = "custom_editor";

    /// <summary>
    /// Addressables (backlog): AddressableAssetGroup entry → the addressable asset
    /// (<c>m_SerializeEntries[*].m_GUID</c>, a bare 32-hex scalar next to <c>m_Address</c>).
    /// </summary>
    public const string AddressableEntry = "addressable_entry";

    /// <summary>
    /// Bare <c>m_AssetGUID</c> scalar → its target asset (backlog): Addressables'
    /// serialized <c>AssetReference</c> fields, but also other Unity types that store
    /// GUID strings the same way (URP's ProbeVolumeStreamableAsset in the fixture).
    /// </summary>
    public const string AssetReference = "asset_reference";

    /// <summary>
    /// Addressables (backlog): Addressables.LoadAssetAsync-family call with a literal key
    /// resolved via the AddressableAssetGroup address/label tables.
    /// </summary>
    public const string AddressablesLoad = "addressables_load";

    /// <summary>Code layer (Phase 2): GetComponent&lt;T&gt;-family call, [RequireComponent], or Find*ByType where T is another script.</summary>
    public const string GetComponent = "get_component";

    /// <summary>Code layer (Phase 2): Resources.Load/LoadAll/LoadAsync with a literal path resolved to an asset.</summary>
    public const string ResourcesLoad = "resources_load";
}
