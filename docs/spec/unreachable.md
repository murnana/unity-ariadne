# unreachable

ルート（既定はシーン）から参照をたどっても到達できないアセットを返します。「どのシーンからも、入れ子や間接参照を含めて使われていない prefab」のような、全件を横断する質問に 1 回で答えます。

## 目的

`search` で全 prefab を列挙すると、出力が呼び出し側のツール出力の上限（約 48,000 文字）に当たり、件数が切られて確認の往復が増えます。`unreferenced`（誰からも参照されない）では、「入れ子の先でだけ孤立している」ものを拾えません。到達集合を 1 回で求め、到達できなかった側だけを返します。

## 引数

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `types` | string[] | `["Prefab"]` | 調べるアセットの種別（`describe_ontology` の `classes[].type`） |
| `roots` | string[] | `["Scene"]` | 出発点にするアセットの種別。到達の判定はここから始める |
| `source` | string | `Assets` | 調べるアセットとルートの置き場（`Assets` / `Packages` / `PackageCache`）。ルートから先は、どの置き場のアセットもたどる |
| `build_only` | bool | false | true なら、ルートのうちビルド設定（EditorBuildSettings）に載っているシーンだけを使う。`roots` が `Scene` のときだけ意味を持つ |
| `kinds` | string[] | 省略 | たどる参照の種類を絞る。省略時は `corresponding_source` 以外のすべて |
| `path_contains` | string[] | 省略 | 調べるアセットを、パスにこの語を含むものに絞る |
| `limit` | int | 50 | 返す件数。1〜200 |

## 出力

```json
{"types": ["Prefab"], "roots": {"types": ["Scene"], "count": 3},
 "candidates": 141, "reachable": 136, "unreachable": 5,
 "matches": [
   {"name": "Assets/Art/Prefabs/Compositions/Terrain_Rivers.prefab", "type": "Prefab",
    "referrers": 0},
   {"name": "Assets/Art/Meshes/Scatter/Grass_A/Grass_A_A.prefab", "type": "Prefab",
    "referrers": 1, "referrer_samples": ["Assets/Art/Prefabs/Foo.prefab"],
    "notes": ["Resources フォルダ: 名前で読み込まれうる"]}],
 "truncated": true, "warnings": ["..."], "notes": ["..."], "meta": {...}}
```

- `build_only` が効いたときは `roots.build_only: true` を付け、`notes` に「無効なビルド設定のシーンも含む」ことを足します。シーンのルートが無いのに指定したときは `warnings` で無視を伝えます。
- `candidates` は調べる対象の数、`reachable` は到達できた数、`unreachable` は到達できなかった数です（`candidates = reachable + unreachable`）。
- `matches` は到達できなかったアセットで、パスの順です。`limit` を超えると `truncated: true`。
- `referrers`: そのアセットを直接参照しているアセットの数（自分自身は数えず、`kinds` を指定したときはその種類だけを数えます）。**0 なら誰からも参照されていません。1 以上なら、参照元もまたルートから到達できません**（そうでなければ到達できているため）。`referrer_samples` はその参照元の例（最大 3 件、`referrers` が 1 以上のときだけ）。
- `notes`（各件）: `search` と同じパス由来の注記（`Resources`、`Editor`、`StreamingAssets`、PackageCache など）。**`Resources` 配下のものは、参照が無くても名前で読み込まれうる**ため、そのまま削除してよいとは言えません。
- トップレベルの `notes`: 次の限界を常に付けます。

## 限界

- 静的に追えない読み込みは検出できません: `Resources.Load` の変数パス、Addressables の動的な key、文字列でのシーン・アセット名、リフレクション、ビルドに入らない Editor 専用の参照。
- `roots` が Scene のとき、既定ではビルド設定に載っていないシーン（テスト用など）もルートに含めます。「ビルドに入るシーンから」なら `build_only: true` を使います。
- 本体がバイナリのアセット（N-9）の中の参照は、グラフに無いため、そこを経由した到達は検出できません。`unsupported` に該当する種別が `types` / `roots` に含まれるときは、`unsupported` を付けます。
- 結果が 0 件でも、動的な読み込みで到達しているアセットを見落としているわけではない、とは言えません（限界は上のとおり）。

## エラーと警告

- ルートが 0 件のとき: `{error: "no_roots", roots, hint, warnings?}`。
- 出力が約 48,000 文字を超えるときは件数を減らし、`warnings` と `truncated` で伝えます。
- 存在しない種別名 / 参照種別名: `warnings` に `describe_ontology` を案内する文（`search` と同じ）。
- `not_found` などアセット指定のエラーは、このツールには出ません（アセットを指定する引数が無いため）。

## 実装

- `src/Graph/Unreachable.cs`。新しいテーブルはありません。ルートの GUID 集合から、`asset_edges` の `ref_kind <> 'corresponding_source'` の辺で幅優先に到達集合を 1 回で求めます（段数の上限なし）。
- `notes` は `search` と同じ `OntologyGlossary.PathNotes` です。

## 効果と互換性

- **効果**: AQ-01（[test-cases/agent-questions.md](../test-cases/agent-questions.md)）の期待する答え（141 / 136 / 5 件）が、サンプルプロジェクトで 1 回の呼び出し（約 830 文字）で返ります。
- **互換性**: ツールの追加だけです。ツール定義が 1 つ増え、固定コストが約 1,400 文字増えます。
