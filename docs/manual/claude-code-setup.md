# Claude Code セットアップガイド

このリポジトリをチェックアウトした直後から、**自分の Unity プロジェクトを Claude Code で照会できるようになるまで**の手順をご紹介します。個々のコマンドの詳細は [cli.md](cli.md)、MCP ツールの仕様は [mcp-server.md](mcp-server.md) をご参照ください。

登場するパスは 3 つあります。混同しないようご注意ください:

| 記号 | 意味 | 例 |
|---|---|---|
| `<guidgraph>` | このリポジトリのチェックアウト先 | `~/Projects/guidgraph` |
| `<unity-project>` | 解析対象の Unity プロジェクト(`Assets/` を持つディレクトリ) | `~/Projects/MyGame` |
| `<graph.db>` | 解析結果の SQLite ファイル。置き場所は自由です | `~/Projects/MyGame/graph.db` |

## 1. .NET 10 SDK を用意する

このツールは .NET 10(LTS)でビルド・実行します。まず確認してください:

```sh
dotnet --version   # 10.x ならOK
```

`dotnet` コマンドが見つからない場合:

- インストール済みで PATH にないだけであれば(公式インストールスクリプトの既定は `~/.dotnet` です):

  ```sh
  export PATH="$HOME/.dotnet:$PATH"
  ```

- 未インストールの場合は [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) のインストーラーか、公式スクリプトをお使いください:

  ```sh
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
  export PATH="$HOME/.dotnet:$PATH"
  ```

**この時点で `which dotnet` の結果を控えておいてください**(例: `/Users/you/.dotnet/dotnet`)。後の MCP 登録では Claude Code がサーバープロセスを起動するため、シェルの PATH 設定に頼らず `dotnet` の絶対パスで登録するのが確実です。

## 2. ビルドを確認する

```sh
cd <guidgraph>
dotnet build
```

これだけで動きます。追加のセットアップ(NuGet の手動復元、グローバルツールのインストール等)は不要です。動作確認したい場合は `dotnet test` をお使いください(フィクスチャ同梱、数十秒。PackageCache 依存の一部テストは環境によりスキップされますが正常です)。

## 3. グラフ DB を構築する

解析対象の Unity プロジェクトに対して `analyze` を 1 回実行します:

```sh
dotnet run --project <guidgraph>/src/Analyzer -- analyze <unity-project> --db <graph.db>
```

- `<graph.db>` には、新しいファイル名か、このツールで作った DB を指定してください。それ以外の中身があるファイルを指定すると、ファイルは変更されず、エラーで終了します。
- 所要時間の目安は中規模プロジェクトで数秒〜十数秒です。最後に `Wrote <graph.db> (N refs)` と出れば成功です。
- **パッケージ由来の参照(URP、TextMesh Pro 等)まで解決したい場合**は、先に対象プロジェクトを一度 Unity で開いて `Library/PackageCache` を生成しておいてください。なくても動作しますが、パッケージ GUID が Unresolved 扱いになります。
- `<graph.db>` を Unity プロジェクト内に置く場合は、`.gitignore` への追加をお忘れなく(`graph.db` は再生成可能な成果物です)。Unity に読ませたくない場合は `Assets/` の外に置いてください。

## 4. Claude Code に MCP サーバーを登録する

**Unity プロジェクト側のディレクトリで**登録してください(Claude Code の MCP 登録は既定でカレントプロジェクトに紐づくためです):

```sh
cd <unity-project>
claude mcp add guidgraph -- \
  <dotnetの絶対パス> run --project <guidgraph>/src/Analyzer -- serve --db <graph.dbの絶対パス>
```

具体例:

```sh
cd ~/Projects/MyGame
claude mcp add guidgraph -- \
  ~/.dotnet/dotnet run --project ~/Projects/guidgraph/src/Analyzer -- serve --db ~/Projects/MyGame/graph.db
```

- **パスはすべて絶対パスで書いてください。** Claude Code が起動するサーバープロセスの作業ディレクトリは登録時のシェルと同じとは限りません。
- スコープを変えたい場合は `--scope` を付けてください: `--scope project` はリポジトリの `.mcp.json` に書き込まれチームで共有できます(ただしパスが各自のマシンで一致している必要があります)。`--scope user` は全プロジェクト共通です。既定(`local`)は自分のマシンのそのプロジェクトのみです。
- 登録内容の確認・削除: `claude mcp list` / `claude mcp remove guidgraph` をお使いください。

## 5. 動作確認

Unity プロジェクトのディレクトリで Claude Code を起動し、次の手順で確認します:

1. `/mcp` で `guidgraph` が connected になっていることを確認してください。初回接続は `dotnet run` がビルドを走らせるため数秒〜数十秒かかることがあります(タイムアウトした場合は下の「publish して登録する」をご参照ください)。
2. 試しに聞いてみてください:

   > search_nodes で "Player" を含むアセットを探して

   > Assets/Materials/Fog.mat を参照しているアセットは?(find_references)

使える 8 ツール(`find_references` / `impact_analysis` / `get_neighbors` / `search_nodes` / `list_unresolved` / `search` / `describe_ontology` / `list_folders`)と活用パターンは [mcp-server.md](mcp-server.md) をご覧ください。

## 6. 日常の運用

- **Unity プロジェクトを編集したら `analyze` を再実行するだけです**(手順 3 と同じコマンド)。既存 DB は差分更新され数秒で終わります。
- **MCP サーバーの再起動は不要です。** ツール呼び出しごとに DB を開き直すため、`analyze` の結果は次のツール呼び出しから反映されます。
- グラフの鮮度は自動では追従しません。「最近の変更が反映されていない気がする」と思ったら `analyze` を実行してください。

## 任意: publish して登録する(起動を速くする)

`dotnet run` は起動のたびにビルドチェックが走ります。頻繁に使う場合は一度発行してバイナリを直接登録するとサーバー起動が速くなります:

```sh
cd <guidgraph>
dotnet publish src/Analyzer -c Release -o publish
```

```sh
cd <unity-project>
claude mcp add guidgraph -- \
  <guidgraph>/publish/guidgraph serve --db <graph.dbの絶対パス>
```

この場合、リポジトリを更新(git pull)したら `dotnet publish` を再実行してください。

なお発行したバイナリも実行時に .NET ランタイムを探します。インストールスクリプトで `~/.dotnet` にだけ入れた環境(システム標準の場所に .NET がない環境)では、登録時に環境変数で場所を教えてください:

```sh
claude mcp add guidgraph -e DOTNET_ROOT="$HOME/.dotnet" -- \
  <guidgraph>/publish/guidgraph serve --db <graph.dbの絶対パス>
```

## トラブルシューティング

| 症状 | 原因と対処 |
|---|---|
| `/mcp` で guidgraph が failed | まず登録したコマンドをそのままターミナルで実行してみてください(stderr にエラーが出ます)。大半は次のどれかです ↓ |
| `dotnet: command not found` | 登録コマンドの `dotnet` を絶対パスにしてください(手順 1・4) |
| 起動直後にエラー終了 | `--db` のパス誤りです。サーバーは起動時に DB パスを検証して即エラーになる仕様です。絶対パスで指定し直してください |
| `analyze` が `not a readable guidgraph database` で終了する | `--db` に、このツールで作ったものではないファイル、または読み取れないファイル(壊れた DB、他のプロセスがロック中の DB)を指定しています。パスを確認してください。壊れた DB を作り直す場合は、そのファイルを自分で削除してから実行します |
| アセットが `not_found` になる | `analyze` を実行し忘れて DB が古い、または対象プロジェクトが違います。`search_nodes` で部分一致検索して DB の中身を確かめてください |
| パッケージのアセットが Unresolved | `Library/PackageCache` 未生成が原因です。対象プロジェクトを一度 Unity で開いてから `analyze --full` を実行してください |
| 参照が「あるはずなのに出ない」 | 仕様上グラフに出ないものがあります。[limitations.md](limitations.md) をご参照ください |

---

> GuidGraph は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> GuidGraph is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
