# unity-ariadne の語彙(オントロジー)

`describe_ontology` が返すのは「このプロジェクトに実在する」語彙です。ここにあるのは、その意味と読み方です。

## 関係(ref_kind)

向きは常に「**参照する側(from) → 参照される側(to)**」です。`traverse` の `direction: "out"` は from → to、`"in"` は to → from にたどります。

### アセット層(シーン・プレハブ・アセット・.meta・JSON から)

| 関係 | 典型的な from → to | out の読み方 | in の読み方 | 推移的にたどる意味 |
|---|---|---|---|---|
| `uses_script` | Prefab / Scene / ScriptableObject → Script | コンポーネントとしてスクリプトを使う | そのスクリプトが付いているアセット | なし(1 段) |
| `prefab_instance` | Scene / Prefab → Prefab | ネストしたプレハブを置いている | そのプレハブを置いているシーン・プレハブ | **あり**: 入れ子の配置をたどる |
| `source_prefab` | Prefab(バリアント) → Prefab | バリアントの元プレハブ | そのプレハブのバリアント | **あり**: バリアントの系譜 |
| `prefab_modification` | Scene / Prefab → Prefab | 配置したプレハブの値をオーバーライドしている | オーバーライドされている | なし |
| `meta_external` | Model / ScriptedImporter のアセット → Material など | インポート設定で外部オブジェクトを割り当てている | 割り当て先として使われている | なし |
| `build_settings` | ProjectSettings(EditorBuildSettings) → Scene | ビルドに含めるシーン | ビルドに含まれている | なし |
| `embedded_json` | ShaderGraph / ShaderSubGraph / VisualEffect → Texture・SubGraph など | グラフの中で使っている | グラフから使われている | サブグラフの入れ子なら意味あり |
| `addressable_entry` | Addressables グループ(ScriptableObject) → 任意 | Addressable として登録している | Addressable に登録されている | なし |
| `asset_reference` | 任意 → 任意 | `AssetReference` フィールドで参照している | `AssetReference` で参照されている | なし |
| `generic` | 任意 → 任意 | 上記以外の参照(Material → Texture / Shader、Scene → Material、AnimatorController → AnimationClip など) | 同左の逆 | 影響範囲としては意味あり |

### コード層(C# スクリプトの構文解析から。from も to も基本は Script)

| 関係 | out の読み方 | in の読み方 | 推移的にたどる意味 |
|---|---|---|---|
| `inherits` | 基底クラス・インターフェース | サブクラス・実装クラス | **あり**: クラス階層 |
| `serialized_field` | シリアライズされるフィールドの型 | その型をフィールドに持つスクリプト | 型の包含関係 |
| `nonserialized_field` | それ以外のフィールドの型 | 同左の逆 | 型の包含関係 |
| `get_component` | `GetComponent<T>` / `[RequireComponent]` / `Find*ByType` で使う型 | その型を取得しにくるスクリプト | 実行時の協調関係 |
| `custom_editor` | `[CustomEditor(typeof(X))]` の X | そのスクリプトのカスタムエディタ | なし |
| `resources_load` | `Resources.Load` のリテラルで読むアセット(Script → 任意) | そのアセットを読むスクリプト | なし |
| `addressables_load` | Addressables のリテラルキーで読むアセット(Script → 任意) | そのアセットを読むスクリプト | なし |

コード層の型解決は構文だけで行うため、DLL の型・同名で曖昧な型・`#if !UNITY_EDITOR` の中は関係になりません。

## クラス(asset_type)

主なものを役割ごとにまとめます。実在する型と件数は `describe_ontology` で確認してください。

| 役割 | クラス |
|---|---|
| 配置の入れ物 | `Scene`、`Prefab` |
| コード | `Script`、`AssemblyDefinition`、`AssemblyDefinitionReference`、`Assembly`(DLL) |
| データ | `ScriptableObject`、`SerializedAsset`、`TextAsset`、`InputActions` |
| 描画 | `Material`、`Shader`、`ShaderGraph`、`ShaderSubGraph`、`ShaderInclude`、`ComputeShader`、`Texture`、`Cubemap`、`RenderTexture`、`SpriteAtlas` |
| 形状・地形 | `Model`、`Mesh`、`SpeedTree`、`TerrainData`、`TerrainLayer` |
| アニメーション | `AnimatorController`、`AnimatorOverrideController`、`AnimationClip`、`AvatarMask` |
| エフェクト | `VisualEffect`、`VisualEffectSubgraph` |
| 音・映像 | `AudioClip`、`AudioMixer`、`VideoClip` |
| UI | `Font`、`VisualTreeAsset`、`StyleSheet` |
| 物理・ライティング | `PhysicsMaterial`、`PhysicsMaterial2D`、`LightingSettings`、`LightmapParameters` |
| その他 | `Folder`、`ProjectSettings`(GUID の代わりにパスを持つ設定ファイル)、`Unknown`(未解決 GUID など) |

上位の概念(「描画系のアセット」など)で絞りたいときは、`types` にこの表の複数のクラスを並べます(例: `types: ["Shader", "ShaderGraph"]`)。

## 出どころ(source)

`Assets`(プロジェクト自身)/ `Packages`(埋め込みパッケージ)/ `PackageCache`(取得したパッケージ)/ `ProjectSettings` / `BuiltIn`(Unity 組み込みリソース)/ `Unresolved`(参照切れ。結果では `dangling: true`)。`search_nodes` の `source` で絞れます。
