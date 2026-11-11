# CLI リファレンス

## セットアップ

### 必要なもの

- **.NET 10 SDK** が必要です。`dotnet` が PATH にない場合:

  ```sh
  export PATH="$HOME/.dotnet:$PATH"
  ```

- 解析対象は **Assets/ ディレクトリを持つ Unity プロジェクト**である必要があります(なければ `analyze` はエラーで終了します)。
- Unity 側の Asset Serialization は **Force Text** を推奨します。バイナリシリアライズされたアセットの中身は解析できません([limitations.md](limitations.md) 参照)。
- パッケージ由来の GUID(URP、TextMesh Pro など)まで解決したい場合は、対象プロジェクトを一度 Unity で開いて `Library/PackageCache` を生成しておいてください。なくても動作しますが、パッケージ GUID が Unresolved 扱いになります。

### ビルドと実行

```sh
dotnet build          # ビルドのみ
dotnet test           # テスト実行
dotnet run --project src/Analyzer -- <command> ...
```

以降のコマンド例では `dotnet run --project src/Analyzer --` を省略して記載します。

## コマンド一覧

```
analyze <unity-project-path> [--db <path>] [--full]
report  --db <path>
export  --db <path> --jsonl <path> [--include-packages]
serve   --db <path> [--project <unity-project-path>]
```

---

## analyze — グラフの構築・更新

```sh
analyze <unity-project-path> --db graph.db
```

Unity プロジェクトを 1 パスで解析し、依存関係グラフを SQLite に書き込みます。

| オプション | 意味 |
|---|---|
| `--db <path>` | 出力先の SQLite ファイル。**省略すると in-memory で解析してサマリーだけ表示します**(ファイルは作られません。ドライラン用途) |
| `--full` | 既存 DB があっても差分更新せず、削除してフル再構築します |

### 差分更新(インクリメンタル更新)

`--db` に**既存のデータベース**を指定すると、自動的に差分更新になります:

- ファイルごとのコンテンツハッシュ(SHA-256)で追加 / 変更 / 移動 / 削除を検出し、**変更のあったファイルだけ再解析**します。
- ハッシュは「解析が実際に読むバイト列」だけを対象にします。テクスチャなどバイナリ本体の編集はハッシュを変えません(.meta しか参照を持たないため、これは正しい挙動です)。
- **コード層(C# 由来の参照)はファイル単位では更新されません**。型解決がプロジェクト全体の宣言型インデックスに依存するため、.cs が 1 つでも増減・変更されるか、アセットパス集合が変わる(何かの追加 / 削除 / リネーム)と、コード層全体を再解析します。出力の `Code layer: re-analyzed (global resolution)` がその印です。
- ProjectSettings は変更があれば全体を再解析します(約 10 ファイルなので安価です)。
- **更新結果はフル再構築と行単位で一致する**ことがテストで保証されています。結果を信用してかまいません。
- スキーマバージョンが異なる古い DB や SQLite でないファイルを渡した場合は、黙ってフル再構築にフォールバックします。

目安(フィクスチャ: 14,559 GUID、PackageCache 込み): フル構築 約 8 秒 → 変更なし更新 約 2.4 秒、コード層再解析あり 約 4 秒です。

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
ProjectSettings references: ...
Unresolved GUIDs: 83 (details: report --db graph.db)
Wrote graph.db (86729 refs)
```

`Unresolved GUIDs` は「参照されているが該当アセットが存在しない GUID」を指します。削除済みアセットへの参照切れ、または PackageCache 未生成によるパッケージ GUID 未解決のいずれかです。詳細は `report` でご確認ください。

---

## report — 参照切れ GUID の一覧

```sh
report --db graph.db
```

未解決 GUID を参照数の多い順に、参照元アセットのサンプルパス付きで列挙します。プロジェクトの参照切れ(dangling reference)の棚卸しにお使いください。

```
Unresolved GUIDs: 83
  299147e0b236560478d85010e9e1c387  31 refs from 17 assets
    Assets/Effects/...vfx
    ...
```

---

## export — memory.jsonl の書き出し

```sh
export --db graph.db --jsonl out.jsonl [--include-packages]
```

グラフをアセット単位に集約し、MCP memory サーバー(`@modelcontextprotocol/server-memory`)互換の JSONL に書き出します。フォーマットの詳細は [memory-jsonl.md](memory-jsonl.md) をご覧ください。

| オプション | 意味 |
|---|---|
| `--jsonl <path>` | 出力先(必須) |
| `--include-packages` | PackageCache 由来のノードとエッジも全部出力します。デフォルトでは PackageCache アセットは「Assets 側から参照されているターゲット」としてのみ現れます |

---

## serve — MCP サーバー(stdio)

```sh
serve --db graph.db
serve --db graph.db --project <unity-project-path>
```

グラフ DB を MCP(stdio)で公開します。起動時に DB を 1 回開いてパスを検証し、以降は**ツール呼び出しごとに読み取り専用で開き直す**ため、サーバーを起動したまま別ターミナルで `analyze` を実行すればグラフの更新が即座に反映されます。再起動は不要です。

| オプション | 説明 |
|---|---|
| `--db <path>` | 公開するグラフ DB(必須) |
| `--project <path>` | `--db` のファイルが**存在しないときだけ**、このプロジェクトを `analyze` してから起動します。既存の DB は再解析せずそのまま使います(更新は `analyze` を再実行してください)。解析のログは stderr に出力され、stdio の MCP 通信には影響しません。`Assets/` が無いディレクトリを指定するとエラー終了します |

セットアップとツールの詳細は [mcp-server.md](mcp-server.md) をご覧ください。

---

## 典型的なワークフロー

```sh
# 1. 初回: グラフを構築
analyze ~/MyUnityProject --db graph.db

# 2. 参照切れを確認
report --db graph.db

# 3. AI エージェントに公開(どちらか)
serve --db graph.db                          # MCP サーバー
export --db graph.db --jsonl memory.jsonl    # memory サーバー用 JSONL

# 4. プロジェクトを編集したら差分更新(数秒)
analyze ~/MyUnityProject --db graph.db
```

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
