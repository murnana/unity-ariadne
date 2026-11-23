# find_references テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| FR-01 | `name` = `Assets/...` の完全パス | `asset` が解決され、`total_referrers` と `references[]`（`from` / `kind` / `ref_count` / `source`）を返す |
| FR-02 | `name` = プロジェクト内で一意のファイル名 | FR-01 と同じ結果 |
| FR-03 | `name` = GUID | FR-01 と同じ結果 |
| FR-04 | `name` = 同名ファイルが複数ある名前 | `error: ambiguous`、`candidates` は最大 10 件、`hint` あり |
| FR-05 | `name` = 存在しない名前 | `error: not_found` と `hint` |
| FR-06 | 参照元が 0 件のアセット | `total_referrers: 0`、`references: []`、エラーにならない |
| FR-07 | 参照元が 200 件を超えるハブアセット | `references` は 200 件、`truncated: true`、`total_referrers` は実数 |
| FR-08 | 未解決 GUID（`dangling`）を指定 | `asset.dangling: true`、参照元は返る |
| FR-09 | 同一参照元が複数回参照 | `ref_count` が参照回数と一致する |
| FR-10 | 読み取り専用 | 呼び出し前後で db ファイルが変化しない |
| FR-11 | `summary: true` | `references` は無く、`by_kind` / `by_type` と `total_referrers` を返す（xUnit 実装済み） |
| FR-12 | `summary: false` を明示 | `references` を返し、`by_kind` は無い（xUnit 実装済み） |
| FR-13 | `summary: true`、`top` = 0 または 99 | 1〜20 に丸められ、`by_kind[].top` の件数がその範囲に収まる |
