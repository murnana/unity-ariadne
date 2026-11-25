# memory.jsonl フォーマット

この文書は、開発の知識がある人向けです。

`export --db graph.db --jsonl out.jsonl` は、グラフをアセット単位に集約して JSONL に書き出します。フォーマットは MCP memory サーバー([`@modelcontextprotocol/server-memory`](https://github.com/modelcontextprotocol/servers/tree/main/src/memory))互換で、実際に読み込ませて動作確認済みです。

## ファイル全体の構造

- 1 行 = 1 JSON オブジェクトです。**entity 行が全部先、relation 行が後**で、それぞれソート済みです — 同じグラフからは常にバイト単位で同一のファイルが出ます(差分比較に使えます)。
- エンコーディングは UTF-8(BOM なし)、改行は `\n` です(memory サーバーが `\n` で分割するためです)。
- 非 ASCII のパス(キリル文字など)はエスケープせずそのまま書かれます。

## entity 行

```json
{"type":"entity","name":"Assets/Materials/Fog.mat","entityType":"Material","observations":["guid: 31f8b7a6...","source: Assets","outgoing: 3 refs to 2 assets","incoming: 6 refs from 6 assets"]}
```

| フィールド | 内容 |
|---|---|
| `name` | アセットのプロジェクト相対パス。**未解決 GUID の場合は GUID そのもの** |
| `entityType` | `assets.asset_type`(Material / Prefab / Scene / ...) |
| `observations` | `guid: ...` / `source: ...` / 未解決なら dangling reference の説明 / `outgoing: N refs to M assets` / `incoming: ...` / C# スクリプトなら宣言している型ごとに `declares class Game.Enemy: <XML ドキュメントコメントの要約>`(コメントが無ければ `: ...` なし。メンバーのコメントは含みません) |

## relation 行

```json
{"type":"relation","from":"Assets/Scenes/Demo.unity","to":"Assets/Materials/Fog.mat","relationType":"generic","ref_count":4}
```

`relationType` は ref_kind です([schema.md](schema.md) の一覧参照)。`ref_count` は memory サーバーの標準にない独自フィールドですが、**サーバーは未知フィールドをそのまま保持して往復させる**ため安全です。

## 何が出力され、何が出力されないか

| 対象 | 指定しない場合 | `--include-packages` |
|---|---|---|
| Assets / ProjectSettings 由来のノードとエッジ | 出力します | 出力します |
| PackageCache アセット | 参照されているターゲットとしてのみ | 全ノード + PackageCache 発のエッジも |
| BuiltIn / 未解決 GUID(参照されているもの) | 出力します | 出力します |
| `corresponding_source` エッジ | 出力しません(SQLite にのみ残ります) | 出力しません |
| エクスポート対象エッジを 1 本も持たないフォルダアセット | 出力しません | 出力しません |

参考値(Assets/ 以下 約 1,000 アセットの URP プロジェクトを PackageCache 込みで analyze した場合): デフォルト 1,280 entities / 3,514 relations、`--include-packages` で 12,811 / 16,606 になります。

## memory サーバーへの読み込み

始める前に確認してください。

- Node.js が必要です。`npx` は Node.js に含まれる命令です。
- 次の命令は、外部のプログラムを確認なしで取得して実行します。会社のパソコンでは、先に管理者の許可を得てください。
- AI のツールにつなぐと、内容が AI サービスへ送られます。仕事のプロジェクトでは、先に責任者の許可を得てください。
- memory サーバーは、指定したファイルを書き換える場合があります。書き出したファイルのコピーを指定してください。`export` で同じ名前に書き出し直すと、追加した記録は失われます。

ファイルの場所は絶対パスで指定します。

macOS:

```sh
MEMORY_FILE_PATH="/path/to/memory-copy.jsonl" npx -y @modelcontextprotocol/server-memory
```

Windows(PowerShell):

```powershell
$env:MEMORY_FILE_PATH = "C:\path\to\memory-copy.jsonl"
```

```powershell
npx -y @modelcontextprotocol/server-memory
```

この命令は接続を待ち続けます。Ctrl キーを押しながら C キーを押すと止まります。AI のツールから使うには、そのツールの方法で、この命令を MCP サーバーとして登録します。ここでの `search_nodes` は memory サーバーのもので、GuidGraph の同じ名前のツールとは別です。

読み込み後は `search_nodes` / `open_nodes` などの memory サーバー標準ツールで参照関係を照会できます。ライブで最新のグラフを引きたい場合は、jsonl 経由ではなく本ツール自身の MCP サーバー([mcp-server.md](mcp-server.md))をお使いいただくほうがよいでしょう(`analyze` の再実行が即反映されます)。

---

> GuidGraph は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> GuidGraph is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
