# search テストケース

前提: 小規模な Unity プロジェクト（シーン・プレハブ・スクリプト・マテリアルを含む）を `analyze` した graph.db に対して MCP ツールを呼ぶ。フィクスチャ依存のケースは `[FixtureFact]` で実装し、リポジトリにフィクスチャ内容は含めない。

| ID | 入力 | 期待結果 |
|---|---|---|
| SE-01 | `from.asset` のみ | その 1 件が `matches` に入り、`path` は無い |
| SE-02 | `from.asset` と filters を同時指定 | `error: invalid_start` |
| SE-03 | `from` が空 `{}` | `error: invalid_start` |
| SE-04 | `from.asset` が曖昧 / 不在 | `ambiguous`（候補最大 10 件）/ `not_found` |
| SE-05 | `from.keywords` のみ | ファイル名・フォルダー名・C# 型名・doc コメントに一致した資産を、一致数の多い順に返す。`clues` と `doc` が付く |
| SE-06 | 日本語キーワード（全角/半角、ひらがな/カタカナ違い） | 表記ゆれを無視して一致。返る名前・clues は原文のまま |
| SE-07 | `match_all: true` で全キーワードに一致する資産が無い | `relax` 有効なら「いずれか一致」に緩和され、`relaxed` に理由が出る |
| SE-08 | `from.types` + `path_contains` | 両条件を満たす資産のみ |
| SE-09 | `steps` 1 つ（`in` + `uses_script` + `types:[Prefab]`） | スクリプトを使うプレハブが返り、`path` は `A <-[uses_script]- B` 形式、`from_clues` が付く |
| SE-10 | `inherits` を `max_hops: 10`、`include_self: true` | 基底クラス自身と全派生クラスが対象になる |
| SE-11 | `direction` = `sideways` など不正値、数字 | `error: invalid_direction` |
| SE-12 | `steps` が 9 個 | `error: invalid_steps` |
| SE-13 | 結果 0 件、`relax: true` | `path_contains` → `kinds` → `max_hops` の順に緩和し、`relaxed` に記録 |
| SE-14 | 結果 0 件、`relax: false` | 緩和せず `matches: []` |
| SE-15 | `unreferenced: true` | 参照されない結果のみ |
| SE-16 | `limit` = 0 / 1000 | 1 / 200 に丸められる。超過時は `truncated: true` |
| SE-17 | 存在しない type / kind | `warnings` に `describe_ontology` を案内する文が出る |
| SE-18 | `relations` | 使われた kind ごとに `A -[kind]-> B: 意味` の説明が出る |
| SE-19 | Editor/ や Resources/ 配下の結果 | `notes` にフォルダー規約の注記が出る |
| SE-20 | C# シンボル無しの古い db でキーワード検索 | `warnings` に再 analyze の案内が出る |
| SE-21 | CLI `guidgraph search --query` と同一 JSON | MCP の `search` と同じ結果になる |
| SE-22 | Prefab を含む結果 | Prefab にだけ `role`（`wrapper` / `leaf`）が付く（xUnit 実装済み） |
| SE-23 | 入れ子の PrefabInstance と自前の MonoBehaviour を持つ prefab | `leaf` になる（xUnit 実装済み） |
