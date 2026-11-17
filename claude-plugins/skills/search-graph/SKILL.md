---
name: search-graph
description: Unity プロジェクトの依存関係グラフを、型(アセットの種類)と関係(参照の種類)を持つオントロジーとして検索します。ファイル名・フォルダー名・C# の XML ドキュメントコメントも手がかりにします。「このスクリプトを継承したクラスを使っているプレハブは?」「ボスの行動に関係するスクリプトが付いたプレハブは?」「UI フォルダーのプレハブが使っているスクリプトは?」「どこからも使われていないプレハブは?」のように、種類・関係・名前・意味で条件を付けた質問に、根拠(参照経路や当たった名前・コメント)つきで答えます。
when_to_use: Unity のアセットやスクリプトの関係を問う質問全般。とくに「〜を使っている〜」「〜に含まれる〜」「〜を継承した〜」「〜経由で」「未使用の〜」「〜に関係する〜」「〜フォルダーの〜」「どのシーン/プレハブ/マテリアルが」のように、アセットの種類や参照の種類で絞り込む・複数段たどる必要があるとき。単にファイルの中身やコードのシンボルを探すだけなら grep のほうが速いので使いません。
---

# オントロジー検索

unity-ariadne のグラフは、ただのファイル依存リストではなく、次の 2 つの語彙を持つオントロジーです。

- **クラス** = ノードの型(`asset_type`): `Scene` / `Prefab` / `Script` / `Material` / `Shader` / `Texture` …
- **関係** = エッジの型(`ref_kind`): `uses_script`(コンポーネント → スクリプト)、`prefab_instance`(シーン/プレハブ → ネストしたプレハブ)、`inherits`(サブクラス → 基底クラス)…

さらに、個々のアセットには**名前と意味の手がかり**が付いています。

- **パス**: フォルダー名(`Characters/Enemies`、`UI`、`Editor`、`Resources` …)とファイル名(`Boss.prefab`、`SM_Rock_01.fbx` …)。プロジェクトが自分で付けた分類です
- **C# の宣言とドキュメントコメント**: スクリプトが宣言している型の名前(ファイル名と違うこともあります)と、`/// <summary>` に書かれた説明。「何のためのコードか」が自然言語で書かれています(日本語のことも多いです)

質問を「**どのクラスから、どの関係を、どちら向きに、何段たどり、どのクラスに着くか**」という型付きの経路に翻訳し、出発点や終点の絞り込みに名前と意味の手がかりを使って、unity-ariadne の MCP ツールで実行します。grep では拾えない「GUID でつながった関係」を型で絞ったまま推移的にたどれることと、「ボス」「UI」のような**概念から出発できる**ことが強みです。

語彙の意味と、関係ごとの典型的な向き(どの型からどの型へ張られるか)は [ontology.md](ontology.md) にまとめてあります。迷ったら読んでください。

## 使うツール

| ツール | 役割 |
|---|---|
| `describe_ontology` | **このプロジェクトに実在する**クラス(型と件数)と関係(件数と `From型->To型` のシグネチャ)を返します。最初に 1 回呼びます |
| `list_folders(path, depth)` | フォルダー構成と、各フォルダーにある種類別のアセット数。プロジェクトの分類(どこに何があるか)をつかむのに使います |
| `search_keywords(keywords, type, source, match_all)` | 概念から個体を探します。ファイル名・型名・フォルダー名・ドキュメント付きメンバー名・ドキュメントコメント本文を部分一致で検索し、`clues`(どこに当たったか)と `doc`(コメントの抜粋)を返します |
| `search_nodes(query, type, source, unreferenced)` | 個体(アセット)を探します。`type` でクラスを絞り、`query` を空にするとそのクラスの一覧になります。`unreferenced: true` で「どこからも参照されていないもの」だけ |
| `traverse(start / start_type, steps, start_path_contains)` | 型付き経路クエリ。`start`(1 つのアセット)か `start_type`(あるクラスの全アセット)から、`steps` を順に適用します。結果には根拠の経路(`A -[kind]-> B` は A が B を参照、`A <-[kind]- B` は B が A を参照)が付きます |
| `find_references` / `impact_analysis` / `get_neighbors` | 型を問わない素朴な逆参照・影響範囲・近傍の確認に使います |

`traverse` の 1 ステップ:

```json
{"direction": "in", "kinds": ["inherits"], "types": ["Script"], "max_hops": 10, "include_self": true, "path_contains": ["/Enemies/"]}
```

- `direction`: `out` = 参照している先(依存先)へ、`in` = 参照してくる側(参照元)へ、`both` = 両方
- `kinds`: たどる関係。省略するとすべての関係
- `types`: そのステップの**終点**に許すクラス。省略すると任意。複数ホップの途中のノードは型を問いません
- `max_hops`: 同じ関係を最大何段たどるか(既定 1、最大 10)。`inherits` や `prefab_instance` のような推移的な関係に使います
- `include_self`: 0 段(出発点そのもの)も結果に含めます。「そのクラスと全サブクラス」のように使います
- `path_contains`: そのステップの終点を、パスにいずれかの文字列を含むものに絞ります(大文字小文字無視)。フォルダー名・ファイル名の手がかりです

`start_type` と一緒に `start_path_contains` を渡すと、出発点もパスで絞れます(例: `start_type: "Prefab", start_path_contains: ["/UI/"]`)。

## 手順

1. **質問を分解する。** 次の 4 つを書き出します。
   - 出発点: 特定のアセット(`Enemy.cs`)か、クラス全体(「すべてのシーン」)か
   - 関係の連鎖: 「継承した」→ `inherits`、「コンポーネントとして付いている」→ `uses_script`、「シーンに置かれている」→ `prefab_instance` … と関係名に置き換え、向き(`in` / `out`)を決めます。向きは「**参照する側 → 参照される側**」が `out` です。「X を使っている Y」は X から見て `in` です
   - 着地点のクラス: 最後に欲しいアセットの型(`Prefab`、`Scene` …)
   - 名前と意味の手がかり: 質問に出てくる**概念語**(「ボス」「UI」「敵の出現」)と**場所**(「UI フォルダーの」「Characters 以下の」)。概念語は英語の識別子にもなりうるので、日本語と英語の両方を用意します(「ボス」→ `["ボス", "boss"]`、「出現」→ `["出現", "spawn"]`)

2. **語彙を確かめる。** セッションで初めてなら `describe_ontology` を呼び、使う型名と関係名が実在するか、その関係が想定の型どうしを結んでいるか(シグネチャ)を確認します。たとえば「マテリアル → シェーダー」は専用の関係ではなく `generic`(`Material->Shader`)です。シグネチャに無い組み合わせを前提にしないでください。

   場所の手がかりがある質問、またはプロジェクトの構成が分からないときは、`list_folders` も呼びます(まず `depth: 1`、必要なら目的のフォルダーを `path` に指定して掘り下げます)。質問の「UI フォルダー」が実際には `Assets/_Game/UserInterface` だった、ということがよくあるので、`path_contains` に入れる文字列はここで実在する名前に合わせます。

3. **出発点を特定する。**
   - ファイル名が分かっている → `search_nodes`(`type` 付き。例: `search_nodes(query: "Enemy", type: "Script")`)
   - 概念で言われている → `search_keywords`(例: `search_keywords(keywords: ["ボス", "boss"], type: "Script")`)。`clues` と `doc` を読んで、本当に質問の意味に合う候補かを判断します。ファイル名・型名に当たったものは強い候補、ドキュメントコメントだけに当たったものは本文を読んで確かめます
   - 場所で言われている → `start_type` + `start_path_contains`
   - クラス全体 → `start_type`

   候補が複数なら、ユーザーに確認するか、それぞれから `traverse` します。

4. **`traverse` を実行する。** 分解した連鎖をそのまま `steps` にします。下の「翻訳例」を参考にしてください。

5. **結果を検証して、必要なら緩める。**
   - `warnings` が返ったら、型名・関係名の綴りか、そのプロジェクトに存在しない語彙です。`describe_ontology` の名前で直します
   - 0 件なら、(a) `path_contains` の綴りを `list_folders` の実在する名前と照らす、(b) `kinds` を外して関係を問わずにたどる、(c) `max_hops` を増やす、(d) 向きを確認する、の順に試し、**何を緩めたかを回答に書きます**
   - `truncated: true` なら上位 200 件だけです。`types` を足すか、出発点を絞って再実行します

6. **答える。** 結果のアセットを列挙し、各結果の根拠を添えます。根拠は 2 種類あり、**区別して書きます**。
   - **参照の根拠**(`traverse` の `path`): GUID でつながった事実です
   - **名前・意味の根拠**(`search_keywords` の `clues` / `doc`、`path_contains` で絞ったフォルダー): 命名やコメントからの推定です。「ドキュメントコメントに『ボスの行動を制御する』とあるため候補にしました」のように書きます

   参照経路の関係名は [ontology.md](ontology.md) の読み方で日本語に直して説明します(例: `Enemy.cs <-[inherits]- Boss.cs <-[uses_script]- Boss.prefab` →「Boss.cs が Enemy.cs を継承し、Boss.prefab が Boss.cs をコンポーネントとして使っている」)。根拠は 1 件につき最短の 1 経路だけなので、「ほかの経路もありうる」点は必要に応じて補足します。

## 翻訳例

| 質問 | 呼び出し |
|---|---|
| `Enemy.cs` を継承したクラス(孫クラスも)は? | `traverse(start: "Enemy.cs", steps: [{direction: "in", kinds: ["inherits"], max_hops: 10}])` |
| `Enemy.cs` またはそのサブクラスが付いたプレハブは? | `traverse(start: "Enemy.cs", steps: [{direction: "in", kinds: ["inherits"], max_hops: 10, include_self: true}, {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}])` |
| `Hero.prefab` が置かれているシーンは?(ネストしたプレハブ経由も) | `traverse(start: "Hero.prefab", steps: [{direction: "in", kinds: ["prefab_instance", "source_prefab"], types: ["Scene"], max_hops: 10}])` |
| `Fog.mat` を(何を経由してでも)使っているシーンは? | `traverse(start: "Fog.mat", steps: [{direction: "in", types: ["Scene"], max_hops: 6}])` |
| `Base.prefab` のバリアント(バリアントのバリアントも)は? | `traverse(start: "Base.prefab", steps: [{direction: "in", kinds: ["source_prefab"], types: ["Prefab"], max_hops: 10}])` |
| `Main.unity` で使われているシェーダーは? | `traverse(start: "Main.unity", steps: [{direction: "out", types: ["Shader", "ShaderGraph"], max_hops: 4}])` |
| ビルドに含まれるシーンは? | `traverse(start_type: "ProjectSettings", steps: [{direction: "out", kinds: ["build_settings"], types: ["Scene"]}])` |
| ビルドに含まれるシーンが使っているスクリプトは? | 上の 1 ステップ目に `{direction: "out", types: ["Script"], max_hops: 6}` を続ける |
| どこからも使われていないプレハブは? | `search_nodes(type: "Prefab", source: "Assets", unreferenced: true, limit: 100)` |
| コードから `Resources.Load` / Addressables で読まれているアセットは? | `traverse(start_type: "Script", steps: [{direction: "out", kinds: ["resources_load", "addressables_load"]}])` |
| ボスの行動に関係するスクリプトが付いたプレハブは? | `search_keywords(keywords: ["ボス", "boss"], type: "Script")` で候補スクリプトを選び、それぞれ `traverse(start: <候補>, steps: [{direction: "in", kinds: ["inherits"], max_hops: 10, include_self: true}, {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}])` |
| UI フォルダーのプレハブが使っているスクリプトは? | `list_folders` で UI のフォルダー名を確かめ、`traverse(start_type: "Prefab", start_path_contains: ["/UI/"], steps: [{direction: "out", kinds: ["uses_script"]}])` |
| `Enemy.cs` のサブクラスのうち、`Enemies` フォルダーにあるものは? | `traverse(start: "Enemy.cs", steps: [{direction: "in", kinds: ["inherits"], max_hops: 10, path_contains: ["/Enemies/"]}])` |
| 「ダメージ」を扱うコードはどれ? | `search_keywords(keywords: ["ダメージ", "damage"], type: "Script")`。`clues` の `method: …TakeDamage` や `doc of …` で、どのメンバーが該当するかまで答えます |
| `PlayerInspector.cs` が対象にしているスクリプトと、それを使うプレハブは? | `traverse(start: "PlayerInspector.cs", steps: [{direction: "out", kinds: ["custom_editor"]}, {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}])` |

### 否定・差集合を含む質問

「A だが B ではないもの」は、A の集合と B の集合をそれぞれ `traverse` / `search_nodes` で求め、差を取って答えます。例: 「シーンに一度も置かれていないプレハブ」= 全プレハブ(`search_nodes(type: "Prefab", source: "Assets", limit: 100)`)から、`traverse(start_type: "Scene", steps: [{direction: "out", kinds: ["prefab_instance", "source_prefab"], types: ["Prefab"], max_hops: 10}])` の結果を引きます。件数が上限を超える場合は、その旨を伝えて範囲(フォルダなど)を絞ってもらいます。

## 名前と意味の手がかりの読み方

- **フォルダーの慣習**: `Editor/` 配下はエディター専用コード(ビルドに入りません)、`Resources/` 配下は名前で読み込まれるアセット(参照が無くても使われている可能性)、`Plugins/` / `ThirdParty/` / `Samples/` は外部由来、`Tests/` はテスト。結果にこれらが混じっていたら、そう添えます
- **命名の慣習**: `SM_`(スタティックメッシュ)、`M_` / `MAT_`(マテリアル)、`T_`(テクスチャ)、`_Variant`、`UI_` などの接頭辞・接尾辞は、そのプロジェクトの分類です。`list_folders` や `search_keywords` の結果に繰り返し現れる規則があれば、それを使って絞り込みます
- **型名とファイル名のずれ**: スクリプトのファイル名と、中で宣言している型名(`declares`)は違うことがあります。`inherits` などの参照経路はファイル単位で表示されるので、型名で答えるときは `declares` を確認します
- **ドキュメントコメントは意図の記述**: 「〜を管理する」「〜から呼ばれる」のような記述は、グラフに現れない関係(イベント、文字列による呼び出しなど)の手がかりにもなります。ただし古いコメントもありうるので、参照の根拠と食い違うときは参照を優先し、食い違いを報告します
- キーワードに当たったこと自体は**関係の証拠ではありません**。候補を見つけたら、できるだけ `traverse` で参照の根拠も確かめます

## 注意

- **型が付くのはアセット単位です。** 「Light コンポーネントを持つプレハブ」のような Unity 組み込みコンポーネント単位の条件は、MCP ツールでは扱えません(SQLite の `objects.class_id` を直接引く必要があります。[schema.md](https://github.com/murnana/unity-ariadne/blob/main/docs/manual/schema.md))
- **未使用(`unreferenced`)の判定はグラフに見えている参照だけが根拠です。** シーンのビルド登録・Addressables・`Resources.Load` のリテラル・コードの型参照は含まれますが、文字列を組み立てたロード、リフレクション、`#if !UNITY_EDITOR` の中のコード、バイナリのアセット(LightingData など)からの参照は見えません。「削除してよい」と断言せず、「グラフ上は参照が無い」と伝えます
- `corresponding_source`(プレハブ内部の対応元オブジェクト)はすべてのツールで除外されています
- `PackageCache` のアセットは、プロジェクトから参照されている範囲でしか載っていません。結果の `source` を見て、プロジェクト自身のアセット(`Assets`)と区別して伝えます
- `search_keywords` が `warning` で「古い ariadne で作った DB」と言ってきたら、型名とドキュメントコメントが入っていません。`update-graph` スキルで作り直すと使えるようになります(ファイル名・フォルダー名での検索はそのままでも動きます)
- ドキュメントコメントは C# の `///`(または `/** */`)の XML ドキュメントコメントだけが対象です。普通の `//` コメント、シーンやプレハブの中の文字列、`#if !UNITY_EDITOR` の中の宣言は検索できません
- 結果が実際と合わない・アセットが見つからないときは、グラフが古い可能性があります。`update-graph` スキルで更新してからやり直します
