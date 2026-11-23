# summarize_project テストケース

| ID | 入力 | 期待結果 |
|---|---|---|
| SP-01 | 引数なし | `scenes` / `build_order` / `folders` / `scripts` を返す。切り詰めが無ければ `*_truncated` は無い（xUnit 実装済み） |
| SP-02 | `max_chars` = 200、`scene_limit` = 0 | `scenes` が空で `scenes_truncated: true`（xUnit 実装済み） |
| SP-03 | `max_chars` が小さい | シーン、フォルダー、README の順に切り詰め、切り詰めた項目に `*_truncated` が付く |
| SP-04 | `folder_depth` = 0 または 9 | 1〜3 に丸められる |
| SP-05 | `ProjectPath` 未設定 | `readme` を返さない（xUnit 実装済み） |
| SP-06 | `ProjectPath` に README がある、`readme_chars` = 10 | `readme` が 10 文字で、本文がそれより長ければ `readme_truncated: true` |
| SP-07 | ビルド設定にシーンがある | `build_order` が `m_Scenes[n]` の順 |
| SP-08 | `[FixtureFact]` サンプルプロジェクト | 既定の出力が約 1,300 文字に収まる |
