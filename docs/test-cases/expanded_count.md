# expanded_count テストケース

| ID | 入力 | 期待結果 |
|---|---|---|
| EC-01 | シーンが Forest を 2 つ、Tree を 1 つ置き、Forest が Tree を 3 つ置く。Tree の中の 3 つのオブジェクトが Material を使う | 展開数 21（xUnit 実装済み） |
| EC-02 | `asset` が prefab | 配置数の合計（Tree 7、Forest 2）。置かれていない prefab は 0（xUnit 実装済み） |
| EC-03 | `max_depth` = 1 | 1 段だけ展開し、`depth_limited: true`（xUnit 実装済み） |
| EC-04 | インスタンスがコンポーネントと GameObject（子孫つき）を取り除いた | その分を差し引き、見つからない ID は `unresolved_removals` に数える（xUnit 実装済み） |
| EC-05 | `scene` が Material | `error: invalid_scene` |
| EC-06 | 古い db（`placements` が無い） | バージョン不一致の db は `analyze` が全再構築する |
| EC-07 | サンプルの Demo.unity、Atlas_Mat.mat | 独立した総当たりの数え方（入れ子を 1 つずつたどる）と一致する（手動で確認済み: 6651） |
| EC-08 | 増分更新と全再構築 | `placements` / `placement_removals` が行単位で一致する（`GraphUpdaterTests` の Dump に含む。サンプルで確認済み） |
