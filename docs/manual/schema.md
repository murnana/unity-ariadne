# SQLite スキーマと直接クエリ

`analyze --db graph.db` が書き出す SQLite は、MCP サーバーや `export` を介さず `sqlite3` で直接引くことができます。AI エージェントに sqlite3 の読み取りを許可してこの DB を渡すだけでも実用になります([効果測定](../development/benchmark.md)はこの形で実施しました)。

## スキーマ

```sql
-- PRAGMA user_version = 2 (スキーマ変更時にバンプ。バージョン不一致の DB はフル再構築されます)

CREATE TABLE assets (
  id INTEGER PRIMARY KEY,
  guid TEXT UNIQUE NOT NULL,
  path TEXT,                 -- 未解決 GUID は NULL
  asset_type TEXT NOT NULL,  -- Scene / Prefab / Script / ScriptableObject / Material / ...
  source TEXT NOT NULL,      -- Assets / Packages / PackageCache / ProjectSettings / BuiltIn / Unresolved
  file_hash TEXT             -- 差分更新用。Unresolved / BuiltIn / ProjectSettings は NULL
);

CREATE TABLE objects (        -- YAML ドキュメント 1 つ = fileID 1 つ
  id INTEGER PRIMARY KEY,
  asset_id INTEGER NOT NULL REFERENCES assets(id),
  file_id INTEGER NOT NULL,
  class_id INTEGER NOT NULL,  -- Unity クラス ID(!u!114 など)
  name TEXT,                  -- m_Name(あれば)
  is_stripped INTEGER DEFAULT 0,
  UNIQUE(asset_id, file_id)
);

CREATE TABLE refs (           -- 生の参照(1 参照 = 1 行)
  id INTEGER PRIMARY KEY,
  from_object_id INTEGER NOT NULL REFERENCES objects(id),
  to_guid TEXT,               -- 外部参照。NULL は同一ファイル内参照
  to_file_id INTEGER NOT NULL,
  property_path TEXT NOT NULL,-- 例: m_Script, m_Materials[0], m_Modifications[3].target
  ref_kind TEXT NOT NULL
);

CREATE VIEW asset_edges AS    -- アセット単位に集約(jsonl エクスポートの元)
  SELECT DISTINCT a1.guid AS from_guid, r.to_guid,
         r.ref_kind, COUNT(*) AS ref_count
  FROM refs r
  JOIN objects o ON o.id = r.from_object_id
  JOIN assets a1 ON a1.id = o.asset_id
  WHERE r.to_guid IS NOT NULL
  GROUP BY a1.guid, r.to_guid, r.ref_kind;

CREATE VIEW unresolved_guids AS
  SELECT guid FROM assets WHERE path IS NULL;

CREATE TABLE settings_files (  -- 差分更新用: スキャンした ProjectSettings/*.asset 全部
  path TEXT PRIMARY KEY,       -- (参照を持たず assets 行がないファイルも含む)
  file_hash TEXT NOT NULL
);
```

### 知っておくべき規約

- **ProjectSettings のアセットは GUID を持ちません**。そのため `assets.guid` にプロジェクト相対パス(`ProjectSettings/EditorBuildSettings.asset` など)がそのまま入ります。GUID 列が 32 桁 16 進とは限らない点にご注意ください。
- **ビルトインアセットは解決済みです**: `...d000...` → `Library/unity default resources`、`...e000...` → `Library/unity editor resources`、`...f000...` → `Resources/unity_builtin_extra`(`source = 'BuiltIn'`)。
- **.meta 由来・JSON ファイル由来の参照は擬似オブジェクト**(`file_id = 0, class_id = 0`)にぶら下がります。実アンカーが 0 になることはないため衝突しません。
- **コード層の参照は MonoScript 擬似オブジェクト**(`file_id = 11500000, class_id = 115`、name = 最初の非ネスト宣言型)にぶら下がります。解決済み参照が 1 つ以上あるスクリプトにだけ作られます。
- MCP サーバーと jsonl エクスポートは `corresponding_source` を除外しますが、**refs テーブルには入っています**。生 SQL で数えると多めに出るのはそのためです。

## ref_kind 一覧

### アセット層(YAML / .meta / JSON)

| ref_kind | 意味 |
|---|---|
| `uses_script` | `m_Script` — コンポーネントからスクリプトへの参照 |
| `source_prefab` | プレハブバリアントの元プレハブ(`m_SourcePrefab` がバリアントのルートの場合) |
| `prefab_instance` | ネストされたプレハブインスタンスの元プレハブ |
| `prefab_modification` | プレハブオーバーライドのターゲット(`m_Modifications[*].target`) |
| `corresponding_source` | `m_CorrespondingSourceObject`(件数が多いためエクスポート・MCP からは除外) |
| `meta_external` | .meta 内の参照(FBX→Material リマップ、ScriptedImporter など) |
| `build_settings` | EditorBuildSettings.asset(シーンリスト等) |
| `embedded_json` | JSON 内の参照(.shadergraph / .shadersubgraph、.vfx の JSON-in-YAML) |
| `addressable_entry` | Addressables グループのエントリ(bare GUID + `m_Address`) |
| `asset_reference` | `AssetReference` フィールド等の bare `m_AssetGUID`(URP の ProbeVolumeStreamableAsset も) |
| `generic` | 上記以外の `{fileID, guid}` 参照全般 |

### コード層(C#、Roslyn 構文解析)

| ref_kind | 意味 |
|---|---|
| `inherits` | クラス / インターフェースの基底リスト → 他スクリプト |
| `serialized_field` | シリアライズされるフィールドの型(public / [SerializeField] / [SerializeReference]) |
| `nonserialized_field` | それ以外のフィールドの型(属性なし private、static / const / readonly) |
| `get_component` | GetComponent&lt;T&gt; 系、[RequireComponent]、Find*ByType、typeof 形式 |
| `resources_load` | Resources.Load / LoadAll / LoadAsync のリテラルパス → アセット |
| `addressables_load` | Addressables.Load* / InstantiateAsync のリテラルキー → アドレス / ラベル経由でアセット |
| `custom_editor` | [CustomEditor(typeof(X))] → 対象型のスクリプト |

## クエリ例

パスから GUID を引きます(以降の例の下ごしらえです):

```sql
SELECT guid FROM assets WHERE path = 'Assets/Materials/Fog.mat';
```

**逆依存 — このアセットを参照しているのは誰か:**

```sql
SELECT a.path, e.ref_kind, e.ref_count
FROM asset_edges e
JOIN assets a ON a.guid = e.from_guid
WHERE e.to_guid = (SELECT guid FROM assets WHERE path = 'Assets/Materials/Fog.mat')
  AND e.ref_kind != 'corresponding_source'
ORDER BY e.ref_count DESC;
```

**依存先 — このアセットが参照しているもの:**

```sql
SELECT COALESCE(a.path, e.to_guid) AS target, e.ref_kind, e.ref_count
FROM asset_edges e
LEFT JOIN assets a ON a.guid = e.to_guid
WHERE e.from_guid = (SELECT guid FROM assets WHERE path = 'Assets/Prefabs/House.prefab');
```

**未参照プレハブ(Assets 配下、どこからも参照されていない .prefab):**

```sql
SELECT p.path
FROM assets p
WHERE p.source = 'Assets' AND p.path LIKE '%.prefab'
  AND NOT EXISTS (
    SELECT 1 FROM asset_edges e
    WHERE e.to_guid = p.guid AND e.ref_kind != 'corresponding_source');
```

**参照切れ GUID を参照数順に:**

```sql
SELECT u.guid, COUNT(*) AS refs
FROM unresolved_guids u
JOIN refs r ON r.to_guid = u.guid
GROUP BY u.guid ORDER BY refs DESC;
```

**プロパティパス付きで生参照を見る(どのフィールドが参照しているか):**

```sql
SELECT src.path, o.file_id, r.property_path, r.ref_kind
FROM refs r
JOIN objects o ON o.id = r.from_object_id
JOIN assets src ON src.id = o.asset_id
WHERE r.to_guid = (SELECT guid FROM assets WHERE path = 'Assets/Materials/Fog.mat');
```

## 注意

- DB ファイルは `analyze` が更新中にトランザクションで書き換えます。読み取り専用で開けば途中状態を見ることはありません(単一トランザクションでコミットされます)。
- スキーマ変更時は `PRAGMA user_version` がバンプされます。自作スクリプトでバージョンを確認する場合は `PRAGMA user_version;` で `2` を期待してください。

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
