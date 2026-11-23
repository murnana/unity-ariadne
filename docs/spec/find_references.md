# find_references

アセットを直接参照するアセット（逆依存）を、参照種別と回数つきで返します。

## 引数

| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| `name` | string | はい | パス、一意のファイル名、または GUID |

## 出力

```json
{"asset": {...}, "total_referrers": 12,
 "references": [{"from": "Assets/A.prefab", "kind": "uses_script", "ref_count": 2, "source": "Assets"}],
 "truncated": true}
```

- `references` は最大 200 件。`from` は参照元のパス、`ref_count` は同じ種別の参照の数です。
- 同じ参照元でも種別が違えば別の行になります。

## エラー

`not_found` / `ambiguous`（共通仕様）。

## 備考

直接参照のみです。間接参照は `impact_analysis` を使います。
