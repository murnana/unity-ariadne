# Unity Ariadne

[![CI](https://github.com/murnana/unity-ariadne/actions/workflows/ci.yml/badge.svg)](https://github.com/murnana/unity-ariadne/actions/workflows/ci.yml) [![CodeQL](https://github.com/murnana/unity-ariadne/actions/workflows/codeql.yml/badge.svg)](https://github.com/murnana/unity-ariadne/actions/workflows/codeql.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![Release](https://img.shields.io/github/v/release/murnana/unity-ariadne)](https://github.com/murnana/unity-ariadne/releases)

Unityプロジェクトを解析して依存関係グラフをSQLiteに構築するCLIツールです。**Unity Editor不要**で、ファイルを直接パースし、`dotnet run` のみで動きます。

「このアセットを消しても大丈夫?」「このマテリアルを変えたら何が壊れる?」— grepの効かないGUID参照層の問いに、AIエージェント(Claude Code等)が数クエリで答えられるようになります。実測でトークン消費**84%削減**しました([効果測定](docs/development/benchmark.md))。

## 何を解析するか

- **アセット層**: シーン / プレハブ / マテリアル等のYAML、.meta、ProjectSettings、shadergraph等のJSON埋め込み参照、Addressablesグループ
- **コード層**: C#をRoslynで構文解析します — 継承、シリアライズフィールド、GetComponent、Resources.Load / Addressables.Loadのリテラルキー等
- 参照は19種のref_kindに分類され、SQLiteに1参照=1行で入ります(→ [docs/manual/schema.md](docs/manual/schema.md))

## 使い方

```sh
dotnet run --project src/Analyzer -- analyze <unity-project-path> --db graph.db
    # 既存の graph.db は差分更新されます(変更ファイルのみ再解析、フル約8秒→変更なし約2.4秒)。--full でフル再構築します
dotnet run --project src/Analyzer -- report --db graph.db                     # 参照切れGUID一覧
dotnet run --project src/Analyzer -- export --db graph.db --jsonl out.jsonl   # memory.jsonl 出力。--include-packages でPackageCacheも出力
dotnet run --project src/Analyzer -- serve --db graph.db                      # MCPサーバー(stdio)
```

`dotnet` がPATHにない場合は `export PATH="$HOME/.dotnet:$PATH"` を実行してください。詳細は [docs/manual/cli.md](docs/manual/cli.md) をご覧ください。

`dotnet` なしで使う場合は、[Releases](../../releases)のインストーラーを使えます(`ariadne` コマンドがPATHに入ります)。Windowsは `.msi`、macOSは `.pkg`(Apple Silicon の `osx-arm64` のみ。Intel Mac 向けは配布していません)です。いずれも未署名のため、初回はSmartScreen / Gatekeeperの警告が出ます。

このリポジトリを丸ごとcloneする代わりに、[Releases](../../releases)から `src/Analyzer` + `src/Graph` のソース一式をまとめたzip(コンパイル済みバイナリではありません)を落として使うこともできます。中の `USAGE.md` に最小限の実行手順が入っています。

### Claude Code プラグイン

インストーラーで `ariadne` を入れたうえで、Claude Code プラグインを使うと MCP サーバーが自動で登録されます(`claude mcp add` 不要)。導入手順と使用する環境変数は [docs/manual/claude-code-plugin.md](docs/manual/claude-code-plugin.md) をご覧ください。

### MCPサーバー

`serve` はグラフDBをMCP(stdio)で公開します。Claude Code からは:

```sh
claude mcp add unity-ariadne -- dotnet run --project <このリポジトリ>/src/Analyzer -- serve --db <graph.dbのパス>
```

ツールは5つあります(`find_references` / `impact_analysis` / `get_neighbors` / `search_nodes` / `list_unresolved`)。アセット名はプロジェクト相対パス・(一意な)ファイル名・GUIDのいずれでも指定できます。ツール呼び出しごとにDBを読み直すため、`analyze` でグラフを作り直せばサーバー再起動なしで反映されます。詳細は [docs/manual/mcp-server.md](docs/manual/mcp-server.md) をご覧ください。

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [docs/manual/claude-code-setup.md](docs/manual/claude-code-setup.md) | チェックアウトから Claude Code で使えるまでの通し手順 |
| [docs/manual/cli.md](docs/manual/cli.md) | セットアップとCLIコマンドリファレンス、差分更新の仕組み |
| [docs/manual/mcp-server.md](docs/manual/mcp-server.md) | MCPサーバーのセットアップと5ツールのリファレンス |
| [docs/manual/claude-code-plugin.md](docs/manual/claude-code-plugin.md) | Claude Code プラグインで MCP サーバーを自動登録する手順 |
| [docs/manual/schema.md](docs/manual/schema.md) | SQLiteスキーマ、ref_kind一覧、sqlite3で直接引くクエリ例 |
| [docs/manual/memory-jsonl.md](docs/manual/memory-jsonl.md) | `export` が書き出す memory.jsonl のフォーマット仕様 |
| [docs/manual/limitations.md](docs/manual/limitations.md) | 既知の制約と近似 — 「グラフに出ないもの」の一覧 |
| [docs/development/benchmark.md](docs/development/benchmark.md) | 効果測定(グラフ有無でのトークン消費比較、84%削減) |

開発者向け: 実装の詳細は [docs/development/internals.md](docs/development/internals.md)、テストフィクスチャの扱いは [docs/development/fixtures.md](docs/development/fixtures.md) をご覧ください。

## 効果測定(要約)

グラフの有無で同一調査タスク3種をClaude Codeに解かせて比較しました(読み取り専用ツール、正答は全条件で一致):

| タスク | 削減率 |
|---|---:|
| 影響範囲調査(マテリアルの直接参照元) | **79%** |
| 未使用アセット探し(未参照プレハブ、15分8秒 → 56秒) | **88%** |
| 実装箇所特定(コードシンボル — grepで既に最適) | -3% |
| **合計**(3,555,423 → 563,854 トークン) | **84%** |

グラフは逆依存(「誰がこれを参照しているか」)を問うタスクで圧倒的に効き、コードシンボル検索では効きません。方法・全数値・考察は [docs/development/benchmark.md](docs/development/benchmark.md) をご覧ください。

## リポジトリ構成

- `src/Graph` — SQLiteスキーマとデータモデル
- `src/Analyzer` — CLI本体(YAML / .meta / ProjectSettings / JSON埋め込み / Roslynコード解析 / MCPサーバー)
- `tests/UnityAriadne.Tests` — xUnit(差分テスト・census・Unity突合)。実プロジェクトを使うテストはフィクスチャを外部から渡したときだけ走ります([docs/development/fixtures.md](docs/development/fixtures.md))
- `installer/` — MSI / pkg インストーラーのビルド定義
- `plugins/` — AI エージェント向けプラグイン(AI ごとにディレクトリを分離。現在は `claude-code/`)
- `docs/manual/` — 利用者向けマニュアル、`docs/development/` — 実装メモ・効果測定・リリース手順

## コントリビュート

Issue と Pull Request を歓迎します。[CONTRIBUTING.md](CONTRIBUTING.md) と [行動規範](CODE_OF_CONDUCT.md) をご覧ください。脆弱性は公開 Issue ではなく [SECURITY.md](SECURITY.md) の手順で報告してください。

## ライセンス

[MIT License](LICENSE)。利用しているサードパーティ製ソフトウェアのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にまとめています。

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
