# search

オントロジー検索です。開始点を選び、型付きの関係を順にたどって、根拠つきで結果を返します。CLI の `guidgraph search --query '<json>'` と同じ JSON・同じ結果です。

## 引数

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `from` | SearchStart | 必須 | 開始点 |
| `steps` | SearchStep[] | なし | 0〜8 個。省略すると開始点そのものを返す |
| `unreferenced` | bool | false | 参照されない結果だけにする |
| `limit` | int | 20 | 1〜200 に丸める |
| `relax` | bool | true | 結果が 0 件のとき、条件を緩めて再検索する |

### SearchStart

`asset` と、フィルター（`keywords` / `types` / `source` / `path_contains`）はどちらか一方だけを指定します。`match_all` はフィルター側の補助です。

| 名前 | 説明 |
|---|---|
| `asset` | 1 アセット（パス、一意のファイル名、GUID） |
| `keywords` | 概念キーワード。ファイル名、フォルダー名、C# の型名・メンバー名、XML doc コメントに部分一致。大文字小文字、全角/半角、ひらがな/カタカナを区別しない |
| `match_all` | true なら全キーワードに一致するものだけ。既定は一致数の多い順 |
| `types` / `source` / `path_contains` | 種別、source、パスに含まれる文字列で絞る |

### SearchStep

| 名前 | 説明 |
|---|---|
| `direction` | 必須。`out`（依存先）/ `in`（参照元）/ `both` |
| `kinds` | たどる参照種別。省略なら全種 |
| `types` | 終点の種別（途中のアセットは検査しない） |
| `max_hops` | 既定 1、最大 10 |
| `include_self` | 0 ホップ（開始アセット自身）も残す |
| `path_contains` | 終点のパスに含まれる文字列 |

## 動作

1. 開始点を決めます。キーワードがあれば一致数順に並べます。
2. steps を順に適用します。
3. 結果が 0 件で `relax: true` なら、次の順に累積して緩めます。緩めた内容は `relaxed` に記録します。
   - `match_all` を「いずれか一致」にする
   - 開始点の `path_contains` を外す
   - steps の `path_contains` を外す
   - steps の `kinds` を外す
   - `max_hops` を 5 まで上げる

## 出力

```json
{"start": {"count": 1, "assets": ["..."]}, "total_matches": 3,
 "matches": [{"name": "...", "type": "Prefab", "source": "Assets",
              "path": "Enemy.cs <-[uses_script]- Boss.prefab", "from": "Enemy.cs",
              "from_clues": ["..."], "from_doc": "...", "declares": ["..."], "notes": ["..."]}],
 "truncated": true,
 "relations": {"uses_script": "A -[uses_script]-> B: ..."},
 "relaxed": ["..."], "unsupported": ["TerrainData"], "warnings": ["..."]}
```

- `path`: `A -[kind]-> B` は A が B を参照、`A <-[kind]- B` は B が A を参照することを表します。
- steps がある場合、キーワードの根拠は `from_clues` / `from_doc`（開始アセットのもの）に入ります。steps が無い場合は `clues` / `doc`（結果自身のもの）に入ります。
- `role` は、入れ子の PrefabInstance を持ち、自身の持ち物が GameObject と Transform（RectTransform）だけの prefab にだけ `wrapper` と付きます（入れ子の prefab のための `stripped` は数えません）。付いていない Prefab は、それ以外（leaf）です。1 つの prefab だけを包むバリアントも `wrapper` になります。`leaf` を省くのは出力を小さくするためです。
- `declares` は C# の宣言型（最大 3 件）、`notes` は Editor/ や Resources/ などのフォルダー規約の注記です。
- `start.assets` は最大 10 件です。
- `unsupported` は、開始点または結果に、本体を読んでいない種類（`describe_ontology` の `parsed: false`）が含まれるときだけ付きます。その種類からたどれる参照（TerrainData の木など）は、グラフに無いことを示します。
- 答えが約 48,000 文字を超えるときは、`limit` を自動で小さくして収め、`truncated: true` と、件数を減らしたことを伝える `warnings` を付けます。呼び出し側のツール出力の上限を超えると、結果がファイルに退避されて読み直しが必要になるためです。
- `warnings` は、存在しない種別名・参照種別名、C# シンボルが無い古い db（再 analyze の案内）に対して出ます。

## エラー

`invalid_query`（`from` が無い）、`invalid_start`（`asset` とフィルターの両方、または両方なし）、`invalid_steps`（9 個以上）、`invalid_direction`、`not_found`、`ambiguous`。
