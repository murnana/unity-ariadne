# Claude Code プラグイン

`guidgraph` プラグインを入れると、Claude Code を開いたプロジェクトの依存関係グラフを MCP サーバー(`guidgraph serve`)として自動で登録できます。`claude mcp add` を手で打つ必要がなく、グラフ(`graph.db`)の作成も初回の起動時に自動で行います。ツールの中身は [mcp-server.md](mcp-server.md) と同じです。

プラグインは `guidgraph` コマンドを呼ぶだけで、.NET SDK も `dotnet run` も使いません。**先にインストーラーで `guidgraph` を入れてください。**

## 必要なもの

- `guidgraph`(インストーラーで PATH に入ります)
- **Node.js 20.12 以降**(PATH に `node` があること)。プラグインの起動用スクリプト(`scripts/launch.mjs`)が使います。`node --version` で確認できます

## 導入手順

1. [Releases](https://github.com/murnana/unity-guidgraph/releases) のインストーラーで `guidgraph` を入れます(Windows は `.msi`、macOS は `.pkg`)。
2. **ターミナルと Claude Code を再起動します**(Windows は PATH の変更が起動済みのプロセスに反映されないためです)。`guidgraph --help` と `node --version` が動けば準備完了です。
3. Claude Code に marketplace を登録し、プラグインを入れます。marketplace は [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で管理しています。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   claude plugin install guidgraph@murnana-claude-code-plugin
   ```

4. Unity プロジェクトのルートで Claude Code を起動し、`/mcp` に `guidgraph` が出ていることを確認します。**`graph.db` は初回の起動時にプラグインが自動で作ります。事前の `analyze` は要りません。**

## graph.db の保存先と自動作成

プラグインは `scripts/launch.mjs` を起動し、これが次のことを行ってから `guidgraph serve --db <graph.db> --project <プロジェクト>` を実行します。`guidgraph` 本体は保存先の管理を知りません(`--db` で渡されたファイルを開くだけです)。

1. `graph.db` の保存先をプロジェクトごとに決める
2. 無ければ `guidgraph analyze` で作る(初回のみ)
3. 使われなくなったプロジェクトのデータを掃除する

`graph.db` は**プロジェクトの中ではなく、プラグインのデータディレクトリ**(`~/.claude/plugins/data/<id>/`。Windows は `%USERPROFILE%\.claude\plugins\data\<id>\`)の、プロジェクトごとのフォルダに置かれます。`<id>` は `guidgraph@murnana-claude-code-plugin` の記号を `-` に置き換えた `guidgraph-murnana-claude-code-plugin` です。プロジェクトのフォルダは汚れず、`.gitignore` への追加も要りません。

```text
~/.claude/plugins/data/guidgraph-murnana-claude-code-plugin/
├── index.json          # ハッシュ → プロジェクト名・パス・最終利用日時
├── 48f69d66fe0e/graph.db
└── 89e851c482fa/graph.db
```

- **フォルダ名**は、プロジェクトのフルパス(末尾の区切りと `.` / `..` を正規化したもの。Windows は大文字小文字を区別しません)の SHA-256 の先頭 12 文字です。同じプロジェクトは常に同じフォルダになります
- **衝突の回避**: 別のプロジェクトがすでに同じハッシュを使っていたら、フォルダ名を 16、20、…文字と延ばします。どのフォルダがどのプロジェクトかは `index.json` の `name` / `path` で分かります
- **複数のセッション**が同時に起動しても、`index.json` は排他ロックで保護されます
- 初回はプロジェクトの規模に応じて解析時間がかかります。解析中は `/mcp` が接続中のままになり、Claude Code の MCP 接続がタイムアウトすることがあります。タイムアウトすると解析も中断され、結果は残りません(次の起動でやり直しになります)。大きなプロジェクトでは、次の[事前の作成](#グラフの作成更新update-graph-スキル----update)をするか、環境変数 `MCP_TIMEOUT`(ミリ秒)を大きくして Claude Code を起動してください
- Unity プロジェクト(`Assets/` があるフォルダ)以外で Claude Code を開くと、`/mcp` で failed になります
- プラグインをアンインストールすると、Claude Code がデータディレクトリごと削除します(`claude plugin uninstall --keep-data` で残せます)

### 古いデータの掃除

起動のたびに、**30 日間使われていないプロジェクトのデータを削除**します(フォルダと `index.json` の項目)。削除後にそのプロジェクトを開くと、自動で作り直されます。

- 期間は環境変数 `GUIDGRAPH_RETENTION_DAYS`(1 以上の整数)で変えられます。Claude Code の `settings.json` の `env` や、起動するシェルで設定してください
- 起動中の `serve` は 6 時間ごとに最終利用日時を更新するため、長く開きっぱなしのセッションの DB が、他のセッションの掃除で消えることはありません
- `index.json` が壊れたり失われたりして項目から外れた、ハッシュ名のフォルダも、中のファイルが保持期間を超えて古くなれば削除します。ハッシュ名でないフォルダには触れません
- 掃除に失敗しても(たとえば他のプロセスが使用中で削除できなくても)サーバーは起動します。削除できなかったデータは次回に再試行します

### グラフの作成・更新(`update-graph` スキル / `--update`)

自動で作られるのは**初回だけ**で、既存のグラフは自動では更新されません。コードやアセットを変更したときは、次のどちらかで更新します。

**Claude Code に頼む(おすすめ)**: プラグインの `update-graph` スキルを使います。「グラフを更新して」と頼むか、`/guidgraph:update-graph` を実行してください。アセットやスクリプトを変更した直後に MCP ツールの結果が実際と合わないときは、Claude が自分でこのスキルを使うこともあります。内容は次の `--update` の実行と、結果の報告です。

**自分で実行する**: Unity プロジェクトのルートで、起動スクリプトを `--update` 付きで実行します。大きなプロジェクトを、Claude Code を開く前に作っておきたいときにも使えます。

```sh
node <スクリプト> --update --data-dir ~/.claude/plugins/data/guidgraph-murnana-claude-code-plugin --project .
```

- `<スクリプト>` は `find ~/.claude/plugins -name launch.mjs` で探せます(プラグインを更新するとパスが変わります)。Windows では `--data-dir` に `%USERPROFILE%\.claude\plugins\data\guidgraph-murnana-claude-code-plugin` を指定します
- 保存先(ハッシュのフォルダ)は自動で決まるので、パスを調べる必要はありません
- `graph.db` が無ければ作り、あれば**差分更新**して終了します。MCP サーバーは起動せず、稼働中の MCP サーバーの再起動も不要です(ツールは呼び出しごとに DB を開き直します)
- 更新に失敗しても、それまでの DB は残ります

### オントロジー検索(`search-graph` スキル)

グラフのノードにはアセットの種類(Scene / Prefab / Script / Material …)、エッジには参照の種類(`uses_script` / `prefab_instance` / `inherits` …)が付いています。`search-graph` スキルは、質問をこの「種類と関係の連鎖」と「名前・概念の手がかり」(ファイル名・フォルダー名・C# の XML ドキュメントコメント)のクエリに直し、guidgraph の `search` ツールを呼んで、返ってきた根拠(参照経路、当たった名前・コメント、Editor / Resources などの注記)を添えて答えます。**検索そのものは guidgraph が行い、スキルは呼び出すだけです**。同じ検索は `guidgraph search` コマンドでも使えます([cli.md](cli.md))。

次のような質問をすると、Claude がこのスキルを使います(`/guidgraph:search-graph` で明示的に呼ぶこともできます)。

- 「`Enemy.cs` かそのサブクラスがコンポーネントとして付いているプレハブは?」
- 「ボスの行動に関係するスクリプトと、それが付いたプレハブは?」(ドキュメントコメントに「ボス」とあるスクリプトから探します)
- 「`Hero.prefab` が置かれているシーンは?(ネストしたプレハブ経由も含めて)」
- 「UI フォルダーのプレハブが使っているスクリプトは?」
- 「どこからも参照されていないプレハブは?」

ドキュメントコメントと型名は `analyze` が記録します。この機能より前の guidgraph で作った `graph.db` には入っていないため、`update-graph` で作り直してください(スキーマが変わったので自動でフル再構築になります)。

ツールの仕様は [mcp-server.md](mcp-server.md) をご覧ください。

## 使用する環境変数

| 環境変数 | 設定する人 | 用途 |
|---|---|---|
| `CLAUDE_PLUGIN_ROOT` | Claude Code が自動設定 | プラグインのインストール先。`.mcp.json` が起動スクリプトの場所を指すために使います。**利用者の設定は不要です** |
| `CLAUDE_PLUGIN_DATA` | Claude Code が自動設定 | プラグインのデータディレクトリ。`graph.db` の保存先になります。**利用者の設定は不要です** |
| `CLAUDE_PROJECT_DIR` | Claude Code が自動設定 | プロジェクトのルート。どのプロジェクトの DB を使うかを決めます。**利用者の設定は不要です** |
| `GUIDGRAPH_RETENTION_DAYS` | 利用者(任意) | 掃除までの日数(デフォルト 30) |
| `GUIDGRAPH_BIN` | 利用者(任意) | `guidgraph` の実行ファイルのパス。PATH 上の `guidgraph` 以外を使うときだけ設定します |
| `PATH` | インストーラー | `guidgraph` を見つけるために使います。Windows は `C:\Program Files\GuidGraph` をシステムの PATH に追加します(**反映にはターミナルと Claude Code の再起動が必要**)。macOS は `/usr/local/bin/guidgraph` へのリンクを置くため、通常は追加設定が要りません。`node` も PATH 上にある必要があります |

## graph.db を自分で決めた場所に置く場合

プラグインの DB の置き場所はデータディレクトリに固定です。別の場所に置く場合は、プラグインを入れずに `claude mcp add` で直接登録してください([mcp-server.md](mcp-server.md))。この場合、自動作成や掃除は働きません。

```sh
claude mcp add guidgraph -- guidgraph serve --db <graph.dbの絶対パス> --project <Unity プロジェクトの絶対パス>
```

## うまく動かないとき

- `/mcp` で `guidgraph` が failed になる → 次を順に確認してください
  1. Claude Code を **Unity プロジェクトのルート**(`Assets/` があるフォルダ)で起動しているか
  2. `node --version` が動くか(Node.js 20.12 以降)。`node` が無いと、起動そのものに失敗します
  3. `guidgraph --help` が動くか(下の項目)
  4. 原因が分からないときは、Unity プロジェクトのルートで起動スクリプトを手動で実行すると、エラーメッセージが読めます。`guidgraph serve` が起動して待機状態になれば成功です(Ctrl+C で止めます)

     ```sh
     node <スクリプト> --data-dir ~/.claude/plugins/data/guidgraph-murnana-claude-code-plugin --project .
     ```

- `guidgraph` が見つからない → インストーラーで入れたあと、ターミナルと Claude Code を再起動してください
- データを消したい・容量を空けたい → データディレクトリの中の、不要なプロジェクトのフォルダ(`index.json` で名前を確認できます)を削除してください。`index.json` に項目が残っていても、次回の起動時に作り直されます
