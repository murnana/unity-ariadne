# Claude Code プラグイン

`unity-ariadne` プラグインを入れると、Claude Code を開いたプロジェクトの `graph.db` を MCP サーバー(`ariadne serve`)として自動で登録できます。`claude mcp add` を手で打つ必要がなくなります。ツールの中身は [mcp-server.md](mcp-server.md) と同じです。

プラグインは `ariadne` コマンドを呼ぶだけで、.NET SDK も `dotnet run` も使いません。**先にインストーラーで `ariadne` を入れてください。**

## 導入手順

1. [Releases](https://github.com/murnana/unity-ariadne/releases) のインストーラーで `ariadne` を入れます(Windows は `.msi`、macOS は `.pkg`)。
2. **ターミナルと Claude Code を再起動します**(Windows は PATH の変更が起動済みのプロセスに反映されないためです)。`ariadne --help` が動けば準備完了です。
3. (任意)Unity プロジェクトのルートで、グラフを先に作っておけます。`graph.db` が無い場合は、プラグインが初回の起動時に自動で作るため、省略しても動きます。

   ```sh
   ariadne analyze . --db graph.db
   ```

4. Claude Code に marketplace を登録し、プラグインを入れます。marketplace は [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で管理しています。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   claude plugin install unity-ariadne@murnana-claude-code-plugin
   ```

5. Unity プロジェクトのルートで Claude Code を起動し、`/mcp` に `unity-ariadne` が出ていることを確認します。

コードを変更したら `ariadne analyze . --db graph.db` を再実行してください。差分更新され、MCP サーバーの再起動は不要です。自動で作られるのは**初回(`graph.db` が無いとき)だけ**で、既存の `graph.db` は自動では更新されません。

### graph.db の自動作成

プラグインは `ariadne serve --db ${CLAUDE_PROJECT_DIR}/graph.db --project ${CLAUDE_PROJECT_DIR}` で起動します。`graph.db` が無ければ `analyze` してから MCP サーバーを開始します。

- 初回はプロジェクトの規模に応じて解析時間がかかります。Claude Code の MCP 接続がタイムアウトする場合は、先に手順 3 で `analyze` を実行するか、環境変数 `MCP_TIMEOUT`(ミリ秒)を大きくして Claude Code を起動してください
- Unity プロジェクト(`Assets/` があるフォルダ)以外で Claude Code を開くと、`/mcp` で failed になります
- `--project` を使うため、**`ariadne` を `serve --project` に対応したバージョンに更新**してください。古い `ariadne` では `unexpected argument '--project'` で failed になります

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

- `/mcp` で `unity-ariadne` が failed になる → まず Claude Code を **Unity プロジェクトのルート**(`Assets/` があるフォルダ)で起動しているか確認してください。`graph.db` が無ければ `analyze` してから起動しますが、`Assets/` が無いフォルダでは解析に失敗して終了します。古い `ariadne` では `--project` が未対応のため、インストーラーで更新してください。原因が分からないときは `ariadne serve --db "$PWD/graph.db" --project "$PWD"` を手動で実行して、エラーメッセージを確認してください
- `ariadne` が見つからない → インストーラーで入れたあと、ターミナルと Claude Code を再起動してください
