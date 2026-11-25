# list_folders テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| LF-01 | 既定（`Assets`, depth 1） | 直下フォルダーごとに `assets`（再帰件数）と `types` |
| LF-02 | `path` = `Assets/Characters` | その配下のみ |
| LF-03 | `path` 空 | 最上位を列挙 |
| LF-04 | `depth` = 2 | 孫フォルダーまで含む |
| LF-05 | `depth` = 0 / 99 | 1 / 4 に丸められる |
| LF-06 | `types` | 件数の多い順（同数は名前順）で最大 6 種 |
| LF-07 | 存在しない `path` | `total_folders: 0`、エラーにならない |
| LF-08 | フォルダーが 200 件超 | 200 件で打ち切り、`truncated: true` |
