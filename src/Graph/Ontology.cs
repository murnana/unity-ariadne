namespace GuidGraph.Graph;

/// <summary>What one relation (ref_kind) means, read in either direction.</summary>
/// <param name="Out">Reading of <c>A -[kind]-&gt; B</c> (A references B).</param>
/// <param name="In">What following the edge backwards from B finds.</param>
/// <param name="Transitive">Whether chaining the relation (max_hops &gt; 1) has a meaning of its own.</param>
public sealed record RelationMeaning(string Layer, string Out, string In, bool Transitive);

/// <summary>
/// The fixed part of the ontology: what each relation means and which role each asset
/// type plays, plus the folder conventions Unity gives special meaning to. The data part
/// (which types and relations exist in a project) comes from the database —
/// <see cref="GraphQueries.DescribeOntology"/>.
/// </summary>
public static class OntologyGlossary
{
    public static IReadOnlyDictionary<string, RelationMeaning> Relations { get; } =
        new Dictionary<string, RelationMeaning>(StringComparer.Ordinal)
        {
            ["uses_script"] = new("asset",
                "A has a component (or is a ScriptableObject) whose script is B",
                "assets that have script B as a component", false),
            ["prefab_instance"] = new("asset",
                "A (scene or prefab) places an instance of prefab B",
                "scenes and prefabs that place prefab B", true),
            ["source_prefab"] = new("asset",
                "A is a prefab variant of base prefab B",
                "variants of prefab B", true),
            ["prefab_modification"] = new("asset",
                "A overrides values of placed prefab B",
                "scenes and prefabs that override prefab B", false),
            ["meta_external"] = new("asset",
                "A's import settings (.meta) assign B, e.g. a model's remapped material",
                "assets whose import settings assign B", false),
            ["build_settings"] = new("asset",
                "build settings A include scene B",
                "the build settings that include scene B", false),
            ["embedded_json"] = new("asset",
                "graph A (ShaderGraph, VFX) uses B inside its JSON",
                "shader graphs and VFX graphs that use B", true),
            ["addressable_entry"] = new("asset",
                "Addressables group A registers B as addressable",
                "the Addressables group that registers B", false),
            ["asset_reference"] = new("asset",
                "A references B through an AssetReference (GUID string) field",
                "assets that reference B through an AssetReference", false),
            ["generic"] = new("asset",
                "A references B through a serialized field (e.g. material -> texture/shader, scene -> material)",
                "assets that reference B through a serialized field", true),
            ["inherits"] = new("code",
                "class A derives from or implements B",
                "subclasses and implementations of B", true),
            ["serialized_field"] = new("code",
                "script A has a serialized field of type B",
                "scripts with a serialized field of type B", true),
            ["nonserialized_field"] = new("code",
                "script A has a non-serialized field of type B",
                "scripts with a non-serialized field of type B", true),
            ["get_component"] = new("code",
                "script A looks up component B (GetComponent<B>, [RequireComponent], Find*ByType)",
                "scripts that look up component B", false),
            ["custom_editor"] = new("code",
                "editor script A is the custom inspector of B",
                "custom inspectors of B", false),
            ["resources_load"] = new("code",
                "script A loads B by a literal Resources.Load path",
                "scripts that load B through Resources.Load", false),
            ["addressables_load"] = new("code",
                "script A loads B by a literal Addressables key",
                "scripts that load B through Addressables", false),
        };

    /// <summary>Role (category) of each asset type; types not listed are "Other".</summary>
    public static IReadOnlyDictionary<string, string> TypeCategories { get; } = BuildCategories(
        ("Placement", ["Scene", "Prefab"]),
        ("Code", ["Script", "AssemblyDefinition", "AssemblyDefinitionReference", "Assembly"]),
        ("Data", ["ScriptableObject", "SerializedAsset", "TextAsset", "InputActions"]),
        ("Rendering", ["Material", "Shader", "ShaderGraph", "ShaderSubGraph", "ShaderInclude", "ComputeShader",
            "Texture", "Cubemap", "RenderTexture", "SpriteAtlas"]),
        ("Geometry", ["Model", "Mesh", "SpeedTree", "TerrainData", "TerrainLayer"]),
        ("Animation", ["AnimatorController", "AnimatorOverrideController", "AnimationClip", "AvatarMask"]),
        ("Effects", ["VisualEffect", "VisualEffectSubgraph"]),
        ("Audio/Video", ["AudioClip", "AudioMixer", "VideoClip"]),
        ("UI", ["Font", "VisualTreeAsset", "StyleSheet"]),
        ("Physics/Lighting", ["PhysicsMaterial", "PhysicsMaterial2D", "LightingSettings", "LightmapParameters"]),
        ("Settings", ["ProjectSettings"]),
        ("Folder", ["Folder"]),
        ("Unresolved", ["Unknown"]));

    /// <summary>
    /// Asset types whose files are binary formats the analyzer never opens: all their edges come from the
    /// .meta side only (TerrainData additionally hides its terrain-layer and tree references, LightingData
    /// its lightmap references). Text formats that merely carry no GUID references are not listed.
    /// </summary>
    public static IReadOnlySet<string> BodyNotParsedTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Texture", "Model", "SpeedTree", "AudioClip", "VideoClip", "Font", "TerrainData", "Assembly",
    };

    public static bool IsParsed(string assetType) => !BodyNotParsedTypes.Contains(assetType);

    public static string CategoryOf(string assetType) =>
        TypeCategories.TryGetValue(assetType, out var category) ? category : "Other";

    /// <summary>
    /// Conventions a path implies (Unity's special folders, package origin, dangling GUID):
    /// facts the reader should weigh, e.g. that an unreferenced asset under Resources/ may
    /// still be loaded by name.
    /// </summary>
    public static IReadOnlyList<string> PathNotes(AssetNode asset)
    {
        var notes = new List<string>();
        switch (asset.Source)
        {
            case "Unresolved":
                notes.Add("unresolved GUID: the referenced asset does not exist");
                return notes;
            case "BuiltIn":
                notes.Add("Unity built-in resource");
                return notes;
            case "PackageCache":
                notes.Add("package from the package cache (read-only, not project content)");
                break;
        }

        var segments = (asset.Path ?? "").Split('/');
        var folders = segments.Take(segments.Length - 1).ToList();
        foreach (var (folder, note) in FolderNotes)
        {
            if (folders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add(note);
            }
        }

        if (folders.Any(f => f.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
                             || f.Equals("Test", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add("test folder: test code or test data");
        }

        return notes;
    }

    private static readonly (string Folder, string Note)[] FolderNotes =
    [
        ("Editor", "Editor folder: editor-only, not included in player builds"),
        ("Editor Default Resources", "Editor Default Resources: editor-only"),
        ("Gizmos", "Gizmos folder: editor-only icons"),
        ("Resources", "Resources folder: loadable by name with Resources.Load; may be used without any reference"),
        ("StreamingAssets", "StreamingAssets: copied as-is into builds and loaded by file path"),
        ("Plugins", "Plugins folder: usually third-party or native code"),
        ("ThirdParty", "third-party content"),
        ("Samples", "sample content"),
    ];

    private static Dictionary<string, string> BuildCategories(params (string Category, string[] Types)[] groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (category, types) in groups)
        {
            foreach (var type in types)
            {
                map[type] = category;
            }
        }

        return map;
    }
}
