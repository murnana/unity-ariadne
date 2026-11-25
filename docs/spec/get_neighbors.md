# get_neighbors

アセットから N ホップ以内の近傍を、両方向（依存先と参照元）と、それらの間の辺ごと返します。

## 引数

| 名前 | 型 | 必須 | 既定 | 説明 |
|---|---|---|---|---|
| `name` | string | はい | | パス、ファイル名、GUID |
| `hops` | int | いいえ | 1 | 半径。1〜3 に丸める |

## 出力

```json
{"asset": {...}, "hops": 1, "total_assets": 5, "total_edges": 6,
 "assets": [{"name": "...", "type": "Material", "source": "Assets"}],
 "edges": [{"from": "...", "to": "...", "kind": "generic", "ref_count": 1}],
 "truncated": true}
```

`assets` と `edges` はそれぞれ最大 200 件で、どちらかが超えると `truncated` が付きます。孤立アセットは自分 1 件だけで、辺は 0 件です。

## エラー

`not_found` / `ambiguous`。
