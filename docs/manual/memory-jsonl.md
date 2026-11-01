# memory.jsonl フォーマット

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
| `observations` | `guid: ...` / `source: ...` / 未解決なら dangling reference の説明 / `outgoing: N refs to M assets` / `incoming: ...` |

## relation 行

```json
{"type":"relation","from":"Assets/Scenes/Demo.unity","to":"Assets/Materials/Fog.mat","relationType":"generic","ref_count":4}
```

`relationType` は ref_kind です([schema.md](schema.md) の一覧参照)。`ref_count` は memory サーバーの標準にない独自フィールドですが、**サーバーは未知フィールドをそのまま保持して往復させる**ため安全です。

## 何が出力され、何が出力されないか

| 対象 | デフォルト | `--include-packages` |
|---|---|---|
| Assets / ProjectSettings 由来のノードとエッジ | ✅ | ✅ |
| PackageCache アセット | 参照されているターゲットとしてのみ | 全ノード + PackageCache 発のエッジも |
| BuiltIn / 未解決 GUID(参照されているもの) | ✅ | ✅ |
| `corresponding_source` エッジ | ❌(SQLite にのみ残る) | ❌ |
| エクスポート対象エッジを 1 本も持たないフォルダアセット | ❌ | ❌ |

参考値(Assets/ 以下 約 1,000 アセットの URP プロジェクトを PackageCache 込みで analyze した場合): デフォルト 1,280 entities / 3,514 relations、`--include-packages` で 12,811 / 16,606 になります。

## memory サーバーへの読み込み

```sh
# 出力ファイルをそのまま memory サーバーの保存先に指定する
npx -y @modelcontextprotocol/server-memory
# 環境変数 MEMORY_FILE_PATH=out.jsonl を指定して起動
```

読み込み後は `search_nodes` / `open_nodes` などの memory サーバー標準ツールで参照関係を照会できます。ライブで最新のグラフを引きたい場合は、jsonl 経由ではなく本ツール自身の MCP サーバー([mcp-server.md](mcp-server.md))をお使いいただくほうがよいでしょう(`analyze` の再実行が即反映されます)。

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
