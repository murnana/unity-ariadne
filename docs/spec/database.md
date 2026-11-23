# データベース定義一覧

GuidGraph が使う SQLite データベースの定義です。ユーザー向けの説明は `docs/manual/schema.md` にあります。ここでは、実装（`src/Graph/GraphDatabase.cs`）と、[new-features.md](new-features.md) で提案している追加分をまとめます。

- **現行**: 実装済みで、スキーマのバージョン（`PRAGMA user_version`）は 3 です。
- **提案**: 未実装です。実装時に定義を変える可能性があります。

## 1. graph.db

プロジェクトの依存グラフです。`analyze` が作り、`serve` / `export` / `report` / `search` が読み取り専用で開きます。

`PRAGMA user_version` が実装のバージョンと違う db は、増分更新せずに全再構築します。つまりスキーマ変更の影響は「次の `analyze` で作り直し」で済みますが、増分更新が全再構築と行単位で一致することを保つ必要があります。

### 1.1 一覧

| 名前 | 種類 | 状態 | 概要 |
|---|---|---|---|
| `assets` | テーブル | 現行 | アセット（GUID 単位）。未解決 GUID も含む |
| `objects` | テーブル | 現行 | YAML ドキュメント（fileID 単位） |
| `refs` | テーブル | 現行 | 参照（1 参照 = 1 行） |
| `code_symbols` | テーブル | 現行 | C# の宣言とドキュメントコメント |
| `settings_files` | テーブル | 現行 | 走査した ProjectSettings の変更検出用 |
| `asset_edges` | ビュー | 現行 | アセット単位に集約した参照 |
| `unresolved_guids` | ビュー | 現行 | 未解決の GUID |
| `placements` | テーブル | 提案 | prefab の配置数（N-8） |

### 1.2 `assets`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `guid` | TEXT | 一意、非 NULL | アセットの GUID |
| `path` | TEXT | | プロジェクト相対パス。未解決 GUID は NULL |
| `asset_type` | TEXT | 非 NULL | `Scene` / `Prefab` / `Script` / `ScriptableObject` / `Material` など |
| `source` | TEXT | 非 NULL | `Assets` / `Packages` / `PackageCache` / `ProjectSettings` / `BuiltIn` / `Unresolved` |
| `file_hash` | TEXT | | 変更検出用。`Unresolved` / `BuiltIn` / `ProjectSettings` は NULL |

### 1.3 `objects`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `asset_id` | INTEGER | 非 NULL、`assets(id)` | |
| `file_id` | INTEGER | 非 NULL | YAML の `&<fileID>` |
| `class_id` | INTEGER | 非 NULL | Unity のクラス ID（`!u!114` の 114 など） |
| `name` | TEXT | | `m_Name`（あれば） |
| `is_stripped` | INTEGER | 既定 0 | `stripped` サフィックス付きなら 1 |

一意制約: `(asset_id, file_id)`。インデックス: `idx_objects_asset(asset_id)`。

### 1.4 `refs`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `from_object_id` | INTEGER | 非 NULL、`objects(id)` | 参照元のオブジェクト |
| `to_guid` | TEXT | | 外部参照の GUID。NULL は同一ファイル内の参照 |
| `to_file_id` | INTEGER | 非 NULL | 参照先の fileID |
| `property_path` | TEXT | 非 NULL | `m_Script`、`m_Materials[0]`、`m_Modifications[3].target` など |
| `ref_kind` | TEXT | 非 NULL | `uses_script` / `source_prefab` / `prefab_instance` / `generic` など（一覧は `docs/manual/schema.md`） |

インデックス: `idx_refs_from_object(from_object_id)`、`idx_refs_to_guid(to_guid)`。

### 1.5 `code_symbols`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `asset_id` | INTEGER | 非 NULL、`assets(id)` | `.cs` のアセット |
| `symbol` | TEXT | 非 NULL | `Namespace.Type` または `Namespace.Type.Member` |
| `kind` | TEXT | 非 NULL | `class` / `struct` / `interface` / `enum` / `record` / `delegate` / `method` / `property` / `field` など |
| `doc` | TEXT | | XML doc コメント（`<summary>` と `<remarks>`）の平文。無ければ NULL |

インデックス: `idx_code_symbols_asset(asset_id)`。スキーマ v3 で追加されました。

### 1.6 `settings_files`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `path` | TEXT | 主キー | 走査した `ProjectSettings/*.asset` |
| `file_hash` | TEXT | 非 NULL | 変更検出用 |

参照を持たないファイルは `assets` に行ができないため、変更検出のためにこのテーブルで持ちます。

### 1.7 ビュー

**`asset_edges`**: アセット単位に集約した参照です。jsonl の出力と、`find_references` などの逆依存の問い合わせに使います。

| 列 | 説明 |
|---|---|
| `from_guid` | 参照元アセットの GUID |
| `to_guid` | 参照先の GUID（`to_guid` が NULL の同一ファイル参照は含まない） |
| `ref_kind` | 参照種別 |
| `ref_count` | 該当する `refs` の行数 |

**`unresolved_guids`**: `assets` のうち `path` が NULL の行の `guid` です。

### 1.8 `placements`（提案）

N-8 `expanded_count` 用です。「A が B を直接配置している数」を 1 行で持ちます。入れ子の乗算は保存せず、呼び出し時に計算します。

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `from_guid` | TEXT | 非 NULL | 配置する側（シーンまたは prefab）の GUID |
| `to_guid` | TEXT | 非 NULL | 配置される prefab の GUID |
| `instances` | INTEGER | 非 NULL | その配置元から直接配置されている PrefabInstance の数 |
| `removed_objects` | INTEGER | 非 NULL、既定 0 | `m_RemovedGameObjects` による削除数 |
| `removed_components` | INTEGER | 非 NULL、既定 0 | `m_RemovedComponents` による削除数 |

主キー: `(from_guid, to_guid)`。インデックス: `idx_placements_to(to_guid)`（逆方向のたどり用）。

**スキーマ変更の手順**: `SchemaVersion` を 4 に上げます。増分更新では、変更されたアセットの `placements` を、その `from_guid` の行ごと作り直します。全再構築と行単位で一致するように、差分テストへ追加します。

## 2. provenance.db（提案）

N-4 の鮮度の表示用です。graph.db と同じプロジェクト用フォルダーに置きます。バージョン管理システム（VCS）の識別情報を持ち、graph.db とはアセットの GUID（`assets.guid`）だけで結びつけます。graph.db のスキーマには影響しません。

`PRAGMA user_version` は別に管理し、初版を 1 とします。

### 2.1 一覧

| 名前 | 概要 |
|---|---|
| `snapshots` | 解析ごとの VCS 識別情報 |
| `asset_revisions` | アセットごとの最終変更リビジョン |
| `repo_aliases` | リポジトリ URL の変更や VCS 移行の対応表 |

### 2.2 `snapshots`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `analyzed_at` | TEXT | 非 NULL | 解析時刻（UTC、ISO 8601） |
| `vcs_kind` | TEXT | 非 NULL | `git` / `svn` / `none` など |
| `repo_url` | TEXT | | リポジトリ URL。`none` なら NULL |
| `revision` | TEXT | | Git は SHA、Subversion はリビジョン番号。`none` なら NULL |
| `dirty` | INTEGER | 非 NULL、既定 0 | 解析時に、未コミットの変更があれば 1 |

値はすべて文字列で保存し、VCS 固有の型を持ち込みません。インデックス: `idx_snapshots_analyzed(analyzed_at)`。

### 2.3 `asset_revisions`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `snapshot_id` | INTEGER | 非 NULL、`snapshots(id)` | |
| `asset_guid` | TEXT | 非 NULL | アセットの GUID。パスは持たない |
| `revision` | TEXT | | そのアセットを最後に変更したリビジョン |
| `file_hash` | TEXT | | 解析時のファイルのハッシュ（graph.db の `assets.file_hash` と同じ値） |

主キー: `(snapshot_id, asset_guid)`。

### 2.4 `repo_aliases`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `old_repo_url` | TEXT | 非 NULL | 旧 URL |
| `new_repo_url` | TEXT | 非 NULL | 新 URL |
| `note` | TEXT | | 理由（移行、改名など） |

主キー: `(old_repo_url, new_repo_url)`。判定時に、`repo_url` をこの表で読み替えます。

## 3. 関係図

```
graph.db                                   provenance.db
assets.guid  <------ (GUID で対応) ------  asset_revisions.asset_guid
  |                                          |
  +-- objects -- refs                        +-- snapshots
  +-- code_symbols                           +-- repo_aliases
placements.from_guid / to_guid -> assets.guid
```

## 4. 更新と互換性のまとめ

| 変更 | 影響 |
|---|---|
| `placements` の追加（graph.db） | `SchemaVersion` 3 → 4。古い db は次の `analyze` で全再構築 |
| provenance.db の追加 | graph.db への影響なし。無ければ更新時刻で判定する |
