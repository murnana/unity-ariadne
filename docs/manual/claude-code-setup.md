# Claude Code セットアップガイド(ソースから)

プログラムのもと(ソース)を取得して組み立て、Claude Code に登録する手順です。開発の知識がある人向けです。通常は入門マニュアル([Windows 版](getting-started-windows.md) / [macOS 版](getting-started-macos.md))の方法を使ってください。

命令の詳細は [cli.md](cli.md)、機能の仕様は [mcp-server.md](mcp-server.md) にあります。

## はじめる前に

- **情報の送信**:質問すると、調べた結果(ファイル名、コード内の説明文など)が AI サービスへ送られます。仕事のプロジェクトでは、先に責任者の許可を得てください。
- 会社や学校のパソコンでは、ソフトを入れる前に管理者の許可を得てください。
- Claude Code を入れて、ログインしておきます(入門マニュアル([Windows 版](getting-started-windows.md) / [macOS 版](getting-started-macos.md))の手順2から4)。
- Git(ソースを取得するソフト)を入れておきます。
- 対応する環境は Windows と macOS です。Linux には対応していません。

この説明では、次の3つの場所を使います。`<` と `>` で囲んだ部分は、記号ごと自分の場所に置き換えます。空白を含む場所は `"` で囲みます。

| 記号 | 意味 | 例(macOS) |
|---|---|---|
| `<guidgraph>` | 取得したソースのフォルダ | `~/Projects/unity-guidgraph` |
| `<unity-project>` | 調べる Unity プロジェクト(`Assets` があるフォルダ) | `~/Projects/MyGame` |
| `<graph.db>` | 結果を保存する専用のファイル | `~/Projects/MyGame/graph.db` |

## 1. ソースを取得する

ソースを置きたいフォルダで、次の命令を入力します。【要確認:公開後に使うブランチ】

```sh
git clone https://github.com/murnana/unity-guidgraph.git
```

`unity-guidgraph` フォルダが作られます。

## 2. .NET 10 SDK を用意する

.NET 10 SDK は、プログラムを組み立てる道具です。次の命令で確認します。

```sh
dotnet --version
```

`10.` で始まる番号が表示されれば、手順3に進みます。

表示されない場合は、[配布ページ](https://dotnet.microsoft.com/download/dotnet/10.0)のインストーラーで入れます。macOS では、次の方法も使えます。**この命令は、Microsoft のサイトから取得したプログラムをすぐに実行します。**

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
```

入れたのに `dotnet` が見つからない場合は、使えるように設定します。

macOS:

```sh
export PATH="$HOME/.dotnet:$PATH"
```

Windows(PowerShell):

```powershell
$env:PATH = "$HOME\.dotnet;$env:PATH"
```

次に、`dotnet` の場所を調べて書き留めます。手順5で使います。

macOS:

```sh
which dotnet
```

Windows(PowerShell):

```powershell
(Get-Command dotnet).Source
```

## 3. 組み立てを確認する

```sh
cd "<guidgraph>"
```

```sh
dotnet build
```

エラーが表示されずに終われば、次に進みます。追加の設定は必要ありません。

テストを実行する場合は `dotnet test` を使います。実際の Unity プロジェクトを使うテストは、材料のファイルを含めていないため省かれます([fixtures.md](../development/fixtures.md))。

## 4. グラフを作る

グラフは、調べた結果のファイルです。

`--db` には、新しいファイル名か、このツールで作ったグラフを指定します。それ以外の中身があるファイルは変更されず、エラーで終了します。

先に調べる Unity プロジェクトを Unity で一度開くと、パッケージの中のファイルも調べられます。

```sh
dotnet run --project "<guidgraph>/src/Analyzer" -- analyze "<unity-project>" --db "<graph.db>"
```

最後に `Wrote <graph.db> (N refs)` と表示されたら、作成は終わりです。`N` は参照の数です。かかる時間は、プロジェクトの大きさとパソコンの性能で変わります。

Git を使うプロジェクトの中にグラフを置く場合は、`.gitignore`(Git の対象から外すファイルの一覧)に追加します。Unity に読ませない場合は、`Assets` の外に置きます。

## 5. Claude Code に登録する

「登録」は、Claude Code から GuidGraph を呼べるようにする設定です。場所はすべて絶対パス(ドライブ名や `/` から始まる、省略のない場所)で書きます。

調べる Unity プロジェクトのフォルダへ移動します。

```sh
cd "<unity-project>"
```

次の命令を1行で入力します。`<dotnet>` は手順2で書き留めた場所です。【要確認:Windows の PowerShell での動き】

```sh
claude mcp add guidgraph -- "<dotnet>" run --project "<guidgraph>/src/Analyzer" -- serve --db "<graph.db>" --project "<unity-project>"
```

`serve` の `--project` は省略できます。指定すると、グラフが古いかどうかが分かります([mcp-server.md](mcp-server.md) の「グラフの鮮度」)。

登録できたことは、次の命令で確認します。一覧に `guidgraph` が表示されます。

```sh
claude mcp list
```

### 登録の範囲を変える

指定しない場合は、自分のパソコンの、そのプロジェクトだけに登録します。範囲は `--scope` で変えます。

- `--scope user`:自分の全プロジェクトで使います。
- `--scope project`:プロジェクトの `.mcp.json` に書き込み、チームと共有します。**自分の利用者名を含む場所も共有されます。先にチームの了承を得てください。** 場所が各自のパソコンで同じでないと動きません。

```sh
claude mcp add --scope user guidgraph -- "<dotnet>" run --project "<guidgraph>/src/Analyzer" -- serve --db "<graph.db>" --project "<unity-project>"
```

登録を消す命令は次のとおりです。

```sh
claude mcp remove guidgraph
```

## 6. 動きを確認する

1. Unity プロジェクトのフォルダで `claude` を起動します。
2. `/mcp` と入力し、`guidgraph` がつながっていることを確認します。初回は組み立てが走るため、時間がかかります。時間切れになる場合は、下の「起動を速くする」を使います。
3. 次のように質問します。`Player` と `Fog.mat` は、自分のプロジェクトにある名前に置き換えます。

   ```text
   名前に Player を含むファイルを探して
   ```

   ```text
   Fog.mat を参照しているファイルは?
   ```

該当するファイルの一覧が回答されます。

## 7. 日常の使い方

- Unity のファイルを変更したら、手順4の命令をもう一度実行します。2回目からは、変更分だけを更新します。
- グラフは自動では新しくなりません。
- Claude Code の再起動は必要ありません。次の質問から新しい結果を使います。

## 起動を速くする(省略できます)

`dotnet run` は、起動のたびに組み立ての確認をします。先に実行用のファイルを作ると、起動が速くなります。この作業を publish と呼びます。

1. ソースのフォルダで、次を入力します。

   ```sh
   dotnet publish src/Analyzer -c Release -o publish
   ```

2. Unity プロジェクトのフォルダで、前の登録を消します。

   ```sh
   claude mcp remove guidgraph
   ```

3. 次のどちらか一方で登録し直します。Windows では、実行用のファイル名は `guidgraph.exe` です。

   .NET を配布ページのインストーラーで入れた場合:

   ```sh
   claude mcp add guidgraph -- "<guidgraph>/publish/guidgraph" serve --db "<graph.db>" --project "<unity-project>"
   ```

   macOS で、手順2の命令を使って `~/.dotnet` に入れた場合:

   ```sh
   claude mcp add guidgraph -e DOTNET_ROOT="$HOME/.dotnet" -- "<guidgraph>/publish/guidgraph" serve --db "<graph.db>" --project "<unity-project>"
   ```

ソースを更新(`git pull`)したら、手順1の publish をもう一度実行します。

## うまく動かないとき

| 症状 | 対処 |
|---|---|
| `/mcp` で `guidgraph` が失敗になる | 登録した命令の `--` より後ろを、そのまま入力します。失敗の理由が表示されます。理由が出ず、入力待ちに戻らなければ起動できています。Ctrl キーと C キーで止めます |
| `dotnet` が見つからない | 登録する `dotnet` を絶対パスにします(手順2) |
| 起動してすぐ終了する | `--db` の場所の誤りが考えられます。絶対パスで指定し直します |
| `not a readable guidgraph database` と表示される | `--db` に、このツールで作ったものではないファイルか、読み取れないファイル(壊れている、ほかのプログラムが使用中)を指定しています。場所を確認します。壊れたグラフを作り直す場合は、そのファイルを自分で削除してから実行します |
| ファイルが `not_found` になる | グラフが古いか、別のプロジェクトを指定しています。手順4を実行し直します |
| パッケージのファイルが「未解決」になる | 対象を Unity で一度開いてから、次を実行します。`dotnet run --project "<guidgraph>/src/Analyzer" -- analyze "<unity-project>" --db "<graph.db>" --full` |
| あるはずの参照が出ない | [limitations.md](limitations.md) を確認します |

直らない場合は、[Issues](https://github.com/murnana/unity-guidgraph/issues) で報告できます。報告は誰でも読めるため、仕事の情報は書かないでください。

---

> GuidGraph は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> GuidGraph is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
