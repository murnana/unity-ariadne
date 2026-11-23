# MCP ツール仕様

`guidgraph serve` が公開する MCP ツールの仕様書です。実装は `src/Analyzer/Mcp/GraphMcpTools.cs` と `src/Graph/GraphSearch.cs` にあります。テストケースは [../test-cases/](../test-cases/) を参照してください。

## 共通仕様

- 出力は 1 つの JSON 文字列で、キーは snake_case、値が null のキーは省略します。非 ASCII 文字（日本語パスなど）はエスケープしません。
- 呼び出しごとに db を読み取り専用で開き、閉じます。db は変更しません。
- 一覧は上限件数で打ち切り、打ち切った場合は `truncated: true` を付けます。`total_*` は実数です。
- アセット指定（`name` / `from.asset`）は、プロジェクト相対パス、一意のファイル名、GUID のいずれかです。解決できないときは次を返します。
  - `{error: "not_found", name, hint}`
  - `{error: "ambiguous", name, candidate_count, candidates(最大10), hint}`
- アセットの要約形式: `{name, guid?, type, source, dangling?}`。`guid` は name と異なるときだけ、`dangling` は未解決 GUID のときだけ付きます。

## 既存ツール

- [find_references](find_references.md)
- [impact_analysis](impact_analysis.md)
- [get_neighbors](get_neighbors.md)
- [search_nodes](search_nodes.md)
- [list_folders](list_folders.md)
- [describe_ontology](describe_ontology.md)
- [search](search.md)
- [list_unresolved](list_unresolved.md)

## 新機能（提案。未実装）

- [database.md](database.md) — データベース定義一覧
- [new-features.md](new-features.md)
