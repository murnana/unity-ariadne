# impact_analysis テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| IA-01 | `name` のみ（既定 depth） | `depth: 3`。`impacted[]` は `path` / `distance` / `via` を持つ |
| IA-02 | 直接参照元 | `distance: 1`、`via` が起点アセットを指す |
| IA-03 | A→B→C の鎖で C を指定 | B が distance 1、A が distance 2、A の `via` は B |
| IA-04 | `depth` = 0 / -1 | 1 に丸められる |
| IA-05 | `depth` = 99 | 10 に丸められる |
| IA-06 | 循環参照（A⇄B） | 無限ループせず、各アセットは最短距離で 1 回だけ現れる |
| IA-07 | 結果が 200 件超 | 200 件で打ち切り、`truncated: true`、`total_impacted` は実数 |
| IA-08 | 曖昧 / 存在しない名前 | `ambiguous` / `not_found`（FR-04, FR-05 と同形式） |
