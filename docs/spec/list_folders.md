# list_folders

フォルダーごとに、配下（再帰）のアセット数と種別の内訳を返します。プロジェクトの構成の手がかりに使います。

## 引数

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `path` | string | `Assets` | 起点のフォルダー。空なら最上位 |
| `depth` | int | 1 | 下りる階層数。1〜4 に丸める |

## 出力

```json
{"path": "Assets", "total_folders": 8,
 "folders": [{"path": "Assets/Characters", "assets": 120, "types": {"Prefab": 60, "Material": 30}}],
 "truncated": true}
```

- `types` は件数の多い順（同数は名前順）で最大 6 種です。
- `folders` は最大 200 件です。存在しない `path` は `total_folders: 0` で、エラーにはなりません。
