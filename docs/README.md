# Unity Ariadne ドキュメント

Unity Ariadne は、Unity プロジェクトを Unity Editor なしで解析し、アセット層(YAML / .meta / ProjectSettings / JSON 埋め込み)とコード層(Roslyn 構文解析)の依存関係グラフを SQLite に構築する CLI ツールです。

## アーキテクチャ

```
Unityプロジェクト
  ├─ Assets/** + Packages/** + Library/PackageCache/**
  │    ├─ *.meta ──────────────────► [GUID辞書 + meta内参照]
  │    ├─ *.unity/prefab/asset/... ─► [Unity YAMLパーサー]
  │    ├─ *.shadergraph / .vfx ─────► [JSON埋め込み参照]
  │    └─ ProjectSettings/*.asset ──► [設定参照抽出]
  ├─ *.cs ──► [Roslyn構文解析(コード層)]
  ▼
[グラフ統合・GUID解決] ─► graph.db (SQLite) ─┬─► memory.jsonl エクスポート
                                             ├─► 未解決GUIDレポート
                                             └─► MCPサーバー (stdio)
```

**SQLite が唯一の真実源**で、memory.jsonl や MCP ツールの応答はそこからの集約ビューです。粒度は 2 層あります:

- **オブジェクト層(SQLite のみ)**: YAML ドキュメント = fileID 単位です。「どのコンポーネントのどのフィールドが参照しているか」(property_path)まで保持します
- **アセット層(jsonl / MCP に出す層)**: オブジェクト層をファイル単位に集約したものです(`asset_edges` ビュー)

## マニュアル([manual/](manual/))

利用者向けの使い方ドキュメントです。

| ドキュメント | 内容 |
|---|---|
| [manual/claude-code-setup.md](manual/claude-code-setup.md) | チェックアウトから Claude Code で使えるまでの通し手順(SDK 準備 → 解析 → MCP 登録 → 運用) |
| [manual/cli.md](manual/cli.md) | セットアップと CLI コマンドリファレンス(`analyze` / `report` / `export` / `serve`)、差分更新の仕組み |
| [manual/mcp-server.md](manual/mcp-server.md) | MCP サーバーのセットアップと 5 ツールのリファレンス、活用パターン |
| [manual/claude-code-plugin.md](manual/claude-code-plugin.md) | Claude Code プラグインで MCP サーバーを自動登録する手順(環境変数の説明を含む) |
| [manual/schema.md](manual/schema.md) | SQLite スキーマ、ref_kind 一覧、sqlite3 で直接引くクエリ例 |
| [manual/memory-jsonl.md](manual/memory-jsonl.md) | `export` が書き出す memory.jsonl のフォーマット仕様 |
| [manual/limitations.md](manual/limitations.md) | 既知の制約と近似 — 「グラフに出ないもの」の一覧 |

- **使いはじめる** → [manual/cli.md](manual/cli.md) のセットアップ節からどうぞ
- **Claude Code で使えるようにしたい(チェックアウト直後から)** → [manual/claude-code-setup.md](manual/claude-code-setup.md)
- **Claude Code などの AI エージェントから使う** → [manual/mcp-server.md](manual/mcp-server.md)
- **SQL で自由に分析したい** → [manual/schema.md](manual/schema.md)
- **「この参照が出てこないのはなぜ?」** → [manual/limitations.md](manual/limitations.md)

## 開発記録([development/](development/))

開発者向けの資料です。**残課題は [GitHub Issues](https://github.com/murnana/unity-ariadne/issues) で管理**しています。

| ドキュメント | 内容 |
|---|---|
| [development/internals.md](development/internals.md) | 実装メモ — Unity のファイル形式の癖と、パーサー・差分更新の設計上の事実(英語) |
| [development/fixtures.md](development/fixtures.md) | テストフィクスチャの用意の仕方(リポジトリには同梱していません) |
| [development/benchmark.md](development/benchmark.md) | 効果測定 — グラフ有無でのトークン消費比較(84%削減)の方法と全数値 |
| [development/release.md](development/release.md) | リリース手順 |

## 関連資料(リポジトリ直下)

- `README.md` — 概要とクイックスタート
- `CLAUDE.md` — Claude Code 向けの開発ガイド
