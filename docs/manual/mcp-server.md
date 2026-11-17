# MCP サーバー

`serve` コマンドは、グラフ DB を [Model Context Protocol](https://modelcontextprotocol.io)(stdio トランスポート)で公開します。Claude Code などの MCP クライアントから、Unity プロジェクトの依存関係を自然言語ツール呼び出しで照会できます。

## セットアップ

事前に `analyze` でグラフ DB を作っておいてください([cli.md](cli.md))。

### Claude Code に登録する

```sh
claude mcp add unity-ariadne -- dotnet run --project <このリポジトリ>/src/Analyzer -- serve --db <graph.db の絶対パス>
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

## ツールリファレンス

### find_references — 直接の参照元(逆依存)

```
find_references(name)
```

指定アセットを**直接参照しているアセット**を、参照の種類(ref_kind)と参照数つきで列挙します。「このマテリアルを使っているのは誰?」に答えます。

返り値: `asset`(解決されたアセット情報)、`total_referrers`、`references[]`(`from` / `kind` / `ref_count` / `source`)。

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

### describe_ontology — 語彙(種類と関係)の一覧

```
describe_ontology()
```

グラフを、ノードの型(アセットの種類 = `asset_type`)とエッジの型(参照の種類 = `ref_kind`)を持つオントロジーとして見たときの語彙を返します。どちらも**そのプロジェクトに実在するもの**だけです。

返り値:

- `classes[]`: `type`(アセットの種類)、`count`、`by_source`(出どころ別の件数)
- `relations[]`: `kind`(ref_kind)、`edges`(アセット単位のエッジ数)、`ref_count`(生の参照数)、`signatures`(`"Prefab->Script: 120"` のような、その関係が結んでいる種類の組み合わせ。多い順に最大 12 件。残りがあれば `more_signatures` に件数)

`traverse` を組み立てる前に、型名・関係名と、その関係がどの種類どうしを結んでいるかを確かめるのに使います。

### traverse — 型付き経路クエリ

```
traverse(steps, start = null, start_type = null)
```

`start`(アセット 1 つ。名前の指定方法は他のツールと同じ)か `start_type`(その種類の全アセット)のどちらか一方から出発し、`steps` を順に適用します。最後のステップのあとに残ったアセットが結果です。

各ステップ:

| フィールド | 内容 |
|---|---|
| `direction`(必須) | `out` = 参照している先へ(依存先)、`in` = 参照してくる側へ(参照元)、`both` = 両方 |
| `kinds` | たどる ref_kind の配列。省略するとすべて |
| `types` | このステップの終点に許すアセットの種類の配列。省略すると任意。複数ホップの途中のアセットは種類を問いません |
| `max_hops` | 最大ホップ数(既定 1、1–10 にクランプ)。`inherits` のクラス階層や `prefab_instance` の入れ子のような推移的な関係に使います |
| `include_self` | ステップの出発点そのもの(0 ホップ)も、種類が合えば結果に残します(「そのクラスと全サブクラス」など) |

返り値: `start`、`start_count`(`start_type` のとき)、`total_matches`、`matches[]`(`name` / `type` / `source` / `path`)。`path` は出発点からの根拠の経路を 1 本だけ示します(幅優先なので、各ステップで最短のもの)。`A -[kind]-> B` は A が B を参照、`A <-[kind]- B` は B が A を参照、の意味です。

```text
traverse(start: "Enemy.cs", steps: [
  {direction: "in", kinds: ["inherits"], max_hops: 10, include_self: true},
  {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}])

→ Assets/Scripts/Enemy.cs <-[inherits]- Assets/Scripts/Boss.cs <-[uses_script]- Assets/Prefabs/Boss.prefab
```

- グラフに存在しない ref_kind やアセットの種類を指定すると、エラーにはせず `warnings` で知らせます(綴りの誤りのことが多いため、`describe_ontology` で確認してください)
- `direction` が不正なら `error: "invalid_direction"`、`start` と `start_type` が両方または両方とも無ければ `error: "invalid_start"`、ステップが 0 個または 9 個以上なら `error: "invalid_steps"` を返します
- ステップは「経路が存在するもの」を集めるだけです。「〜ではないもの」は、`search_nodes` などで求めた集合との差を呼び出し側で取ってください

Claude Code プラグインには、質問をこの型付き経路に翻訳する手順をまとめた `search-graph` スキルが入っています([claude-code-plugin.md](claude-code-plugin.md))。

### list_unresolved — 参照切れ GUID 一覧

```
list_unresolved()
```

未解決 GUID(参照はあるが実体のないアセット)を参照数の多い順に、参照元サンプルパスつきで返します。CLI の `report` と同内容です。

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
| 未使用のプレハブ候補を挙げたい | `search_nodes`(`type: "Prefab"`, `unreferenced: true`) |
| このクラスかそのサブクラスが付いたプレハブは? | `traverse`(`inherits` を `in` に 10 ホップ → `uses_script` を `in` に 1 ホップ、終点 `Prefab`) |
| このマテリアルが最終的に入っているシーンは? | `traverse`(`in` に数ホップ、終点 `Scene`) |
| このプロジェクトにどんな種類・関係があるか | `describe_ontology` |
| プロジェクトの参照切れを棚卸ししたい | `list_unresolved` |

[効果測定](../development/benchmark.md)では、逆依存を問うタスク(影響範囲調査・未使用アセット探し)でグラフが特に効く(トークン 79〜88% 削減)ことがわかっています。コードシンボルの検索は grep のほうが速く、グラフの価値は grep の効かない GUID 参照層にあります。

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
