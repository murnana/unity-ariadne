# エージェント向けの質問（トークン消費の比較用）

AI エージェント（Claude Code）に実際に聞く質問です。グラフありとグラフなしで、同じ質問に対する回答の正しさと、トークン消費・ターン数を比べるために使います。計測結果はここには書きません。

## 測り方

- サンプルプロジェクトをカレントディレクトリにして、`claude -p --output-format json --model sonnet --max-turns 40 --tools Read,Glob,Grep --strict-mcp-config --disable-slash-commands --permission-mode dontAsk` を実行し、質問の文面は標準入力で渡します。
- グラフありは `--mcp-config <guidgraph の serve を登録した JSON> --allowedTools "mcp__guidgraph__*"` を足します。グラフなしは `--allowedTools Read,Glob,Grep` を足します。
- トークン数は、結果の JSON の `usage`（input + output + cache read + cache creation）の合計です。ターン数は `num_turns` です。

（[../development/benchmark.md](../development/benchmark.md) は、`sqlite3` で直接引く形の過去の測定で、条件が異なります。）

前提: サンプルプロジェクト（Fantasy Kingdom in Unity 6 | URP。[../development/fixtures.md](../development/fixtures.md)）を `analyze` した graph.db。質問の文面は固定し、プロジェクトの内容が変わったときは期待する答えを `graph.db` から確かめ直します。

| ID | 分類 | 質問 | 期待する答え | 主に使うツール |
|---|---|---|---|---|
| AQ-01 | 全プレハブ横断 | Assets/ 以下のプレハブのうち、どのシーンからも入れ子（プレハブの中のプレハブ）を含めて使われていないものを列挙してください。 | 5 件: `Grass_A_A` / `Grass_A_B` / `Grass_A_C`（Assets/Art/Meshes/Scatter/Grass_A/）、`Terrain_Rivers`（Assets/Art/Prefabs/Compositions/）、`Preset_House_02_B`（Assets/Art/Prefabs/Houses/）。Assets/ のプレハブ 141 件のうち、シーンから到達できるのは 136 件 | `unreachable`（既定の引数） |
| AQ-02 | プロジェクト概要 | このプロジェクトの構成（フォルダごとの資産の種類、シーン、主要スクリプトの役割）を概要としてまとめてください。 | シーンは Demo / Loader / VFXTestScene の 3 つ。ビルド順は Loader → Demo。フォルダごとの種類（Art に Model・Prefab・Texture など）を挙げる。スクリプトの役割は名前からの推定と分かる書き方にする | `summarize_project`、`list_folders` |
| AQ-03 | 特定対象の起点 | Atlas_Mat.mat を参照しているアセットを、種別ごとの件数とあわせて挙げてください。 | 24 件: Prefab 12 件（Assets/Art/Prefabs/Foliage/Trees and Bushes/）、`.st9`（SpeedTree）の `.meta` 経由 12 件（Meshes 側 11、Textures 側 1） | `find_references`（`summary`） |
| AQ-04 | 特定対象の起点 | RuntimeLoader.cs を使っているシーンやプレハブと、その役割を教えてください。 | 使っている側と、スクリプトの役割（起動時にシーンを読み込む等）を、グラフの参照とコードの内容から説明する | `find_references`、`search` |
| AQ-05 | 集計 | Assets/ 以下のスクリプトのうち、プレハブにアタッチされている数が多い上位 10 件を、プレハブ数つきで挙げてください。 | アタッチされているのは 5 件だけで、どれも 1 プレハブ: `ButtonAnimation`、`StatsCanvasController`（StatsCanvas.prefab）、`AudioHeightBlendComponent`（Audio.prefab）、`RTSCameraController`、`InputProvider`（RTSCameraController.prefab）。Assets/ 内のスクリプトは 17 件で、残りは付いていない | `search`（`uses_script` を `in` で、Script から Prefab へ） |

## 確かめること

- 回答が期待する答えと一致する（AQ-02・AQ-04 は事実の食い違いがないこと）。
- AQ-01・AQ-05 のように多数の対象を扱う質問で、対象ごとにツールを呼び直さず、1〜2 回の呼び出し（AQ-01 は `unreachable`、AQ-05 は `search`）で済んでいる。
- ツールの結果が呼び出し側の出力上限を超えて、ファイルへ退避されていない（退避されると読み直しでターンが増える。search は約 48,000 文字で自動的に件数を減らす）。
- AQ-02 で、グラフなしより多くのトークンを使っていない。

## 過去に見つかった問題

| 問題 | 質問 | 原因と対処 |
|---|---|---|
| 検索結果が上限を超え、ファイルに退避されて読み直しが増えた | AQ-01 | Prefab 全件の `search` で、全件に `role` を付けて出力が上限をまたいだ。`role` は wrapper のときだけにし、`search` は約 48,000 文字で件数を減らす |
| 1 件ずつ `find_references` を呼ぶ往復が増えた | AQ-05 | `summary` の追加で個別呼び出しが促された。説明に「多数のアセットには `search` の `in` ステップを使う」と書いた |
| 回答の漏れ（正解 5 件のところ 3 件） | AQ-05 | 集計の取りこぼし。`search` の経路で全件を数える |
| 概要の質問でグラフありのほうが多い | AQ-02 | 概要を取る専用の手段が無く、往復が増えた。`summarize_project` を追加 |
| 同じ質問でも探索経路が毎回変わる | 全般 | 1 回の計測では比べられない。各セル 3〜4 回で比べる |
