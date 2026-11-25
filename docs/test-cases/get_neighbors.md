# get_neighbors テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| GN-01 | `name` のみ（既定 hops=1） | `hops: 1`。依存先と参照元の両方が `assets` に含まれ、`edges` はそれらの間の辺 |
| GN-02 | `hops` = 2 | 2 ホップ以内のアセットが追加される |
| GN-03 | `hops` = 0 / 99 | それぞれ 1 / 3 に丸められる |
| GN-04 | `edges[]` の形 | `from` / `to` / `kind` / `ref_count` を持つ |
| GN-05 | `assets[]` の形 | `name` / `type` / `source` を持つ |
| GN-06 | 孤立アセット | `total_assets` は自分 1 件、`total_edges: 0` |
| GN-07 | assets または edges が 200 件超 | 200 件で打ち切り、`truncated: true`、合計は実数 |
| GN-08 | 曖昧 / 存在しない名前 | `ambiguous` / `not_found` |
