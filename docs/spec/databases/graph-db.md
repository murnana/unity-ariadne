# graph.db

プロジェクトの依存グラフです。`analyze` が作り、`serve` / `export` / `report` / `search` が読み取り専用で開きます。

`PRAGMA user_version` が実装のバージョンと違う db は、増分更新せずに全再構築します。つまりスキーマ変更の影響は「次の `analyze` で作り直し」で済みますが、増分更新が全再構築と行単位で一致することを保つ必要があります。

## リレーション図

```mermaid
erDiagram
    assets ||--o{ objects : "asset_id"
    objects ||--o{ refs : "from_object_id"
    assets ||--o{ code_symbols : "asset_id"
    assets ||--o{ placements : "from_guid（提案）"
    assets ||--o{ placements : "to_guid（提案）"
    assets {
        INTEGER id PK
        TEXT guid UK
        TEXT path
        TEXT asset_type
        TEXT source
        TEXT file_hash
    }
    objects {
        INTEGER id PK
        INTEGER asset_id FK
        INTEGER file_id
        INTEGER class_id
        TEXT name
        INTEGER is_stripped
    }
    refs {
        INTEGER id PK
        INTEGER from_object_id FK
        TEXT to_guid "assets.guid（論理参照）"
        INTEGER to_file_id
        TEXT property_path
        TEXT ref_kind
    }
    code_symbols {
        INTEGER asset_id FK
        TEXT symbol
        TEXT kind
        TEXT doc
    }
    settings_files {
        TEXT path PK
        TEXT file_hash
    }
    placements {
        TEXT from_guid PK
        TEXT to_guid PK
        INTEGER instances
        INTEGER removed_objects
        INTEGER removed_components
    }
```

- `refs.to_guid` は `assets.guid` を指しますが、外部キー制約は付けていません（NULL は同一ファイル内の参照）。未解決の GUID も `assets` に行を持つため、参照先は必ず見つかります。
- `placements`（提案）は、`from_guid` と `to_guid` のどちらも `assets.guid` を指します。
- `settings_files` は他のテーブルと関係を持たず、変更検出にだけ使います。
- ビュー `asset_edges` は `refs` / `objects` / `assets` を結合して集約し、`unresolved_guids` は `assets` から導きます。

## 一覧

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

## `assets`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `guid` | TEXT | 一意、非 NULL | アセットの GUID |
| `path` | TEXT | | プロジェクト相対パス。未解決 GUID は NULL |
| `asset_type` | TEXT | 非 NULL | `Scene` / `Prefab` / `Script` / `ScriptableObject` / `Material` など |
| `source` | TEXT | 非 NULL | `Assets` / `Packages` / `PackageCache` / `ProjectSettings` / `BuiltIn` / `Unresolved` |
| `file_hash` | TEXT | | 変更検出用。`Unresolved` / `BuiltIn` / `ProjectSettings` は NULL |

## `objects`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `asset_id` | INTEGER | 非 NULL、`assets(id)` | |
| `file_id` | INTEGER | 非 NULL | YAML の `&<fileID>` |
| `class_id` | INTEGER | 非 NULL | Unity のクラス ID（`!u!114` の 114 など） |
| `name` | TEXT | | `m_Name`（あれば） |
| `is_stripped` | INTEGER | 既定 0 | `stripped` サフィックス付きなら 1 |

一意制約: `(asset_id, file_id)`。インデックス: `idx_objects_asset(asset_id)`。

## `refs`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | INTEGER | 主キー | |
| `from_object_id` | INTEGER | 非 NULL、`objects(id)` | 参照元のオブジェクト |
| `to_guid` | TEXT | | 外部参照の GUID。NULL は同一ファイル内の参照 |
| `to_file_id` | INTEGER | 非 NULL | 参照先の fileID |
| `property_path` | TEXT | 非 NULL | `m_Script`、`m_Materials[0]`、`m_Modifications[3].target` など |
| `ref_kind` | TEXT | 非 NULL | `uses_script` / `source_prefab` / `prefab_instance` / `generic` など（一覧は `docs/manual/schema.md`） |

インデックス: `idx_refs_from_object(from_object_id)`、`idx_refs_to_guid(to_guid)`。

## `code_symbols`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `asset_id` | INTEGER | 非 NULL、`assets(id)` | `.cs` のアセット |
| `symbol` | TEXT | 非 NULL | `Namespace.Type` または `Namespace.Type.Member` |
| `kind` | TEXT | 非 NULL | `class` / `struct` / `interface` / `enum` / `record` / `delegate` / `method` / `property` / `field` など |
| `doc` | TEXT | | XML doc コメント（`<summary>` と `<remarks>`）の平文。無ければ NULL |

インデックス: `idx_code_symbols_asset(asset_id)`。スキーマ v3 で追加されました。

## `settings_files`

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `path` | TEXT | 主キー | 走査した `ProjectSettings/*.asset` |
| `file_hash` | TEXT | 非 NULL | 変更検出用 |

参照を持たないファイルは `assets` に行ができないため、変更検出のためにこのテーブルで持ちます。

## ビュー

**`asset_edges`**: アセット単位に集約した参照です。jsonl の出力と、`find_references` などの逆依存の問い合わせに使います。

| 列 | 説明 |
|---|---|
| `from_guid` | 参照元アセットの GUID |
| `to_guid` | 参照先の GUID（`to_guid` が NULL の同一ファイル参照は含まない） |
| `ref_kind` | 参照種別 |
| `ref_count` | 該当する `refs` の行数 |

**`unresolved_guids`**: `assets` のうち `path` が NULL の行の `guid` です。

## `placements`（提案）

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


関連: [データベース一覧](README.md)
