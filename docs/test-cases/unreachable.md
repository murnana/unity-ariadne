# unreachable テストケース

前提: 小規模なグラフ（シーン S → prefab A → prefab B、孤立した prefab C、A だけが参照する prefab D の参照元が C、など）。実装は `tests/GuidGraph.Tests/UnreachableTests.cs`。UN-13 はサンプルプロジェクトで手で確かめる。

| ID | 入力 | 期待結果 |
|---|---|---|
| UN-01 | 既定（Prefab、ルートは Scene） | シーンから直接・入れ子・間接に到達できない prefab だけが `matches` に入る。`candidates = reachable + unreachable` |
| UN-02 | 入れ子の先（S → A → B） | B は到達できているので `matches` に入らない |
| UN-03 | 孤立した prefab C | `referrers: 0` |
| UN-04 | 到達できない prefab C だけが参照する prefab D | D は `matches` に入り、`referrers: 1`、`referrer_samples` に C |
| UN-05 | 到達できないものが `Resources/` 配下 | その件に `notes` で Resources の注記が付く |
| UN-06 | `build_only: true`、ビルド設定に載っていないシーンだけが参照する prefab | その prefab は `matches` に入る（`false` なら入らない） |
| UN-07 | `kinds` を `["prefab_instance"]` に絞る | `generic` でだけつながっている prefab も `matches` に入る |
| UN-08 | `path_contains` | `candidates` もその語を含むものに絞られる |
| UN-09 | 到達できないものが `limit` を超える | `limit` 件で `truncated: true`、`unreachable` は全件数 |
| UN-10 | ルートが 0 件（シーンが無いプロジェクト） | `error: no_roots` |
| UN-11 | 存在しない `types` / `kinds` | `warnings` に `describe_ontology` を案内する文 |
| UN-12 | 常に | 限界（動的な読み込みを検出できない）を伝える `notes` が付く |
| UN-13 | （手動）サンプルプロジェクトで既定 | `candidates` 141、`reachable` 136、`unreachable` 5。5 件は [agent-questions.md](agent-questions.md) の AQ-01 と一致する |
| UN-14 | 読み取り専用 | 呼び出し前後で db ファイルが変化しない |
