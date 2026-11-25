# GuidGraph 利用者マニュアル

GuidGraph は、Unity プロジェクトのファイル同士の参照関係を調べるツールです。Unity Editor は使いません。Claude Code につなぐと、「このマテリアルはどこから使われている?」のように日本語で質問できます。

## このマニュアルの対象

- Unity を使い慣れている人を対象にしています。
- Claude Code に詳しくなくても読めます。Claude Code は Anthropic の AI コーディングツールです。使う場合は、先にインストールとログインを済ませてください。

## 対応環境

| OS | インストーラー |
|---|---|
| Windows | `.msi` |
| macOS(Apple Silicon) | `.pkg` |

Intel Mac 向けのインストーラーはありません。**Linux には対応していません。**

## はじめかた

Claude Code から使う場合は、**プラグイン**をおすすめします。MCP サーバー(Claude Code と外部ツールをつなぐ仕組み)の登録と、解析結果のファイル `graph.db` の作成が自動になります。

→ [claude-code-plugin.md](claude-code-plugin.md)

ほかの使い方は次のとおりです。

| やりたいこと | 読む文書 |
|---|---|
| ソースからビルドして Claude Code に登録する | [claude-code-setup.md](claude-code-setup.md) |
| Claude Code を使わず、コマンドで解析する | [cli.md](cli.md) |

## 必要になったら読む文書

| 知りたいこと | 文書 |
|---|---|
| Claude Code が使うツールの仕様 | [mcp-server.md](mcp-server.md) |
| 結果に出てこない参照(**アセットを削除する前に読んでください**) | [limitations.md](limitations.md) |
| `graph.db` を SQL で直接調べる(SQL の知識が必要) | [schema.md](schema.md) |
| `export` が書き出す memory.jsonl の形式 | [memory-jsonl.md](memory-jsonl.md) |
