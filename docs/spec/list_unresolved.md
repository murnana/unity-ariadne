# list_unresolved

未解決 GUID（参照先のアセットがプロジェクトに存在しない参照）を、参照数の多い順に返します。引数はありません。

## 出力

```json
{"total_unresolved": 4,
 "unresolved": [{"guid": "...", "ref_count": 10, "referrer_count": 3, "sample_referrers": ["Assets/A.unity"]}],
 "truncated": true}
```

`unresolved` は最大 200 件です。`{fileID: 0}` の null 参照は数えません。

## 備考

名前が分からない GUID への手がかりは、現状では返しません。提案は [new-features.md](new-features.md) を参照してください。
