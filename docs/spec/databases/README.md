# データベース定義

GuidGraph が使う SQLite データベースの定義です。ユーザー向けの説明は `docs/manual/schema.md` にあります。ここでは、実装（`src/Graph/GraphDatabase.cs`）に合わせてまとめます。

| データベース | 状態 | 役割 | 定義 |
|---|---|---|---|
| graph.db | 現行（v4） | プロジェクトの依存グラフ | [graph-db.md](graph-db.md) |

N-4（鮮度の表示）は、graph.db ファイルの更新時刻を使うので、別のデータベースは持ちません。VCS の識別情報を持つ案（provenance.db）は、今回は見送りました。

## 更新と互換性のまとめ

| 変更 | 影響 |
|---|---|
| `placements` と `placement_removals` の追加（graph.db） | `SchemaVersion` 3 → 4。古い db は次の `analyze` で全再構築 |
