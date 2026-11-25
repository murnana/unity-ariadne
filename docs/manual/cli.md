# CLI リファレンス

命令を入力して GuidGraph を使う方法です。開発の知識がある人向けです。初めて使う場合は入門マニュアル([Windows 版](getting-started-windows.md) / [macOS 版](getting-started-macos.md))を読んでください。

## セットアップ

### 必要なもの

- インストーラーで入れた `guidgraph`、またはソースと **.NET 10 SDK** が必要です。ソースの取得と SDK の準備は [claude-code-setup.md](claude-code-setup.md) の手順1と2を見てください。
- 解析対象は **Assets/ ディレクトリを持つ Unity プロジェクト**である必要があります(なければ `analyze` はエラーで終了します)。
- Unity 側の Asset Serialization は **Force Text** を推奨します。Force Text でないアセットの中身は調べられません([limitations.md](limitations.md) 参照)。
- パッケージ由来の GUID(URP、TextMesh Pro など)まで解決したい場合は、対象プロジェクトを一度 Unity で開いて `Library/PackageCache` を生成しておいてください。なくても動作しますが、パッケージ GUID が Unresolved 扱いになります。

### 命令の実行のしかた

この文書の例は、インストーラーで入れた `guidgraph` を使います。

ソースから使う場合は、ソースのフォルダに移動し、例の `guidgraph` を `dotnet run --project src/Analyzer --` に置き換えます。

```sh
dotnet run --project src/Analyzer -- analyze "<Unity プロジェクト>" --db graph.db
```

`<` と `>` で囲んだ部分は、記号ごと自分の場所に置き換えます。`[ ]` の中は省略できます。`|` は、どちらか一方を指定する意味です。どちらの記号も入力しません。

## コマンド一覧

```
analyze <unity-project-path> [--db <path>] [--full]
report  --db <path>
export  --db <path> --jsonl <path> [--include-packages]
serve   --db <path> [--project <unity-project-path>]
search  --db <path> (--query <json> | --query-file <path or ->)
```

---

## analyze — グラフの構築・更新

Unity プロジェクトを調べて、参照関係のグラフを SQLite のファイルに書き込みます。

**`--db` には、新しいファイル名か、このツールで作ったグラフを指定します。ほかのファイルを指定すると、そのファイルは確認なしで削除され、中身が失われます。**

```sh
guidgraph analyze "<Unity プロジェクト>" --db graph.db
```

| オプション | 意味 |
|---|---|
| `--db <path>` | 出力先の SQLite ファイル。**省略すると、ファイルを作らずに結果の要約だけを表示します**(試しに実行するときに使います) |
| `--full` | 既存 DB があっても差分更新せず、削除してフル再構築します |

### 差分更新(インクリメンタル更新)

`--db` に**既存のデータベース**を指定すると、自動的に差分更新になります:

- ファイルごとのコンテンツハッシュ(SHA-256)で追加 / 変更 / 移動 / 削除を検出し、**変更のあったファイルだけ再解析**します。
- ハッシュは「解析が実際に読むバイト列」だけを対象にします。テクスチャなどバイナリ本体の編集はハッシュを変えません(.meta しか参照を持たないため、これは正しい挙動です)。
- **C# の参照(コード層)は、ファイル単位では更新しません**。型の判定に、プロジェクト全体の情報を使うためです。
  - .cs が1つでも追加、削除、変更されると、全体を調べ直します。
  - ファイルの追加、削除、名前の変更があった場合も同じです。
  - 調べ直すと `Code layer: re-analyzed (global resolution)` と表示されます。
- ProjectSettings は変更があれば全体を再解析します(約 10 ファイルなので安価です)。
- 差分更新と全体の作り直しで、結果が同じになることをテストで確認しています。
- 古い形式のグラフや、SQLite でないファイルを指定した場合は、そのファイルを削除して作り直します。確認はありません。

参考値(テスト用のプロジェクト、14,559 GUID、PackageCache を含む)は次のとおりです。全体の作成が約8秒、変更なしの更新が約2.4秒、C# の調べ直しがある更新が約4秒です。時間はプロジェクトとパソコンで変わります。

### 出力の読み方

```
GUIDs: 14559                      ← GUID 辞書の総数(source 別内訳が続く)
Asset types: ...                  ← アセット種別の内訳
YAML assets: 368 files, 11946 objects
References: ... (external, same-file; null refs skipped)
  generic: 23142                  ← ref_kind 別の参照数(schema.md 参照)
  ...
Meta references (meta_external): ...
JSON asset references (embedded_json): ...
Code references: ... from N of M scripts
  skipped: ... ambiguous type names, ... non-literal Resources.Load
                                  ← 解決できず意図的にスキップした参照(limitations.md)
Code symbols: 5210 (1834 with XML doc comments)
                                  ← キーワード検索用に記録した C# の型と、XML ドキュメントコメント付きのメンバー(schema.md の code_symbols)
ProjectSettings references: ...
Unresolved GUIDs: 83 (details: report --db graph.db)
Wrote graph.db (86729 refs)
```

`Unresolved GUIDs` は「参照されているが該当アセットが存在しない GUID」を指します。削除済みアセットへの参照切れ、または PackageCache 未生成によるパッケージ GUID 未解決のいずれかです。詳細は `report` で確認してください。まず対象を Unity で一度開き、`analyze` に `--full` を付けて実行します。残ったものは、表示される参照元を Unity で確認します。

---

## report — 参照切れ GUID の一覧

```sh
guidgraph report --db graph.db
```

未解決 GUID を参照数の多い順に、参照元アセットのサンプルパス付きで列挙します。プロジェクトの参照切れを調べるときに使います。

```
Unresolved GUIDs: 83
  299147e0b236560478d85010e9e1c387  31 refs from 17 assets
    Assets/Effects/...vfx
    ...
```

---

## export — memory.jsonl の書き出し

**`--jsonl` には新しいファイル名を指定します。同じ名前のファイルがあると、確認なしで上書きします。**

```sh
guidgraph export --db graph.db --jsonl out.jsonl
```

グラフをアセット単位に集約し、MCP memory サーバー(`@modelcontextprotocol/server-memory`)互換の JSONL に書き出します。フォーマットの詳細は [memory-jsonl.md](memory-jsonl.md) をご覧ください。

| オプション | 意味 |
|---|---|
| `--jsonl <path>` | 出力先(必須) |
| `--include-packages` | PackageCache 由来のノードとエッジも全部出力します。指定しない場合、PackageCache アセットは「Assets 側から参照されているターゲット」としてのみ現れます |

---

## serve — MCP サーバー(stdio)

AI のツールにつなぐと、調べた結果(ファイル名、コード内の説明文など)が AI サービスへ送られます。仕事のプロジェクトでは、先に責任者の許可を得てください。

```sh
guidgraph serve --db graph.db --project "<Unity プロジェクト>"
```

`serve` は接続を待ち続ける命令です。画面が止まって見えても正常です。Ctrl キーを押しながら C キーを押すと止まります。Claude Code に登録した場合は、Claude Code が自動で実行します。

グラフ DB を MCP(stdio)で公開します。`--project` は省略できます。指定すると、各応答の `meta` に、グラフが古いかどうか(`stale`)が載ります([mcp-server.md](mcp-server.md) の「グラフの鮮度」を参照)。起動時に DB を 1 回開いてパスを検証し、以降は**ツール呼び出しごとに読み取り専用で開き直す**ため、サーバーを起動したまま別ターミナルで `analyze` を実行すればグラフの更新が即座に反映されます。再起動は不要です。

セットアップとツールの詳細は [mcp-server.md](mcp-server.md) をご覧ください。

---

## search — オントロジー検索

条件を JSON で書いて指定します。PowerShell では、次の内容を `query.json` に UTF-8 で保存して使います。

```json
{"from": {"types": ["Prefab"], "source": "Assets"}, "unreferenced": true}
```

```sh
guidgraph search --db graph.db --query-file query.json
```

macOS では、命令に直接書くこともできます。

```sh
guidgraph search --db graph.db --query '{"from": {"keywords": ["ボス", "boss"], "types": ["Script"]}, "steps": [{"direction": "in", "kinds": ["uses_script"], "types": ["Prefab"]}]}'
```

上の `query.json` の例は、どこからも参照されていないプレハブを探します。結果に出ても、削除してよいとは限りません([limitations.md](limitations.md))。

MCP の `search` ツールと**同じ機能**を CLI から使います。クエリは MCP ツールの引数と同じ形の JSON(`from` / `steps` / `unreferenced` / `limit` / `relax`)で、出力も同じ JSON(人が読みやすいよう字下げ付き、UTF-8)です。クエリと結果の詳細は [mcp-server.md の search](mcp-server.md#search--オントロジー検索種類関係名前意味) をご覧ください。

- Windows の PowerShell などで引数の引用符が扱いにくい場合は、クエリを UTF-8 のファイルに書いて `--query-file` で渡してください(`-` で標準入力)
- 説明文は英語です。アセット名やドキュメントコメントは、日本語・韓国語・中国語などの文字のまま出力します
- キーワードは全角・半角、ひらがな・カタカナ、大文字・小文字の違いを無視して照合します
- クエリが不正なとき(`error` を含む JSON が出たとき)は終了コード 1 になります

---

## 典型的なワークフロー

1. グラフを作ります。

   ```sh
   guidgraph analyze "<Unity プロジェクト>" --db graph.db
   ```

2. 参照切れを確認します。

   ```sh
   guidgraph report --db graph.db
   ```

3. 使い方に合わせて、次のどちらか一方を実行します。

   AI のツールから調べる場合:

   ```sh
   guidgraph serve --db graph.db
   ```

   memory サーバー用のファイルを書き出す場合:

   ```sh
   guidgraph export --db graph.db --jsonl memory.jsonl
   ```

4. プロジェクトを編集したら、手順1の命令をもう一度実行します。変更分だけを更新します。

---

> GuidGraph は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> GuidGraph is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
