# MCP サーバー

`serve` コマンドは、グラフ DB を [Model Context Protocol](https://modelcontextprotocol.io)(stdio トランスポート)で公開します。Claude Code などの MCP クライアントから、Unity プロジェクトの依存関係を自然言語ツール呼び出しで照会できます。

## セットアップ

事前に `analyze` でグラフ DB を作っておいてください([cli.md](cli.md))。

### Claude Code に登録する

```sh
claude mcp add guidgraph -- dotnet run --project <このリポジトリ>/src/Analyzer -- serve --db <graph.db の絶対パス>
```

登録後、Claude Code のセッションで `find_references` などのツールが使えるようになります。リポジトリのチェックアウトから始める通し手順(SDK 準備、パスの注意、トラブルシューティング含む)は [claude-code-setup.md](claude-code-setup.md) をご覧ください。

### 動作の要点

- **ツール呼び出しごとに DB を読み取り専用で開き直します。** `analyze` でグラフを更新すれば、サーバーの再起動なしで次のツール呼び出しから反映されます。
- ログはすべて stderr に出ます(stdout は MCP トランスポート専用です)。
- 起動時に DB パスを検証するため、パスが間違っていれば即座にエラー終了します。

## アセット名の指定方法(全ツール共通)

`name` を取るツールは、次の 3 段階で解決します(最初にヒットした段階を採用します):

1. **GUID 完全一致**(32 桁 16 進)
2. **プロジェクト相対パス完全一致**(例: `Assets/Materials/Fog.mat`)
3. **ファイル名のサフィックス一致**(大文字小文字無視、パス区切り境界で一致。例: `Fog.mat`)

段階 3 で複数ヒットした場合は `error: "ambiguous"` と候補パス(最大 10 件)が返るため、候補のフルパスで呼び直してください。どれにも一致しなければ `error: "not_found"` が返ります — その場合は `search_nodes` で部分一致検索してください。

## グラフの鮮度(`meta`)

各ツールの応答には、グラフの新しさを示す `meta` が付きます。

```json
{"meta": {"analyzed_at": "2026-11-23T09:00:00Z", "stale": true, "stale_count": 3}}
```

- `analyzed_at`: DB ファイルの最終更新時刻(UTC)。`analyze` が書き終えた時刻にあたります。
- `stale`: `Assets/` 内の `.meta` / `.unity` / `.prefab` のうち、`analyzed_at` より後に更新されたものがあるか。`stale_count` はその数です(上限 1000)。`stale` は、サーバーを `--project <パス>` つきで起動したときだけ付きます(プラグイン経由なら自動で付きます)。
- ファイルの更新時刻だけで判定します(Git などは見ません)。削除されたファイルは検出しません。DB ファイルをコピーや共有すると、更新時刻が変わって正しく判定できません。
- `stale` が true のときは、`analyze` を再実行すると最新になります。

## ツールリファレンス

### find_references — 直接の参照元(逆依存)

```
find_references(name, summary = false, top = 5)
```

指定アセットを**直接参照しているアセット**を、参照の種類(ref_kind)と参照数つきで列挙します。「このマテリアルを使っているのは誰?」に答えます。

返り値: `asset`(解決されたアセット情報)、`total_referrers`、`references[]`(`from` / `kind` / `ref_count` / `source`)。

`summary = true` を指定すると、個別の行(`references`)の代わりに集計を返します。参照元が多いアセットで出力が小さくなり、「誰が・どの種類で参照しているか」を 1 回で把握できます。`top` は種類ごとに挙げる参照元の数です(1〜20、既定 5)。

- `by_kind[]`: 参照の種類ごとに `kind` / `referrers`(参照元の数) / `refs`(参照の総数) / `top`(参照元のパス)。`referrers` の多い順です。
- `by_type[]`: 参照元のアセット種別ごとの `type` / `referrers`。

### impact_analysis — 影響範囲(逆依存の推移閉包)

```
impact_analysis(name, depth = 3)
```

逆依存を推移的にたどり、**このアセットを変更したら影響が及ぶ範囲**を距離別に返します。`distance: 1` が直接の参照元です。各結果の `via` は「1 ホップ手前のどのアセット経由で最初に到達したか」を示します。

`depth` は 1–10 にクランプされます。

### get_neighbors — 近傍サブグラフ(双方向)

```
get_neighbors(name, hops = 1)
```

指定アセットから**双方向に N ホップ以内**の全アセットと、それらの間の全エッジを返します。hops=1 で「直接の依存先(outgoing)と参照元(incoming)」が 1 枚の絵になります。

`hops` は 1–3 にクランプされます。返り値: `assets[]`(name / type / source)+ `edges[]`(from / to / kind / ref_count)。

### search_nodes — パス・種類での検索

```
search_nodes(query = "", type = null, source = null, unreferenced = false, limit = 20)
```

アセットパスの大文字小文字無視の部分一致検索です(GUID 完全一致も可能です)。**短いパス順**に返すため、目的のアセットが上位に来やすくなっています。名前が曖昧・不明なときに最初に呼ぶツールです。

- `type` を付けると、そのアセットの種類(`Prefab`、`Script` など。大文字小文字無視)だけに絞ります。`query` を空にすると、その種類の一覧になります
- `source` を付けると、出どころ(`Assets` / `Packages` / `PackageCache` / `ProjectSettings` / `BuiltIn` / `Unresolved`)で絞ります
- `unreferenced: true` で、**どのアセットからも参照されていない**ものだけを返します(`corresponding_source` は参照に数えません)。未使用アセットの候補探しに使います。グラフに見えない参照(文字列を組み立てたロードなど)は考慮できない点にご注意ください([limitations.md](limitations.md))

`limit` は 1–100 にクランプされます。`_` や `%` は検索文字列としてそのまま扱われます(LIKE ワイルドカードとして解釈されません)。

### search — オントロジー検索(種類・関係・名前・意味)

```
search(from, steps = null, unreferenced = false, limit = 20, relax = true)
```

グラフを、ノードの型(アセットの種類)とエッジの型(参照の種類)を持つオントロジーとして検索します。**種類・関係の連鎖・場所・概念を組み合わせた質問は、このツール 1 回で答えられるように作ってあります。** 同じ機能を CLI の `guidgraph search` でも使えます([cli.md](cli.md))。

処理の流れ:

1. **出発点を決める**(`from`)。特定のアセット 1 つ(`asset`)か、条件で絞ったアセット群です。条件で絞る場合、`keywords` があればキーワードの当たり具合で並べ、関連の強い順に出発点にします
2. **`steps` を順にたどる**。各ステップは、指定の関係を指定の向きに最大 `max_hops` 段たどり、終点の種類・パスで絞ります。`steps` を省略すると出発点そのものが結果です
3. **0 件なら条件を緩める**(`relax: false` で無効)。出発点は「全キーワード一致 → いずれか一致」「`path_contains` を外す」、ステップは「`path_contains` を外す → 関係の種類を問わない → `max_hops` を 5 に上げる」の順に、見つかるまで累積で緩め、`relaxed` に記録します
4. `unreferenced: true` なら、**どこからも参照されていない**ものだけ残します
5. 結果ごとに**根拠と注記**を付けます

`from` のフィールド(`asset` と、それ以外の条件はどちらか一方):

| フィールド | 内容 |
|---|---|
| `asset` | アセット 1 つ(名前の指定方法は他のツールと同じ) |
| `keywords` | 概念のキーワード(大文字小文字無視の部分一致。空白で分割)。下の表の手がかりと照合します |
| `match_all` | 全キーワードに当たったものだけ(既定は 1 つでも当たれば可。当たった数の多い順) |
| `types` | アセットの種類の配列(例: `["Script"]`) |
| `source` | 出どころ(`Assets` / `Packages` / `PackageCache` / `ProjectSettings` / `BuiltIn`) |
| `path_contains` | パスにいずれかを含むものだけ(フォルダー名・ファイル名の手がかり。例: `["/UI/"]`) |

キーワードの手がかりと重み(キーワードごとに最も強い 1 つを数えます):

| 手がかり | 重み | 表記 |
|---|---|---|
| ファイル名 | 4 | `enemy → file name: Enemy.cs` |
| C# で宣言された型の名前 | 3 | `boss → class: Game.Boss` |
| パス上のフォルダー名 | 2 | `enemies → folder: Enemies` |
| XML ドキュメントコメント付きメンバーの名前 | 2 | `damage → method: Game.Enemy.TakeDamage` |
| XML ドキュメントコメントの本文(`<summary>` / `<remarks>`) | 1 | `怒り → doc of Game.Boss.Enrage` |

照合の前に、キーワードと照合先の両方の表記を揃えます。

- **全角・半角**(Unicode の NFKC 正規化): `ＢＯＳＳ` = `BOSS`、`ﾎﾞｽ` = `ボス`、`①` = `1`
- **大文字・小文字**
- **ひらがな・カタカナ**: `ぼす` = `ボス`、`きゃらくたー` = `キャラクター`
- **英語の複数形 `-ies`**: 単数形の `-y` でも当たります(`enemy` → `Enemies`)

日本語のファイル名・フォルダー名・コメントもそのまま検索できます。ただし形態素解析はしないため、活用の違い(`攻撃した` と `攻撃する`)や同義語(`敵` と `エネミー`)は吸収しません。語幹(`攻撃`)を使うか、複数のキーワードを渡してください。`path_contains` も同じ規則で照合します。型名とドキュメントコメントは `analyze` が `code_symbols` テーブルに記録したものです([schema.md](schema.md))。

`steps` の各要素:

| フィールド | 内容 |
|---|---|
| `direction`(必須) | `out` = 参照している先へ(依存先)、`in` = 参照してくる側へ(参照元)、`both` = 両方 |
| `kinds` | たどる ref_kind の配列。省略するとすべて |
| `types` | このステップの終点に許すアセットの種類の配列。複数ホップの途中は問いません |
| `max_hops` | 最大ホップ数(既定 1、1–10)。`inherits` のクラス階層や `prefab_instance` の入れ子に使います |
| `include_self` | ステップの出発点そのもの(0 ホップ)も、条件に合えば残します(「そのクラスと全サブクラス」) |
| `path_contains` | このステップの終点を、パスにいずれかを含むものに絞ります |

返り値:

| フィールド | 内容 |
|---|---|
| `start` | 出発点の件数と、関連の強い順に最大 10 件 |
| `total_matches` / `matches[]` / `truncated` | 結果(最大 `limit` 件)。`steps` がある場合は、関連の強い出発点から近い順 |
| `matches[].path` | 根拠の参照経路(1 本、各ステップで最短)。`A -[kind]-> B` は A が B を参照、`A <-[kind]- B` は B が A を参照 |
| `matches[].from` / `from_clues` / `from_doc` | 経路の出発点と、その出発点がキーワードに当たった手がかり・ドキュメントコメントの抜粋 |
| `matches[].clues` / `doc` | `steps` が無いとき、結果そのものがキーワードに当たった手がかりと抜粋 |
| `matches[].role` | `wrapper` だけが付く。他の prefab を入れ子に置くだけで、自身のコンポーネントを持たない prefab(「木」のような概念検索で、置き場のプレハブと中身を数え分けるために使います)。付いていない Prefab はそれ以外です |
| `unsupported` | 開始点か結果に、本体を読んでいない種類(上の `parsed: false`)が含まれるときだけ付く、その種類の一覧。「グラフに出ない」ことを示す(例: TerrainData の木は出ません) |
| `matches[].declares` | スクリプトが宣言している型(最大 3 件) |
| `matches[].notes` | パスから分かる注記: `Editor` フォルダー(ビルドに入らない)、`Resources` フォルダー(参照が無くても名前で読み込まれうる)、`StreamingAssets`、`Plugins`、サンプル、テスト、PackageCache、ビルトイン、未解決 GUID |
| `relations` | 経路に出てきた関係それぞれの意味(`A -[uses_script]-> B: A has a component ... whose script is B` など) |
| `relaxed` | 0 件だったために緩めた条件(順番どおり) |
| `warnings` | グラフに存在しない種類名・関係名(綴りの誤りなど)、古い DB で型名・コメントが検索できなかったこと |

```text
search(from: {keywords: ["ボス", "boss"], types: ["Script"]},
       steps: [{direction: "in", kinds: ["inherits"], max_hops: 10, include_self: true},
               {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}])

→ Assets/Prefabs/Enemies/Boss.prefab
    path:       Assets/Scripts/Boss.cs <-[uses_script]- Assets/Prefabs/Enemies/Boss.prefab
    from_clues: ["ボス → doc of Game.Boss", "boss → file name: Boss.cs"]
    from_doc:   "Game.Boss: ボスの行動を制御する"
```

説明文(`relations`、`notes`、`clues` の見出し、`relaxed`、`warnings`、エラーの `hint`)は英語です。アセット名・キーワード・手がかりの対象・ドキュメントコメントの抜粋は、日本語・韓国語・中国語などの文字のまま、エスケープも変換もせずに返します。

エラー: `from` が無い(`invalid_query`)、`asset` と条件の両方または両方とも無い(`invalid_start`)、`direction` が不正(`invalid_direction`)、ステップが 9 個以上(`invalid_steps`)、`asset` が見つからない(`not_found`)、複数に当たる(`ambiguous`、`candidates` 付き)。いずれも `hint` が付きます。

否定(「A だが B ではないもの」)は、A と B をそれぞれ検索して差を取ってください。Unity 組み込みコンポーネント単位の条件(「Light を持つプレハブ」)は扱えません([schema.md](schema.md) の `objects.class_id` を直接引いてください)。

### list_folders — フォルダー構成の一覧

```
list_folders(path = "Assets", depth = 1)
```

`path` の下のフォルダーを `depth` 階層(1–4)まで列挙し、それぞれに含まれる(サブフォルダーを含む)アセット数と、多い種類の上位 6 件を返します。`path` を空にすると最上位(`Assets` / `Packages` / `ProjectSettings` …)です。フォルダー名からプロジェクトの整理の仕方(`Characters/Enemies`、`UI`、`Levels` …)を読み取り、`search` の `path_contains` に使うための道具です。

### summarize_project — プロジェクトの概要

```
summarize_project(max_chars = 1500, scene_limit = 20, folder_depth = 1, readme_chars = 200)
```

プロジェクトの全体像を 1 回で返します。「このプロジェクトは何でできているか」を最初に知りたいときに使います。

- `scenes`: `Assets/` 内のシーン。`scene_limit` 件まで(超えると `scenes_truncated`)。
- `build_order`: ビルド設定(EditorBuildSettings)のシーンの順序。有効・無効の区別は保存していないため、無効なシーンも含みます。
- `folders`: `Assets/` の `folder_depth` 階層(1–3)までのフォルダーごとのアセット数と、多い種類の上位 4 件。
- `scripts`: `assets`(`Assets/` 内のスクリプト数)と `packages`(パッケージ内の数)。
- `readme`: プロジェクト直下の README の先頭 `readme_chars` 文字(0 で省略)。**`serve --project` を指定したときだけ**付きます(プラグイン経由なら自動です)。

出力が `max_chars` を超えるときは、シーン、フォルダー、README の順に切り詰め、切り詰めた項目に `*_truncated: true` を付けます。

### expanded_count — 展開後の数

```
expanded_count(asset, scene, max_depth = 10)
```

`scene`(シーンまたは prefab)の中で、`asset` を直接使っているオブジェクトが、入れ子の prefab を展開すると何個になるかを返します。「このマテリアルはシーンにいくつあるか」「この prefab はいくつ置かれているか」に、YAML を読まずに答えます。`asset` は prefab に限らず、マテリアル、メッシュ、スクリプトなど何でも構いません。

- `expanded_count` = `direct`(`scene` が直接使っている数) + `via_nested`(入れ子の prefab の中の使用数 × その prefab が置かれている数) − `removed`(インスタンスが取り除いた分)。
- `max_depth`(1〜50)は入れ子をたどる深さの上限です。打ち切ったときは `depth_limited: true`。
- `unresolved_removals`: 取り除かれた対象を特定できなかった数。入れ子の prefab やモデルの中のオブジェクトは Unity が派生させた ID で記録されるため、グラフでは追えません。この数が大きいと、`expanded_count` は取り除かれた分だけ多めになっている可能性があります。
- `m_Modifications` による差し替え(あるアセットを別のものに付け替える)は、新しいアセットを PrefabInstance ごとに 1 回だけ数え、元のアセットからは引きません。
- バイナリ本体を読んでいない種類(Texture / Model など)は `unsupported` に載ります。

### unreachable — シーンから到達できないアセット

```
unreachable(types = ["Prefab"], roots = ["Scene"], source = "Assets", build_only = false,
            kinds, path_contains, limit = 50)
```

`roots` のアセットから参照を(入れ子や間接参照も含めて)たどっても届かない `types` のアセットを返します。「どのシーンからも使われていない prefab は?」のように、全件を調べる質問に 1 回で答えます。

- `candidates`(調べた数) = `reachable`(届いた数) + `unreachable`(届かなかった数)。`matches` はパス順で、`limit`(1〜200)を超えると `truncated: true` です。
- 各件の `referrers` は直接参照しているアセットの数です。**0 なら参照元がありません。1 以上でも、その参照元はどれもルートから届きません**。例は `referrer_samples`(最大 3 件)。
- 各件の `notes` は `search` と同じフォルダー由来の注記です。`Resources` 配下のものは、参照が無くても名前で読み込まれえます。
- `source` は調べるアセットとルートの置き場です。ルートから先は、どの置き場のアセットもたどります。
- `build_only: true` は、シーンのルートをビルド設定(EditorBuildSettings)に載っているものに絞ります。有効・無効の区別は保存していないため、無効なシーンも含みます。
- `kinds` を指定すると、その種類の参照だけをたどります(`referrers` も同じ範囲で数えます)。
- ルートが 0 件のときは `error: "no_roots"` です。
- **静的な参照しか見ません。** `Resources.Load` の変数パス、Addressables の動的な key、文字列でのシーン名・アセット名、リフレクションによる読み込みは検出できないため、結果に載ったことは未使用の証明になりません。

### describe_ontology — 語彙(種類と関係)の一覧

```
describe_ontology()
```

グラフの語彙を返します。種類と関係の件数・組み合わせは**そのプロジェクトに実在するもの**、意味は guidgraph が持つ定義です。

- `classes[]`: `type`(アセットの種類)、`category`(Placement / Code / Data / Rendering / Geometry / Animation / Effects / Audio/Video / UI / Physics/Lighting / Settings / Folder / Unresolved / Other)、`parsed`(本体がバイナリで読んでいない種類にだけ `false`。Texture / Model / SpeedTree / AudioClip / VideoClip / Font / TerrainData / Assembly)、`count`、`by_source`
- `relations[]`: `kind`(ref_kind)、`layer`(`asset` / `code`)、`out`(`A -[kind]-> B` の読み方)、`in`(逆にたどると何が見つかるか)、`transitive`(`max_hops` で何段もたどる意味があるか)、`edges`、`ref_count`、`signatures`(`"Prefab->Script: 120"` のような、実際に結んでいる種類の組み合わせ。多い順に最大 12 件、残りは `more_signatures`)

`search` の `types` / `kinds` に使う名前と、関係の向きを確かめるのに使います。

### list_unresolved — 参照切れ GUID 一覧

```
list_unresolved(limit = 20)
```

未解決 GUID(参照はあるが実体のないアセット)を参照数の多い順に、参照元サンプルパスつきで返します。CLI の `report` と同内容に加えて、どのパッケージにありそうかの推定 `hint`(`package_candidates` と、推定の根拠 `basis`)を付けます。`hint` は参照元のパスと、参照元が使っているパッケージからの推定で、確実ではありません。推定できないものには付きません。

`limit`(1〜200、既定 20)は列挙する件数の上限です。`total_unresolved` は常に全件数で、超えた分は `truncated: true` になります。以前は最大 200 件を返していたので、全件が必要なときは `limit = 200` を指定してください。

## 共通の出力仕様

- 返り値はコンパクトな snake_case JSON 文字列です。非 ASCII のアセットパスもエスケープせずそのまま入ります。
- **リストは最大 200 件で打ち切られ**、打ち切り時は `truncated: true` が付きます(ハブアセットで呼び出し側のコンテキストが溢れないための上限です)。
- `corresponding_source` エッジ(prefab 内部の対応元オブジェクト参照)は、jsonl エクスポートと同じくクエリ結果から除外されています。生データが必要な場合は SQLite を直接引いてください([schema.md](schema.md))。
- 未解決 GUID がアセットとして返るときは `dangling: true` が付きます。

## 活用パターン

| 知りたいこと | ツール |
|---|---|
| このアセット、消しても大丈夫? | `find_references` → 0 件なら未参照 |
| このシェーダーを変えたら何が壊れうる? | `impact_analysis` |
| このプレハブの依存関係を俯瞰したい | `get_neighbors` (hops=1〜2) |
| 正確なパスがわからない | `search_nodes` |
| 未使用のプレハブ候補を挙げたい | `search`(`from: {types: ["Prefab"], source: "Assets"}`, `unreferenced: true`)。`notes` の Resources 注記に注意 |
| このクラスかそのサブクラスが付いたプレハブは? | `search`(`inherits` を `in` に 10 ホップ → `uses_script` を `in` に 1 ホップ、終点 `Prefab`) |
| このマテリアルが最終的に入っているシーンは? | `search`(`in` に数ホップ、終点 `Scene`) |
| このプロジェクトにどんな種類・関係があるか | `describe_ontology` |
| 「ボス」に関係するスクリプトやアセットは? | `search`(`from: {keywords: ["boss", "ボス"]}`) |
| このプロジェクトは何でできている?(最初の 1 回) | `summarize_project` |
| このアセットはシーンにいくつ展開される? | `expanded_count` |
| どのシーンからも(間接的にも)使われていない prefab は? | `unreachable` |
| このプロジェクトのフォルダー構成は? | `list_folders` |
| UI フォルダーのプレハブが使っているスクリプトは? | `search`(`from: {types: ["Prefab"], path_contains: ["/UI/"]}` → `uses_script` を `out`) |
| プロジェクトの参照切れを棚卸ししたい | `list_unresolved` |

[効果測定](../development/benchmark.md)では、逆依存を問うタスク(影響範囲調査・未使用アセット探し)でグラフが特に効く(トークン 79〜88% 削減)ことがわかっています。コードシンボルの検索は grep のほうが速く、グラフの価値は grep の効かない GUID 参照層にあります。

---

> GuidGraph は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> GuidGraph is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
