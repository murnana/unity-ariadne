# Claude Code プラグイン

`unity-guidgraph` プラグイン(Claude Code に追加する機能)の動きと設定を説明します。このプラグインを入れると、Claude Code から GuidGraph を使えます。調べた結果のファイル(グラフ、`graph.db`)は、初回の起動時に自動で作られます。使える機能は [mcp-server.md](mcp-server.md) のとおりです。

初めて入れる場合は、[入門マニュアル](getting-started.md)の手順で入れてください。

## 入れる前に確認してください

- 質問すると、調べた結果(ファイル名、コード内の説明文など)が AI サービスへ送られます。仕事のプロジェクトでは、先に責任者の許可を得てください。
- 会社や学校のパソコンでは、ソフトを入れる前に管理者の許可を得てください。
- **30日間使わないプロジェクトのグラフは、自動で削除されます。** もう一度開くと、作り直されます。

## 必要なもの

- Claude Code(ログイン済みのもの)
- `guidgraph`(インストーラーで入れます)
- Node.js 20.12 以降

このプラグインは、入れた `guidgraph` を使います。.NET SDK(プログラムを組み立てる道具)は必要ありません。

## 導入手順

1. [配布ページ](https://github.com/murnana/unity-guidgraph/releases)のインストーラーで `guidgraph` を入れます。Windows は `.msi`、macOS は `.pkg` です。
2. ターミナル(命令を入力する画面)と Claude Code を閉じて、開き直します。
3. 次の2つの命令が動くことを確認します。

   ```sh
   guidgraph --help
   ```

   ```sh
   node --version
   ```

4. プラグインの配布元(marketplace)を登録します。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   ```

5. プラグインを入れます。

   ```sh
   claude plugin install unity-guidgraph@murnana-claude-code-plugin
   ```

6. `Assets` フォルダがある場所で Claude Code を起動します。大きなプロジェクトは、先に[グラフを自分で作成](#グラフを自分で作成更新する)してください。
7. `/mcp` と入力し、「plugin:unity-guidgraph:guidgraph」がつながっていることを確認します。

## グラフの作成と更新

自動で作られるのは初回だけです。ファイルを変更したら、次のどちらかで更新します。

最初の作成には時間がかかります。時間切れになると作成は中止され、結果は残りません。待ち時間は、環境変数(パソコンに登録しておく設定値)の `MCP_TIMEOUT` で延ばせます。単位はミリ秒です。

### Claude Code に頼む

「グラフを更新して」と入力します。`/unity-guidgraph:update-graph` と入力しても同じです。ファイルを変更した直後で結果が合わないときは、Claude が自分で更新することもあります。

### グラフを自分で作成・更新する

`Assets` フォルダがある場所で、次の2行を順に入力します。1行目は起動用のファイル(`launch.mjs`)を探し、2行目はそれを実行します。

Windows(PowerShell):

```powershell
$script = (Get-ChildItem "$HOME\.claude\plugins" -Recurse -Filter launch.mjs | Where-Object FullName -like "*unity-guidgraph*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

```powershell
node $script --update --data-dir "$HOME\.claude\plugins\data\unity-guidgraph-murnana-claude-code-plugin" --project .
```

macOS:

```sh
script=$(find ~/.claude/plugins -name launch.mjs -path '*unity-guidgraph*' -exec ls -t {} + | head -n 1)
```

```sh
node "$script" --update --data-dir ~/.claude/plugins/data/unity-guidgraph-murnana-claude-code-plugin --project .
```

グラフがなければ作り、あれば変更分だけを更新して終了します。更新に失敗しても、前のグラフは残ります。

## 質問して探す(search-graph)

質問に合うファイルを探し、見つけた理由も一緒に答える機能です。たとえば次のように質問します。`/unity-guidgraph:search-graph` と入力して呼ぶこともできます。

- 「`Enemy.cs` か、それを引き継いだクラスが付いているプレハブは?」
- 「ボスの行動に関係するスクリプトと、それが付いたプレハブは?」(コード内の説明文も手がかりにします)
- 「`Hero.prefab` が置かれているシーンは?(プレハブの中のプレハブも含めて)」
- 「UI フォルダのプレハブが使っているスクリプトは?」
- 「どこからも参照されていないプレハブは?」

結果に出なくても、使われていないとは限りません([limitations.md](limitations.md))。

コード内の説明文が検索できないという警告が出た場合は、グラフが古い形式です。「グラフを更新して」と入力すると、作り直されます。

## 設定値

通常は設定する必要はありません。

| 環境変数 | 内容 |
|---|---|
| `GUIDGRAPH_RETENTION_DAYS` | 使わないグラフを削除するまでの日数(1以上の整数)。指定しない場合は30 |
| `GUIDGRAPH_BIN` | 使う `guidgraph` の場所。入れたもの以外を使う場合だけ指定します |

日数を変える場合は、`~/.claude/settings.json` の `env` に追加します。`~` は自分のユーザーフォルダです。ファイルにほかの内容がある場合は消さず、`env` の部分だけを足します。

```json
{ "env": { "GUIDGRAPH_RETENTION_DAYS": "60" } }
```

## プラグインを消す

**次の命令は、グラフも削除します。**

```sh
claude plugin uninstall unity-guidgraph@murnana-claude-code-plugin
```

グラフを残す場合は、代わりに次の命令を入力します。

```sh
claude plugin uninstall unity-guidgraph@murnana-claude-code-plugin --keep-data
```

## グラフの場所を自分で決める

プラグインでは、グラフの場所は変えられません。別の場所に置く場合は、プラグインを消してから、次の命令で登録します。`<` と `>` で囲んだ部分は、記号ごと自分の場所に置き換えます。この方法では、自動の作成と削除は働きません。

```sh
claude mcp add guidgraph -- guidgraph serve --db "<graph.db の絶対パス>" --project "<Unity プロジェクトの絶対パス>"
```

「絶対パス」は、ドライブ名や `/` から始まる、省略のないファイルの場所です。

## うまく動かないとき

### `/mcp` で「plugin:unity-guidgraph:guidgraph」が失敗になる

次を順に確認します。

1. `Assets` フォルダがある場所で Claude Code を起動しているか。
2. `node --version` が動くか(20.12 以降)。
3. `guidgraph --help` が動くか。
4. それでも分からない場合は、起動用のファイルを直接実行して、表示される内容を読みます。

直接実行する場合は、上の `script` を探す1行目を入力してから、次を入力します。

Windows(PowerShell):

```powershell
node $script --data-dir "$HOME\.claude\plugins\data\unity-guidgraph-murnana-claude-code-plugin" --project .
```

macOS:

```sh
node "$script" --data-dir ~/.claude/plugins/data/unity-guidgraph-murnana-claude-code-plugin --project .
```

「guidgraph:」で始まる失敗の理由が出ず、次の命令を入力できる状態に戻らなければ、起動できています。Ctrl キーを押しながら C キーを押して止めます。

### `guidgraph` が見つからない

インストーラーで入れたあと、ターミナルと Claude Code を閉じて、開き直します。

### グラフを削除して容量を空けたい

1. Claude Code をすべて終了します。
2. 次の「保存先」のフォルダにある `index.json` を開き、`name` と `path` で対象のフォルダ名を確認します。
3. そのフォルダだけを削除します。Unity のプロジェクトは削除しないでください。

次に開くと、グラフは作り直されます。

## くわしい仕組み

この章は、開発の知識がある人向けです。

### 保存先

グラフは、プロジェクトの中ではなく、プラグインのデータフォルダに保存します。

- macOS:`~/.claude/plugins/data/unity-guidgraph-murnana-claude-code-plugin/`
- Windows:`%USERPROFILE%\.claude\plugins\data\unity-guidgraph-murnana-claude-code-plugin\`

```text
unity-guidgraph-murnana-claude-code-plugin/
├── index.json          # ハッシュ → プロジェクト名・パス・最終利用日時
├── 48f69d66fe0e/graph.db
└── 89e851c482fa/graph.db
```

- フォルダ名は、プロジェクトのフルパス(末尾の区切りと `.` / `..` を正規化したもの。Windows は大文字と小文字を区別しない)の SHA-256 の先頭12文字です。
- 別のプロジェクトと重なる場合は、16、20、…文字と延ばします。
- `index.json` は排他ロックで保護します。

### 起動時の処理

`scripts/launch.mjs` が保存先を決め、グラフがなければ `guidgraph analyze` で作ります。次に古いデータを削除し、`guidgraph serve --db <graph.db> --project <プロジェクト>` を実行します。

### 自動削除

- 起動中の `serve` は6時間ごとに最終利用日時を更新します。
- `index.json` の項目から外れたハッシュ名のフォルダも、保持期間を超えて古くなれば削除します。ハッシュ名でないフォルダには触れません。
- 削除に失敗してもサーバーは起動し、次回に再び試します。

### 環境変数

`CLAUDE_PLUGIN_ROOT`、`CLAUDE_PLUGIN_DATA`、`CLAUDE_PROJECT_DIR` は Claude Code が自動で設定します。インストーラーは、`guidgraph` を呼べるように設定します。Windows は `C:\Program Files\GuidGraph` を `PATH` に追加し、macOS は `/usr/local/bin/guidgraph` にリンクを置きます。
