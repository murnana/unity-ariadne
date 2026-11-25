# MCP ツール仕様

`guidgraph serve` が公開する MCP ツールの仕様書です。MCP の入口は `src/Analyzer/Mcp/GraphMcpTools.cs`（鮮度は `GraphFreshness.cs`）、`search` は `src/Graph/GraphSearch.cs`、`expanded_count` は `src/Graph/ExpandedCount.cs`、`unreachable` は `src/Graph/Unreachable.cs` にあります。テストケースは [../test-cases/](../test-cases/) を参照してください。

## 共通仕様

- 出力は 1 つの JSON 文字列で、キーは snake_case、値が null のキーは省略します。非 ASCII 文字（日本語パスなど）はエスケープしません。
- 呼び出しごとに db を読み取り専用で開き、閉じます。db は変更しません。
- 応答には `meta`（`analyzed_at`、`--project` 指定時は `stale` / `stale_count`）が付きます。判定は db ファイルとプロジェクトのファイルの更新時刻の比較だけです。詳細は [new-features.md](new-features.md) の N-4 を参照してください。
- 一覧は上限件数で打ち切り、打ち切った場合は `truncated: true` を付けます。`total_*` は実数です。
- アセット指定（`name` / `from.asset`）は、プロジェクト相対パス、一意のファイル名、GUID のいずれかです。解決できないときは次を返します。
  - `{error: "not_found", name, hint}`
  - `{error: "ambiguous", name, candidate_count, candidates(最大10), hint}`
- アセットの要約形式: `{name, guid?, type, source, dangling?}`。`guid` は name と異なるときだけ、`dangling` は未解決 GUID のときだけ付きます。

## ツール

- [find_references](find_references.md)
- [impact_analysis](impact_analysis.md)
- [get_neighbors](get_neighbors.md)
- [search_nodes](search_nodes.md)
- [list_folders](list_folders.md)
- [describe_ontology](describe_ontology.md)
- [search](search.md)
- [list_unresolved](list_unresolved.md)
- [summarize_project](summarize_project.md)
- [expanded_count](expanded_count.md)
- [unreachable](unreachable.md)

## データベースと追加仕様

- [databases/](databases/README.md) — データベース定義
- [new-features.md](new-features.md) — 追加仕様（N-2 と N-3 は未実装）
