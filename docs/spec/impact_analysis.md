# impact_analysis

アセットを変更したときに影響を受けるアセットを、逆依存を推移的にたどって距離別に返します。

## 引数

| 名前 | 型 | 必須 | 既定 | 説明 |
|---|---|---|---|---|
| `name` | string | はい | | パス、ファイル名、GUID |
| `depth` | int | いいえ | 3 | たどるホップ数。1〜10 に丸める |

## 出力

```json
{"asset": {...}, "depth": 3, "total_impacted": 40,
 "impacted": [{"path": "Assets/A.prefab", "distance": 1, "via": "Assets/Target.mat"}],
 "truncated": true}
```

- `distance` 1 が直接の参照元です。
- `via` は、そのアセットに最初に到達した 1 ホップ手前のアセットです。
- `impacted` は最大 200 件です。循環があっても各アセットは最短距離で 1 回だけ現れます。

## エラー

`not_found` / `ambiguous`。
