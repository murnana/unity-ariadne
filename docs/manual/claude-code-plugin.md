# Claude Code プラグイン

`unity-ariadne` プラグインを入れると、Claude Code を開いたプロジェクトの依存関係グラフを MCP サーバー(`ariadne serve`)として自動で登録できます。`claude mcp add` を手で打つ必要がなく、グラフ(`graph.db`)の作成も初回の起動時に自動で行います。ツールの中身は [mcp-server.md](mcp-server.md) と同じです。

プラグインは `ariadne` コマンドを呼ぶだけで、.NET SDK も `dotnet run` も使いません。**先にインストーラーで `ariadne` を入れてください。**

## 必要なもの

- `ariadne`(インストーラーで PATH に入ります)
- **Node.js 20.12 以降**(PATH に `node` があること)。プラグインの起動用スクリプト(`scripts/launch.mjs`)が使います。`node --version` で確認できます

## 導入手順

1. [Releases](https://github.com/murnana/unity-ariadne/releases) のインストーラーで `ariadne` を入れます(Windows は `.msi`、macOS は `.pkg`)。
2. **ターミナルと Claude Code を再起動します**(Windows は PATH の変更が起動済みのプロセスに反映されないためです)。`ariadne --help` と `node --version` が動けば準備完了です。
3. Claude Code に marketplace を登録し、プラグインを入れます。marketplace は [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で管理しています。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   claude plugin install unity-ariadne@murnana-claude-code-plugin
   ```

4. Unity プロジェクトのルートで Claude Code を起動し、`/mcp` に `unity-ariadne` が出ていることを確認します。**`graph.db` は初回の起動時にプラグインが自動で作ります。事前の `analyze` は要りません。**

## graph.db の保存先と自動作成

プラグインは `scripts/launch.mjs` を起動し、これが次のことを行ってから `ariadne serve --db <graph.db>` を実行します。`ariadne` 本体は保存先の管理を知りません(`--db` で渡されたファイルを開くだけです)。

1. `graph.db` の保存先をプロジェクトごとに決める
2. 無ければ `ariadne analyze` で作る(初回のみ)
3. 使われなくなったプロジェクトのデータを掃除する

`graph.db` は**プロジェクトの中ではなく、プラグインのデータディレクトリ**(`~/.claude/plugins/data/<id>/`。Windows は `%USERPROFILE%\.claude\plugins\data\<id>\`)の、プロジェクトごとのフォルダに置かれます。`<id>` は `unity-ariadne@murnana-claude-code-plugin` の記号を `-` に置き換えた `unity-ariadne-murnana-claude-code-plugin` です。プロジェクトのフォルダは汚れず、`.gitignore` への追加も要りません。

```text
~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin/
├── index.json          # ハッシュ → プロジェクト名・パス・最終利用日時
├── 48f69d66fe0e/graph.db
└── 89e851c482fa/graph.db
```

- **フォルダ名**は、プロジェクトのフルパス(末尾の区切りと `.` / `..` を正規化したもの。Windows は大文字小文字を区別しません)の SHA-256 の先頭 12 文字です。同じプロジェクトは常に同じフォルダになります
- **衝突の回避**: 別のプロジェクトがすでに同じハッシュを使っていたら、フォルダ名を 16、20、…文字と延ばします。どのフォルダがどのプロジェクトかは `index.json` の `name` / `path` で分かります
- **複数のセッション**が同時に起動しても、`index.json` は排他ロックで保護されます
- 初回はプロジェクトの規模に応じて解析時間がかかります。解析中は `/mcp` が接続中のままになり、Claude Code の MCP 接続がタイムアウトすることがあります。タイムアウトすると解析も中断され、結果は残りません(次の起動でやり直しになります)。大きなプロジェクトでは、次の[事前の作成](#グラフの作成更新--update)をするか、環境変数 `MCP_TIMEOUT`(ミリ秒)を大きくして Claude Code を起動してください
- Unity プロジェクト(`Assets/` があるフォルダ)以外で Claude Code を開くと、`/mcp` で failed になります
- プラグインをアンインストールすると、Claude Code がデータディレクトリごと削除します(`claude plugin uninstall --keep-data` で残せます)

### 古いデータの掃除

起動のたびに、**30 日間使われていないプロジェクトのデータを削除**します(フォルダと `index.json` の項目)。削除後にそのプロジェクトを開くと、自動で作り直されます。

- 期間は環境変数 `ARIADNE_RETENTION_DAYS`(1 以上の整数)で変えられます。Claude Code の `settings.json` の `env` や、起動するシェルで設定してください
- 起動中の `serve` は 6 時間ごとに最終利用日時を更新するため、長く開きっぱなしのセッションの DB が、他のセッションの掃除で消えることはありません
- `index.json` が壊れたり失われたりして項目から外れた、ハッシュ名のフォルダも、中のファイルが保持期間を超えて古くなれば削除します。ハッシュ名でないフォルダには触れません
- 掃除に失敗しても(たとえば他のプロセスが使用中で削除できなくても)サーバーは起動します。削除できなかったデータは次回に再試行します

### グラフの作成・更新(`update-graph` スキル / `--update`)

自動で作られるのは**初回だけ**で、既存のグラフは自動では更新されません。コードやアセットを変更したときは、次のどちらかで更新します。

**Claude Code に頼む(おすすめ)**: プラグインの `update-graph` スキルを使います。「グラフを更新して」と頼むか、`/unity-ariadne:update-graph` を実行してください。アセットやスクリプトを変更した直後に MCP ツールの結果が実際と合わないときは、Claude が自分でこのスキルを使うこともあります。内容は次の `--update` の実行と、結果の報告です。

**自分で実行する**: Unity プロジェクトのルートで、起動スクリプトを `--update` 付きで実行します。大きなプロジェクトを、Claude Code を開く前に作っておきたいときにも使えます。

```sh
node <スクリプト> --update --data-dir ~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin --project .
```

- `<スクリプト>` は `find ~/.claude/plugins -name launch.mjs` で探せます(プラグインを更新するとパスが変わります)。Windows では `--data-dir` に `%USERPROFILE%\.claude\plugins\data\unity-ariadne-murnana-claude-code-plugin` を指定します
- 保存先(ハッシュのフォルダ)は自動で決まるので、パスを調べる必要はありません
- `graph.db` が無ければ作り、あれば**差分更新**して終了します。MCP サーバーは起動せず、稼働中の MCP サーバーの再起動も不要です(ツールは呼び出しごとに DB を開き直します)
- 更新に失敗しても、それまでの DB は残ります

## 使用する環境変数

| 環境変数 | 設定する人 | 用途 |
|---|---|---|
| `CLAUDE_PLUGIN_ROOT` | Claude Code が自動設定 | プラグインのインストール先。`.mcp.json` が起動スクリプトの場所を指すために使います。**利用者の設定は不要です** |
| `CLAUDE_PLUGIN_DATA` | Claude Code が自動設定 | プラグインのデータディレクトリ。`graph.db` の保存先になります。**利用者の設定は不要です** |
| `CLAUDE_PROJECT_DIR` | Claude Code が自動設定 | プロジェクトのルート。どのプロジェクトの DB を使うかを決めます。**利用者の設定は不要です** |
| `ARIADNE_RETENTION_DAYS` | 利用者(任意) | 掃除までの日数(デフォルト 30) |
| `ARIADNE_BIN` | 利用者(任意) | `ariadne` の実行ファイルのパス。PATH 上の `ariadne` 以外を使うときだけ設定します |
| `PATH` | インストーラー | `ariadne` を見つけるために使います。Windows は `C:\Program Files\Unity Ariadne` をシステムの PATH に追加します(**反映にはターミナルと Claude Code の再起動が必要**)。macOS は `/usr/local/bin/ariadne` へのリンクを置くため、通常は追加設定が要りません。`node` も PATH 上にある必要があります |

## graph.db を自分で決めた場所に置く場合

プラグインの DB の置き場所はデータディレクトリに固定です。別の場所に置く場合は、プラグインを入れずに `claude mcp add` で直接登録してください([mcp-server.md](mcp-server.md))。この場合、自動作成や掃除は働きません。

```sh
claude mcp add unity-ariadne -- ariadne serve --db <graph.dbの絶対パス>
```

## うまく動かないとき

- `/mcp` で `unity-ariadne` が failed になる → 次を順に確認してください
  1. Claude Code を **Unity プロジェクトのルート**(`Assets/` があるフォルダ)で起動しているか
  2. `node --version` が動くか(Node.js 20.12 以降)。`node` が無いと、起動そのものに失敗します
  3. `ariadne --help` が動くか(下の項目)
  4. 原因が分からないときは、Unity プロジェクトのルートで起動スクリプトを手動で実行すると、エラーメッセージが読めます。`ariadne serve` が起動して待機状態になれば成功です(Ctrl+C で止めます)

     ```sh
     node <スクリプト> --data-dir ~/.claude/plugins/data/unity-ariadne-murnana-claude-code-plugin --project .
     ```

- `ariadne` が見つからない → インストーラーで入れたあと、ターミナルと Claude Code を再起動してください
- データを消したい・容量を空けたい → データディレクトリの中の、不要なプロジェクトのフォルダ(`index.json` で名前を確認できます)を削除してください。`index.json` に項目が残っていても、次回の起動時に作り直されます
