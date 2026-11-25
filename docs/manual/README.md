# GuidGraph 利用者マニュアル

GuidGraph は、Unity プロジェクトのファイル同士の参照関係を調べるツールです。Unity Editor は使いません。Claude Code につなぐと、「このマテリアルはどこから使われていますか」のように日本語で質問できます。

**初めて使う場合は、自分のパソコンに合う入門マニュアルを読んでください:[Windows 版](getting-started-windows.md) / [macOS 版](getting-started-macos.md)。** Claude Code を入れるところから、最初の質問までを説明します。

## このマニュアルの対象

- Unity を使い慣れている人を対象にしています。
- Claude Code に詳しくなくても読めます。Claude Code は Anthropic の AI コーディングツールです。入れ方とログインの方法は、入門マニュアルの手順2から4で説明します。

## 対応する環境

| パソコン | インストーラー(ソフトを入れるためのファイル) |
|---|---|
| Windows(64ビットの x64) | `.msi` |
| macOS(Apple Silicon) | `.pkg` |

Intel の Mac 向けのインストーラーはありません。**Linux には対応していません。**

## 初めての人向けの文書

| やりたいこと | 文書 |
|---|---|
| Windows で入れて、質問できるようにする | [getting-started-windows.md](getting-started-windows.md) |
| Mac で入れて、質問できるようにする | [getting-started-macos.md](getting-started-macos.md) |
| 結果に出てこない参照を知る(**ファイルを削除する前に読んでください**) | [limitations.md](limitations.md) |

## 開発の知識がある人向けの文書

次の文書は、命令の入力やプログラムの知識がある人向けです。

| やりたいこと | 文書 |
|---|---|
| プラグイン(Claude Code に追加する機能)の動きと設定を知る | [claude-code-plugin.md](claude-code-plugin.md) |
| プログラムを自分で組み立てて、Claude Code に登録する | [claude-code-setup.md](claude-code-setup.md) |
| Claude Code を使わず、命令で調べる | [cli.md](cli.md) |
| Claude Code が使う機能(MCP のツール)の仕様を知る | [mcp-server.md](mcp-server.md) |
| 調べた結果のファイルを SQL で調べる | [schema.md](schema.md) |
| `export` 命令が書き出す memory.jsonl の形式を知る | [memory-jsonl.md](memory-jsonl.md) |
