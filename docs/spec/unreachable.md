# unreachable（提案）

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

- `candidates` は調べる対象の数、`reachable` は到達できた数、`unreachable` は到達できなかった数です（`candidates = reachable + unreachable`）。
- `matches` は到達できなかったアセットで、パスの順です。`limit` を超えると `truncated: true`。
- `referrers`: そのアセットを直接参照しているアセットの数。**0 なら誰からも参照されていません。1 以上なら、参照元もまたルートから到達できません**（そうでなければ到達できているため）。`referrer_samples` はその参照元の例（最大 3 件、`referrers` が 1 以上のときだけ）。
- `notes`（各件）: `search` と同じパス由来の注記（`Resources`、`Editor`、`StreamingAssets`、PackageCache など）。**`Resources` 配下のものは、参照が無くても名前で読み込まれうる**ため、そのまま削除してよいとは言えません。
- トップレベルの `notes`: 次の限界を常に付けます。

## 限界

- 静的に追えない読み込みは検出できません: `Resources.Load` の変数パス、Addressables の動的な key、文字列でのシーン・アセット名、リフレクション、ビルドに入らない Editor 専用の参照。
- `roots` が Scene のとき、既定ではビルド設定に載っていないシーン（テスト用など）もルートに含めます。「ビルドに入るシーンから」なら `build_only: true` を使います。
- 本体がバイナリのアセット（N-9）の中の参照は、グラフに無いため、そこを経由した到達は検出できません。`unsupported` に該当する種別が `types` / `roots` に含まれるときは、`unsupported` を付けます。
- 結果が 0 件でも、動的な読み込みで到達しているアセットを見落としているわけではない、とは言えません（限界は上のとおり）。

## エラーと警告

- ルートが 0 件のとき: `{error: "no_roots", roots, hint}`。
- 存在しない種別名 / 参照種別名: `warnings` に `describe_ontology` を案内する文（`search` と同じ）。
- `not_found` などアセット指定のエラーは、このツールには出ません（アセットを指定する引数が無いため）。

## 実装の方針

- 新しいテーブルは要りません。ルートの GUID 集合から、`GraphQueries.Traverse` と同じ辺（`asset_edges` の `ref_kind <> 'corresponding_source'`）で幅優先に到達集合を 1 回で求めます。
- 結果の整形（`notes`、`referrers`）は `search` の整形を共有します。出力は約 48,000 文字の上限内に収まる件数（50 件 × 約 150 文字）です。
- 参照元の判定（`referrers`）は、到達の計算とは別に、対象ごとに `asset_edges` の入りの辺を数えます。

## 効果と互換性

- **効果**: AQ-01（[test-cases/agent-questions.md](../test-cases/agent-questions.md)）のような質問が、`search` の全件列挙と読み直しを使わず、1 回の呼び出しで済む見込みです。実測はこれからです。
- **互換性**: ツールの追加だけです。ツール定義が 1 つ増え、固定コストが約 700 文字増えます。

## 決めたいこと

1. `build_only` を入れるか（案: 入れる。既定は false）。
2. `roots` の既定を、Scene のほかにビルド設定のシーンだけにするか（案: 全シーン。Q1 の期待する答えは全シーンを使った値）。
3. `limit` の既定（案: 50）。
