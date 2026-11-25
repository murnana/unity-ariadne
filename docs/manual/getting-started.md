# GuidGraph 入門マニュアル

Unity を使い慣れていて、Claude Code を初めて使う人向けの説明です。Windows(64ビットの x64)と、Apple Silicon の Mac を対象にしています。Linux と Intel の Mac には対応していません。

## このマニュアルでできること

Unity のプロジェクトについて、Claude Code に日本語で質問できるようになります。たとえば「このマテリアルはどこで使われていますか」と質問できます。Claude Code は、Anthropic の AI コーディングツールです。

## はじめる前に準備するもの

- Windows のパソコン、または Apple Silicon の Mac
  - Mac は、リンゴのマークから「この Mac について」を開きます。「チップ」が「Apple」で始まれば使えます。【要確認:画面の表示】
- インターネット接続
- 調べたい Unity のプロジェクト(`Assets` フォルダがあるもの)
- Claude Code を使えるアカウント
  - Pro、Max、Team、Enterprise などの有料プランが必要です。
  - 使った分の料金を払う「Console」のアカウントでも使えます。
  - claude.ai の無料プランでは使えません。料金は申し込む前に確認してください。
  - アカウントの作成は、[claude.ai](https://claude.ai/) で始めます。【要確認:申し込みの手順】
- パソコンにソフトを入れる権限

### 始める前に確認してください

- **情報の送信**:質問すると、調べた結果が Anthropic などの AI サービスへ送られます。
  - 結果には、ファイル名、コード内の説明文、README の一部などが含まれます。
  - Claude Code がファイルの中身を読んで送る場合もあります。
  - 仕事のプロジェクトでは、先に責任者の許可を得てください。
- **ソフトの追加**:会社や学校のパソコンでは、先に管理者の許可を得てください。

### Unity の設定

GuidGraph は、Unity のファイルの保存形式が「Force Text」でないと、中身を調べられません。

**設定を変える場合は、先に次の2つを済ませてください。** 多くのファイルが書き換わることがあるためです。

- 仕事のプロジェクトでは、責任者の許可を得ます。
- Unity を閉じて、プロジェクトのフォルダ全体を別の場所にコピーします。

1. Unity でプロジェクトを開きます。
2. 「Edit」から「Project Settings」を開き、「Editor」を選びます。【要確認:画面の表示】
3. 「Asset Serialization」の「Mode」が「Force Text」であることを確認します。違う場合は、選び直します。【要確認:画面の表示】

プロジェクトを Unity で一度開くと、パッケージ(Unity に追加した部品)の中のファイルも調べられるようになります。

## 手順

枠の中の命令は、文字で命令を入力する画面に入力します。命令はコピーして貼り付けます。1行ずつ貼り付けて、Enter キーを押します。

### 1. 命令を入力する画面を開く

- Windows:スタートボタンを押して「PowerShell」と入力し、表示された項目を開きます。
- Mac:「アプリケーション」の「ユーティリティ」から「ターミナル」を開きます。【要確認:画面の表示】

### 2. Claude Code を入れる

すでに入っている場合は、手順3に進みます。

**次の命令は、Anthropic のサイトから取得したプログラムをすぐに実行します。**

Windows(PowerShell):

```powershell
irm https://claude.ai/install.ps1 | iex
```

Mac:

```sh
curl -fsSL https://claude.ai/install.sh | bash
```

次の命令を入力できる状態に戻ったら、画面を一度閉じて、開き直します。うまくいかないときの表示が出た場合は、[Claude Code の公式の説明](https://code.claude.com/docs/ja/setup)を確認してください。

### 3. Claude Code が入ったことを確認する

```sh
claude --version
```

版の番号が表示されます。

### 4. Claude Code にログインする

1. 次の命令を入力します。

   ```sh
   claude
   ```

2. 画面に質問が表示された場合は、内容を読んで選びます。【要確認:初回に表示される画面】
3. ブラウザーに表示される案内に従って、ログインします。ブラウザーが開かない場合は、画面に表示されたアドレスを開きます。【要確認:画面の表示】
4. ログインできたら、Claude Code の入力欄に `/exit` と入力して終了します。【要確認:終了の操作】

### 5. Node.js を入れる

Node.js は、このあと追加する機能を動かすためのソフトです。版は 20.12 以降が必要です。

1. [Node.js のダウンロードページ](https://nodejs.org/en/download)を開きます。
2. 自分のパソコン向けのインストーラー(ソフトを入れるためのファイル)を取得します。
   - Windows:名前の最後が `.msi` のファイルです。【要確認:画面のボタン】
   - Mac:Apple Silicon(arm64)向けのファイルです。【要確認:ファイルの種類と画面のボタン】
3. 取得したファイルを開き、画面の案内に従って入れます。

### 6. GuidGraph を入れる

**GuidGraph のインストーラーには、作成者を証明する署名がありません。** そのため、開くと警告が表示されます。次の配布ページから取得したファイルだけを開いてください。会社や学校のパソコンで判断できない場合は、管理者に相談してください。

1. [GuidGraph の配布ページ](https://github.com/murnana/unity-guidgraph/releases)を開きます。
2. 一番新しい版の「Assets」を開きます。これはファイルの一覧です。Unity の Assets フォルダとは別のものです。
3. 次のファイルを取得します。【要確認:次の版のファイル名】
   - Windows:名前の最後が `win-x64.msi` のファイル
   - Mac:名前の最後が `osx-arm64.pkg` のファイル
4. 取得したファイルを開きます。
5. 警告が表示されたら、実行を許可します。
   - Windows:「詳細情報」を押してから「実行」を押します。【要確認:警告画面の表示】
   - Mac:「システム設定」の「プライバシーとセキュリティ」を開き、「このまま開く」を押します。【要確認:画面の表示】
6. 画面の案内に従って入れます。

### 7. 準備ができたことを確認する

1. 命令を入力する画面と Claude Code をすべて閉じて、開き直します。
2. 次の命令を入力します。版の番号が表示されます。20.12 以降であることを確認します。

   ```sh
   node --version
   ```

3. 次の命令を入力します。「usage:」を含む、使い方の一覧が表示されます。

   ```sh
   guidgraph --help
   ```

### 8. Claude Code に GuidGraph の機能を追加する

追加する機能を「プラグイン」と呼びます。【要確認:配布元での公開と、プラグインの名前】

1. 次の命令で、プラグインの配布元を登録します。

   ```sh
   claude plugin marketplace add murnana/murnana-claude-code-plugin
   ```

2. 次の命令で、プラグインを入れます。

   ```sh
   claude plugin install unity-guidgraph@murnana-claude-code-plugin
   ```

どちらも、完了の表示を確認してから次に進みます。すでに登録済みと表示された場合も、次に進みます。【要確認:表示される文】

### 9. Unity のプロジェクトで Claude Code を起動する

起動すると、プロジェクトを調べた結果のファイルが自動で作られます。このファイルを「グラフ」(`graph.db`)と呼びます。

- グラフは、プロジェクトのフォルダの外に保存されます。
- **30日間使わないグラフは、自動で削除されます。** もう一度起動すると、作り直されます。

1. `Assets` フォルダがある場所へ移動します。
   - Windows:エクスプローラーで、`Assets` の1つ上のフォルダを右クリックして「パスのコピー」を選びます。`cd ` に続けて貼り付けます。【要確認:画面の表示】
   - Mac:`cd ` と入力します。続けて、同じフォルダを画面にドラッグします。【要確認:操作】

   Windows の入力例は次のとおりです。`"` は1組だけにします。

   ```powershell
   cd "C:\Projects\MyGame"
   ```

2. Claude Code を起動します。最初の作成には時間がかかるため、待ち時間を10分に延ばします。`600000` はミリ秒で、10分のことです。

   Windows(PowerShell):

   ```powershell
   $env:MCP_TIMEOUT = "600000"
   ```

   ```powershell
   claude
   ```

   Mac:

   ```sh
   MCP_TIMEOUT=600000 claude
   ```

3. フォルダを信用するかを聞かれた場合は、自分のプロジェクトであることを確認して許可します。【要確認:表示される文】

### 10. つながったことを確認する

1. Claude Code の入力欄に、次のとおり入力します。

   ```text
   /mcp
   ```

2. 一覧の「plugin:unity-guidgraph:guidgraph」の状態を確認します。チェックの印なら、つながっています。準備中の場合は、少し待ってもう一度 `/mcp` を入力します。【要確認:画面の表示】
3. Esc キーで一覧を閉じます。【要確認:操作】

「×」の印の場合は、「よくある質問」を見てください。

### 11. 質問する

Claude Code の入力欄に質問を入力します。`Player.prefab` は、自分のプロジェクトのファイル名に置き換えます。

```text
Player.prefab はどこで使われていますか
```

使っているファイルの一覧などが回答されます。

途中で操作の許可を聞かれる場合があります。調べるだけなら、ファイルの書き換えや削除は必要ありません。内容が分からない操作は、許可しないでください。

**ファイルを削除する前に**:一覧に何も出なくても、使われていないとは限りません。

1. Unity を閉じて、プロジェクトのフォルダ全体を別の場所にコピーします。
2. ファイルを削除します。
3. Unity で開き、そのファイルを使いそうな場面を再生して確認します。

調べられない参照は、[limitations.md](limitations.md) の最初にまとめています。

### 12. ファイルを変えたあとに結果を新しくする

グラフは、自動では新しくなりません。Unity のファイルを変えたら、次のように入力します。

```text
グラフを更新して
```

命令の実行の許可を聞かれたら、`launch.mjs` を実行する命令であることを確認して許可します。更新の結果が報告されます。【要確認:許可を聞く画面】

## よくある質問

**Q. 「claude」「node」「guidgraph」が見つからないと表示されます。**

命令を入力する画面と Claude Code をすべて閉じて、開き直します。直らない場合は、そのソフトを入れる手順をもう一度実行します。

**Q. `/mcp` で「×」の印が表示されます。**

次を順に確認します。直らない場合は、次の質問に進みます。

1. `Assets` フォルダがある場所で Claude Code を起動しているか。
2. 手順7の2つの命令が動くか。
3. 手順9の、待ち時間を延ばした方法で起動したか。

**Q. 10分待っても、つながりません。**

先にグラフだけを作ります。Claude Code を終了し、`Assets` フォルダがある場所で、次の2行を順に入力します。

Windows(PowerShell):

```powershell
$script = (Get-ChildItem "$HOME\.claude\plugins" -Recurse -Filter launch.mjs | Where-Object FullName -like "*unity-guidgraph*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

```powershell
node $script --update --data-dir "$HOME\.claude\plugins\data\unity-guidgraph-murnana-claude-code-plugin" --project .
```

Mac:

```sh
script=$(find ~/.claude/plugins -name launch.mjs -path '*unity-guidgraph*' -exec ls -t {} + | head -n 1)
```

```sh
node "$script" --update --data-dir ~/.claude/plugins/data/unity-guidgraph-murnana-claude-code-plugin --project .
```

最後に「database ready」または「Wrote」を含む行が表示されたら、手順9からやり直します。それ以外の場合は、表示された文を確認します。

**Q. 「does not look like a Unity project」と表示されます。**

Claude Code を終了します。`Assets` フォルダがある場所へ移動して、起動し直します。

**Q. プラグインを消したいです。**

**次の命令は、グラフも削除します。** Unity のプロジェクトは削除されません。

```sh
claude plugin uninstall unity-guidgraph@murnana-claude-code-plugin
```

グラフを残す場合は、代わりに次の命令を入力します。

```sh
claude plugin uninstall unity-guidgraph@murnana-claude-code-plugin --keep-data
```

**Q. 解決しない場合はどうすればよいですか。**

[GuidGraph の Issues](https://github.com/murnana/unity-guidgraph/issues) で報告できます。報告は誰でも読めます。仕事の情報やファイル名は書かないでください。
