# search_nodes テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| SN-01 | `query` = パスの部分文字列（大文字小文字違い） | 大文字小文字を無視して一致、短いパス順 |
| SN-02 | `query` = GUID（完全一致） | そのアセット 1 件 |
| SN-03 | `query` 空 + `type` = `Scene` | 全シーンを列挙 |
| SN-04 | `type` の大文字小文字違い（`prefab`） | `Prefab` と同じ結果 |
| SN-05 | `source` = `Assets` / `PackageCache` / `Unresolved` | 該当 source のみ |
| SN-06 | `unreferenced: true` | 他から参照されないアセットのみ |
| SN-07 | `limit` 未指定 | 最大 20 件。それ以上あれば `truncated: true` |
| SN-08 | `limit` = 0 / 1000 | 1 / 100 に丸められる |
| SN-09 | 一致なし | `matches: []`、エラーにならない |
| SN-10 | `type` / `source` が空白文字列 | 指定なしとして扱う |
| SN-11 | `matches[]` の形 | `name` / `guid`（name と同じなら省略）/ `type` / `source` |
