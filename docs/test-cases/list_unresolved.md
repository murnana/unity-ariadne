# list_unresolved テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| LU-01 | 未解決 GUID がある | `total_unresolved` と `unresolved[]`（`guid` / `ref_count` / `referrer_count` / `sample_referrers`） |
| LU-02 | 並び順 | 参照数の多い順 |
| LU-03 | 未解決が 0 件 | `total_unresolved: 0`、`unresolved: []` |
| LU-04 | 既定の `limit`（20）を超える件数 | 20 件で打ち切り、`truncated: true`、`total_unresolved` は全件数 |
| LU-05 | `Library/PackageCache` がある状態で `analyze` | PackageCache 内のアセットの GUID は解決され、未解決に含まれない |
| LU-06 | `{fileID: 0}` の null 参照 | 未解決として数えない |
| LU-07 | 参照元がパッケージ内にある GUID | `hint.basis` が `referrer_package`、`package_candidates` はハッシュなしのパッケージ名（xUnit 実装済み） |
| LU-08 | 参照元は `Assets/` で、同じ参照元がパッケージのアセットも使っている | `basis` が `referrer_dependencies`（xUnit 実装済み） |
| LU-09 | 手がかりのない GUID（ProjectSettings のみ等） | `hint` が付かない（xUnit 実装済み） |
| LU-10 | `limit` = 0 または 999 | 1〜200 に丸められる（xUnit は 0 のみ実装済み） |
