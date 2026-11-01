# 既知の制約と近似

Unity Ariadne は Unity Editor API を使わず、ファイルを直接パースします。この設計上、**原理的にグラフに現れない依存関係**がいくつかあります。「参照があるはずなのに出てこない」ときはまずこちらをご確認ください。

## アセット層

### バイナリアセットの中身は読みません

- 解析対象はテキスト(YAML / JSON / .cs / .meta)のみです。バイナリアセットは GUID 辞書にノードとして登録されますが、**その中身が持つ参照は見えません**。
- **`LightingData.asset` と `TerrainData.asset` は Force Text 設定でも常にバイナリ**です。LightingData はシーン参照とベイク済みライトマップ / プローブ参照を、TerrainData は .terrainlayer 参照を持ちますが、どちらもグラフには出ません。実プロジェクトでは「ライティングデータ経由でしか繋がっていない依存チェーン」が丸ごと欠ける可能性があります。
- そもそも Asset Serialization が **Force Text でないプロジェクト**では、YAML アセットもバイナリになり .meta 層とコード層しか解析できません。

### Unity のインポート結果にしか存在しない依存

ソースファイルのどこにも書かれておらず、Unity のインポーターが生成する依存は検出できません:

- インポーターが割り当てるサブアセットスクリプト(例: `.inputactions` → InputActionAsset.cs、shadergraph → *Metadata.cs)
- `.shader` の名前文字列による依存(例: TerrainLit → TerrainLitBase.shader — シェーダー名での参照)

※ AssetDatabase.GetDependencies との突合テストで、**ツール側に余計なエッジがないこと**(過検出ゼロ)は確認済みです。欠けるのは上記のカテゴリのみです。

### 対応 Unity バージョン

フォーマットの検証は **Unity 6000.0(Unity 6)** の実プロジェクトに対して行っています。Unity の YAML / .meta フォーマットはバージョンで細部が変わるため、他バージョンはスコープ外です(動く可能性は高いですが未検証です)。

### PackageCache がないとパッケージ GUID は未解決になります

`Library/` は通常 git 管理外のため、クローン直後や CI ではパッケージアセットの GUID が解決できず Unresolved になります。対象プロジェクトを一度 Unity で開いて `Library/PackageCache` を生成すれば解決されます。

## コード層(C#)

### 構文解析のみです(コンパイルしません)

- Roslyn の **syntax-only** 解析を行います。型解決はプロジェクト内の全 .cs の宣言型インデックスに対する名前照合で行います。
- **DLL にしかない型はエッジを生みません**(UnityEngine.* コア型など)。パッケージソース(URP / uGUI / TMP / Cinemachine 等)は .cs のため解決されます。
- **曖昧な型名はスキップします**: 同じ単純名の非ネスト型が複数の名前空間に存在し、スコープ内で一意に決まらない場合、エッジを張らずに諦めます(`analyze` 出力の `skipped: ... ambiguous type names`)。
- 同一ファイル内での型参照はエッジにしません(自己参照は無意味なためです)。

### プリプロセッサの近似

`UNITY_EDITOR` + 累積 `UNITY_*_OR_NEWER`(Unity 6000.0 相当)を定義してパースします。つまり **`#if !UNITY_EDITOR` のランタイム専用ブランチのコードは見えません**。

### 文字列キーの解決はリテラルのみです

- `Resources.Load` / `Addressables.Load*` は**文字列リテラル引数のみ**解決します。変数・連結・補間で組み立てたキーはスキップされます(出力の `non-literal` カウント)。
- Addressables のキー照合(アドレス + ラベル)は**大文字小文字を区別します**。
- `AssetReference` フィールド経由のインスタンス呼び出し(`myRef.LoadAssetAsync()`)はキーを取らないため、コード層ではスキップされます — フィールド自体が `asset_reference` エッジとしてアセット層に出るため、依存としては欠けません。
- `addressables_load` は削除済みアセットを指すエントリに当たると dangling GUID をターゲットにできます(コード層で唯一です)。

### その他のスコープ外

- `[CustomEditorForRenderPipeline]` は対象外です(`[CustomEditor]` のみ対応)。
- 実行時のリフレクション、`SendMessage`、文字列ベースの `AddComponent` などは検出しません。

## クエリ・エクスポート層

- `corresponding_source` エッジ(prefab の `m_CorrespondingSourceObject`)は件数が膨大なため、**MCP ツールと jsonl エクスポートからは除外**されています。SQLite の refs テーブルには残っているため、必要な場合は直接引いてください([schema.md](schema.md))。
- MCP ツールのリストは 200 件で打ち切られます(`truncated: true`)。全件必要な場合は SQLite を直接引いてください。

## 未解決 GUID は「エラー」ではありません

`report` / `list_unresolved` に出る GUID は、(1) 本当に参照が切れている、(2) PackageCache 未生成でパッケージ GUID が引けない、のどちらかです。実プロジェクトでもある程度の参照切れは普通に存在します(アセットの間引きや削除の後には数十件残ることも珍しくありません)。

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
