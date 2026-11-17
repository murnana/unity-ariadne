namespace UnityAriadne.Graph;

/// <summary>What one relation (ref_kind) means, read in either direction.</summary>
/// <param name="Out">Reading of <c>A -[kind]-&gt; B</c> (A references B).</param>
/// <param name="In">What following the edge backwards from B finds.</param>
/// <param name="OutJa">Japanese reading of <paramref name="Out"/>.</param>
/// <param name="InJa">Japanese reading of <paramref name="In"/>.</param>
/// <param name="Transitive">Whether chaining the relation (max_hops &gt; 1) has a meaning of its own.</param>
public sealed record RelationMeaning(string Layer, string Out, string In, string OutJa, string InJa, bool Transitive)
{
    public string OutIn(Lang lang) => lang.Pick(Out, OutJa);

    public string InIn(Lang lang) => lang.Pick(In, InJa);
}

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
                "assets that have script B as a component",
                "A がスクリプト B をコンポーネントとして持つ(または B を型とする ScriptableObject である)",
                "スクリプト B をコンポーネントとして持つアセット", false),
            ["prefab_instance"] = new("asset",
                "A (scene or prefab) places an instance of prefab B",
                "scenes and prefabs that place prefab B",
                "A(シーンまたはプレハブ)がプレハブ B を配置している",
                "プレハブ B を配置しているシーン・プレハブ", true),
            ["source_prefab"] = new("asset",
                "A is a prefab variant of base prefab B",
                "variants of prefab B",
                "A はプレハブ B のバリアントである",
                "プレハブ B のバリアント", true),
            ["prefab_modification"] = new("asset",
                "A overrides values of placed prefab B",
                "scenes and prefabs that override prefab B",
                "A が配置したプレハブ B の値をオーバーライドしている",
                "プレハブ B の値をオーバーライドしているシーン・プレハブ", false),
            ["meta_external"] = new("asset",
                "A's import settings (.meta) assign B, e.g. a model's remapped material",
                "assets whose import settings assign B",
                "A のインポート設定(.meta)が B を割り当てている(モデルのマテリアル差し替えなど)",
                "インポート設定で B を割り当てているアセット", false),
            ["build_settings"] = new("asset",
                "build settings A include scene B",
                "the build settings that include scene B",
                "ビルド設定 A がシーン B をビルドに含めている",
                "シーン B をビルドに含めているビルド設定", false),
            ["embedded_json"] = new("asset",
                "graph A (ShaderGraph, VFX) uses B inside its JSON",
                "shader graphs and VFX graphs that use B",
                "グラフ A(ShaderGraph・VFX)が内部の JSON で B を使っている",
                "B を使っているシェーダーグラフ・VFX グラフ", true),
            ["addressable_entry"] = new("asset",
                "Addressables group A registers B as addressable",
                "the Addressables group that registers B",
                "Addressables グループ A が B を Addressable として登録している",
                "B を登録している Addressables グループ", false),
            ["asset_reference"] = new("asset",
                "A references B through an AssetReference (GUID string) field",
                "assets that reference B through an AssetReference",
                "A が AssetReference(GUID 文字列)のフィールドで B を参照している",
                "AssetReference で B を参照しているアセット", false),
            ["generic"] = new("asset",
                "A references B through a serialized field (e.g. material -> texture/shader, scene -> material)",
                "assets that reference B through a serialized field",
                "A がシリアライズされたフィールドで B を参照している(マテリアル → テクスチャ・シェーダー、シーン → マテリアルなど)",
                "シリアライズされたフィールドで B を参照しているアセット", true),
            ["inherits"] = new("code",
                "class A derives from or implements B",
                "subclasses and implementations of B",
                "クラス A が B を継承・実装している",
                "B のサブクラス・実装クラス", true),
            ["serialized_field"] = new("code",
                "script A has a serialized field of type B",
                "scripts with a serialized field of type B",
                "スクリプト A が型 B のシリアライズされるフィールドを持つ",
                "型 B のシリアライズされるフィールドを持つスクリプト", true),
            ["nonserialized_field"] = new("code",
                "script A has a non-serialized field of type B",
                "scripts with a non-serialized field of type B",
                "スクリプト A が型 B のシリアライズされないフィールドを持つ",
                "型 B のシリアライズされないフィールドを持つスクリプト", true),
            ["get_component"] = new("code",
                "script A looks up component B (GetComponent<B>, [RequireComponent], Find*ByType)",
                "scripts that look up component B",
                "スクリプト A がコンポーネント B を取得する(GetComponent<B>、[RequireComponent]、Find*ByType)",
                "コンポーネント B を取得するスクリプト", false),
            ["custom_editor"] = new("code",
                "editor script A is the custom inspector of B",
                "custom inspectors of B",
                "エディタースクリプト A が B のカスタムインスペクターである",
                "B のカスタムインスペクター", false),
            ["resources_load"] = new("code",
                "script A loads B by a literal Resources.Load path",
                "scripts that load B through Resources.Load",
                "スクリプト A が Resources.Load のリテラルパスで B を読み込む",
                "Resources.Load で B を読み込むスクリプト", false),
            ["addressables_load"] = new("code",
                "script A loads B by a literal Addressables key",
                "scripts that load B through Addressables",
                "スクリプト A が Addressables のリテラルキーで B を読み込む",
                "Addressables で B を読み込むスクリプト", false),
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

    public static string CategoryOf(string assetType) =>
        TypeCategories.TryGetValue(assetType, out var category) ? category : "Other";

    /// <summary>
    /// Conventions a path implies (Unity's special folders, package origin, dangling GUID):
    /// facts the reader should weigh, e.g. that an unreferenced asset under Resources/ may
    /// still be loaded by name.
    /// </summary>
    public static IReadOnlyList<string> PathNotes(AssetNode asset, Lang lang = Lang.En)
    {
        var notes = new List<string>();
        switch (asset.Source)
        {
            case "Unresolved":
                notes.Add(lang.Pick("unresolved GUID: the referenced asset does not exist",
                    "未解決の GUID: 参照先のアセットが存在しない"));
                return notes;
            case "BuiltIn":
                notes.Add(lang.Pick("Unity built-in resource", "Unity の組み込みリソース"));
                return notes;
            case "PackageCache":
                notes.Add(lang.Pick("package from the package cache (read-only, not project content)",
                    "パッケージキャッシュのパッケージ(読み取り専用。プロジェクト自身のアセットではない)"));
                break;
        }

        var segments = (asset.Path ?? "").Split('/');
        var folders = segments.Take(segments.Length - 1).ToList();
        foreach (var (folder, en, ja) in FolderNotes)
        {
            if (folders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add(lang.Pick(en, ja));
            }
        }

        if (folders.Any(f => f.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
                             || f.Equals("Test", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add(lang.Pick("test folder: test code or test data", "テストフォルダー: テストコードまたはテストデータ"));
        }

        return notes;
    }

    private static readonly (string Folder, string En, string Ja)[] FolderNotes =
    [
        ("Editor", "Editor folder: editor-only, not included in player builds",
            "Editor フォルダー: エディター専用で、ビルドには含まれない"),
        ("Editor Default Resources", "Editor Default Resources: editor-only",
            "Editor Default Resources: エディター専用"),
        ("Gizmos", "Gizmos folder: editor-only icons", "Gizmos フォルダー: エディター専用のアイコン"),
        ("Resources", "Resources folder: loadable by name with Resources.Load; may be used without any reference",
            "Resources フォルダー: Resources.Load で名前から読み込めるため、参照が無くても使われている可能性がある"),
        ("StreamingAssets", "StreamingAssets: copied as-is into builds and loaded by file path",
            "StreamingAssets: そのままビルドにコピーされ、ファイルパスで読み込まれる"),
        ("Plugins", "Plugins folder: usually third-party or native code",
            "Plugins フォルダー: 多くはサードパーティーまたはネイティブのコード"),
        ("ThirdParty", "third-party content", "サードパーティーのコンテンツ"),
        ("Samples", "sample content", "サンプルのコンテンツ"),
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
