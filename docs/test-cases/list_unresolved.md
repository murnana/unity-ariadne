# list_unresolved テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| LU-01 | 未解決 GUID がある | `total_unresolved` と `unresolved[]`（`guid` / `ref_count` / `referrer_count` / `sample_referrers`） |
| LU-02 | 並び順 | 参照数の多い順 |
| LU-03 | 未解決が 0 件 | `total_unresolved: 0`、`unresolved: []` |
| LU-04 | 200 件超 | 200 件で打ち切り、`truncated: true` |
| LU-05 | `analyze --include-packages` の有無 | PackageCache 内の GUID は未解決に含まれない（含めた場合） |
| LU-06 | `{fileID: 0}` の null 参照 | 未解決として数えない |
