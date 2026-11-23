# describe_ontology テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| DO-01 | 通常のグラフ | `classes[]`（`type` / `category` / `count` / `by_source`）と `relations[]` を返す |
| DO-02 | `search_guide` | 検索例（`inherits` + `uses_script` の鎖）を含む文字列が返る |
| DO-03 | `relations[]` の形 | `kind` / `layer` / `out` / `in` / `transitive` / `edges` / `ref_count` / `signatures` |
| DO-04 | signatures が 12 件超の relation | 12 件のみ、残数が `more_signatures` に入る |
| DO-05 | 用語集にない ref_kind | `out` / `in` / `layer` が省略され、エラーにならない |
| DO-06 | 空のグラフ | `classes` / `relations` が空配列 |
| DO-07 | `search` の警告 | `describe_ontology` にない type / kind を `search` に渡すと `warnings` に出る |
