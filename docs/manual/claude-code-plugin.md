# Claude Code プラグイン

`unity-ariadne` プラグインを入れると、Claude Code を開いたプロジェクトの `graph.db` を MCP サーバー(`ariadne serve`)として自動で登録できます。`claude mcp add` を手で打つ必要がなくなります。ツールの中身は [mcp-server.md](mcp-server.md) と同じです。

プラグインは `ariadne` コマンドを呼ぶだけで、.NET SDK も `dotnet run` も使いません。**先にインストーラーで `ariadne` を入れてください。**

## 導入手順

1. [Releases](https://github.com/murnana/unity-ariadne/releases) のインストーラーで `ariadne` を入れます(Windows は `.msi`、macOS は `.pkg`)。
2. **ターミナルと Claude Code を再起動します**(Windows は PATH の変更が起動済みのプロセスに反映されないためです)。`ariadne --help` が動けば準備完了です。
3. Claude Code に marketplace を登録し、プラグインを入れます。marketplace は [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で管理しています。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   claude plugin install unity-ariadne@murnana-claude-code-plugin
   ```

4. Unity プロジェクトのルートで Claude Code を起動し、`/mcp` に `unity-ariadne` が出ていることを確認します。**グラフ(`graph.db`)は初回の起動時にプラグインが自動で作ります。事前の `analyze` は要りません。**

## graph.db の保存先と自動作成

プラグインは次のコマンドで MCP サーバーを起動します。

```sh
ariadne serve --data-dir ${CLAUDE_PLUGIN_DATA} --project ${CLAUDE_PROJECT_DIR}
```

`graph.db` は**プロジェクトの中ではなく、プラグインのデータディレクトリ**(`~/.claude/plugins/data/<id>/`。Windows は `%USERPROFILE%\.claude\plugins\data\<id>\`)の、プロジェクトごとのフォルダに置かれます。`<id>` は `unity-ariadne@murnana-claude-code-plugin` の記号を `-` に置き換えた `unity-ariadne-murnana-claude-code-plugin` です。プロジェクトのフォルダは汚れず、`.gitignore` への追加も要りません。

```text
~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin/
├── index.json          # ハッシュ → プロジェクト名・パス・最終利用日時
├── 48f69d66fe0e/graph.db
└── 89e851c482fa/graph.db
```

- フォルダ名はプロジェクトのパスのハッシュです。どのフォルダがどのプロジェクトかは `index.json` の `name` / `path` で分かります。ハッシュが衝突した場合の扱いなど、詳細は [cli.md](cli.md#データディレクトリ--data-dir) をご覧ください
- **30 日間使われなかったプロジェクトのデータは、プラグインの起動時に自動で削除されます。** 削除後にそのプロジェクトを開くと、自動で作り直されます
- プラグインをアンインストールすると、Claude Code がデータディレクトリごと削除します(`claude plugin uninstall --keep-data` で残せます)
- 初回はプロジェクトの規模に応じて解析時間がかかります。Claude Code の MCP 接続がタイムアウトする場合は、次のように先に解析しておくか、環境変数 `MCP_TIMEOUT`(ミリ秒)を大きくして Claude Code を起動してください

  ```sh
  ariadne analyze . --data-dir ~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin
  ```

- Unity プロジェクト(`Assets/` があるフォルダ)以外で Claude Code を開くと、`/mcp` で failed になります
- **`ariadne` を `--data-dir` に対応したバージョンに更新**してください。古い `ariadne` では `unexpected argument '--data-dir'` で failed になります

### グラフの更新

コードを変更したら、同じ `--data-dir` を指定して `analyze` を再実行してください(上のコマンドです。Unity プロジェクトのルートで実行します)。差分更新され、MCP サーバーの再起動は不要です。自動で作られるのは**初回だけ**で、既存のグラフは自動では更新されません。

## 使用する環境変数

| 環境変数 | 設定する人 | 用途 |
|---|---|---|
| `CLAUDE_PROJECT_DIR` | Claude Code が自動設定 | プロジェクトのルート。どのプロジェクトの DB を使うかを決めるために、プラグインの `.mcp.json` が `--project` に渡します。**利用者の設定は不要です** |
| `CLAUDE_PLUGIN_DATA` | Claude Code が自動設定 | プラグインのデータディレクトリ。`.mcp.json` が `--data-dir` に渡し、`graph.db` の保存先になります。**利用者の設定は不要です** |
| `PATH` | インストーラー | `ariadne` を見つけるために使います。Windows は `C:\Program Files\Unity Ariadne` をシステムの PATH に追加します(**反映にはターミナルと Claude Code の再起動が必要**)。macOS は `/usr/local/bin/ariadne` へのリンクを置くため、通常は追加設定が要りません |

## graph.db を自分で決めた場所に置く場合

プラグインの DB の置き場所はデータディレクトリに固定です。別の場所に置く場合は、プラグインを入れずに `claude mcp add` で直接登録してください([mcp-server.md](mcp-server.md))。この場合、自動作成や掃除は働きません。

```sh
claude mcp add unity-ariadne -- ariadne serve --db <graph.dbの絶対パス>
```

## うまく動かないとき

- `/mcp` で `unity-ariadne` が failed になる → まず Claude Code を **Unity プロジェクトのルート**(`Assets/` があるフォルダ)で起動しているか確認してください。`Assets/` が無いフォルダでは起動できません。古い `ariadne` では `--data-dir` が未対応のため、インストーラーで更新してください。原因が分からないときは、Unity プロジェクトのルートで次を手動実行して、エラーメッセージを確認してください

  ```sh
  ariadne serve --data-dir ~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin --project "$PWD"
  ```

- データを消したい・容量を空けたい → データディレクトリの中の、不要なプロジェクトのフォルダ(`index.json` で名前を確認できます)を削除してください。`index.json` に項目が残っていても、次回の起動時に作り直されます
- `ariadne` が見つからない → インストーラーで入れたあと、ターミナルと Claude Code を再起動してください
