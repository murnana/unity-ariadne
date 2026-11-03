# Claude Code プラグイン

`unity-ariadne` プラグインを入れると、Claude Code を開いたプロジェクトの `graph.db` を MCP サーバー(`ariadne serve`)として自動で登録できます。`claude mcp add` を手で打つ必要がなくなります。ツールの中身は [mcp-server.md](mcp-server.md) と同じです。

プラグインは `ariadne` コマンドを呼ぶだけで、.NET SDK も `dotnet run` も使いません。**先にインストーラーで `ariadne` を入れてください。**

## 導入手順

1. [Releases](https://github.com/murnana/unity-ariadne/releases) のインストーラーで `ariadne` を入れます(Windows は `.msi`、macOS は `.pkg`)。
2. **ターミナルと Claude Code を再起動します**(Windows は PATH の変更が起動済みのプロセスに反映されないためです)。`ariadne --help` が動けば準備完了です。
3. Unity プロジェクトのルートで、グラフを作ります。

   ```sh
   ariadne analyze . --db graph.db
   ```

4. Claude Code に marketplace を登録し、プラグインを入れます。marketplace は [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で管理しています。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   claude plugin install unity-ariadne@murnana-claude-code-plugin
   ```

5. Unity プロジェクトのルートで Claude Code を起動し、`/mcp` に `unity-ariadne` が出ていることを確認します。

コードを変更したら `ariadne analyze . --db graph.db` を再実行してください。差分更新され、MCP サーバーの再起動は不要です。

## 使用する環境変数

| 環境変数 | 設定する人 | 用途 |
|---|---|---|
| `CLAUDE_PROJECT_DIR` | Claude Code が自動設定 | プロジェクトのルート。プラグインの `.mcp.json` が `${CLAUDE_PROJECT_DIR}/graph.db` を DB のパスにするために使います。**利用者の設定は不要です** |
| `PATH` | インストーラー | `ariadne` を見つけるために使います。Windows は `C:\Program Files\Unity Ariadne` をシステムの PATH に追加します(**反映にはターミナルと Claude Code の再起動が必要**)。macOS は `/usr/local/bin/ariadne` へのリンクを置くため、通常は追加設定が要りません |

## graph.db をプロジェクト直下以外に置く場合

プラグインの DB パスは `${CLAUDE_PROJECT_DIR}/graph.db` に固定です。別の場所に置く場合は、プラグインを入れずに `claude mcp add` で直接登録してください([mcp-server.md](mcp-server.md))。

```sh
claude mcp add unity-ariadne -- ariadne serve --db <graph.dbの絶対パス>
```

## うまく動かないとき

- `/mcp` で `unity-ariadne` が failed になる → `graph.db` がプロジェクト直下にあるか確認してください。`serve` は起動時に DB パスを検証し、無ければ即エラー終了します
- `ariadne` が見つからない → インストーラーで入れたあと、ターミナルと Claude Code を再起動してください
