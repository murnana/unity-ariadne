# 新機能の仕様（提案・未実装）

検索時のトークン消費を減らすための追加仕様です。各項目に効果の見込みと互換性への影響を併記します。数値はすべて見込みで、フィクスチャでの実測が必要です。

実装済みなのは、`search` の description と引数説明の圧縮（`tools/list` 8,475 → 6,757 文字）と、`describe_ontology` の `search_guide` の追加だけです。

## 共通方針

- 既存の出力に影響する変更は、新しい引数で選べるようにするか、追加フィールドに留めます。
- 既定の出力を変えるもの（N-2、N-3）は、実測してから決めます。
- スキーマに足す文字数を抑えるため、新しい引数の説明は 1 行にします。

## N-1. `find_references` の `summary` モード（改善点 2）

**目的**: 「対象を解決 → 参照元を取得 → 隣接を取得」の 3〜4 往復を 1 回にします。

**引数の追加**

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `summary` | bool | false | true なら種別ごとの件数と上位 N 件のパスを返す |
| `top` | int | 5 | 種別ごとに返すパスの数。1〜20 |

**出力（`summary: true`）**

```json
{"asset": {...}, "total_referrers": 120,
 "by_kind": [{"kind": "uses_script", "referrers": 80, "refs": 95,
              "top": ["Assets/A.prefab", "Assets/B.prefab"]}]}
```

- `by_kind` は `referrers` の多い順です。
- 個別の行は返しません（`references` は無し）。
- 型ごとの内訳が欲しいときのために、`by_type`（参照元のアセット種別ごとの件数）も付けます。

**効果**: 参照元が多いアセットで出力が 1/5〜1/20 になり、往復が 1 回で済む見込みです。
**互換性**: `summary` が false の間は現状と同一です。

## N-2. `search` の `verbose`（改善点 1）

**引数の追加**: `verbose`（bool、既定 false）。

- false: `name` / `type` / `source` / `path` / `from` と `notes` だけを返します。`clues` / `doc` / `from_clues` / `from_doc` / `declares` は省きます。
- true: 現状と同じです。
- 結果の先頭 3 件だけは、false でも根拠（`clues`）を返す案もあります。

**効果**: 1 件あたりの出力が小さくなります。削減量は実測が必要です。
**互換性**: 既定の出力が変わります。`claude-plugins/skills/search-graph` が `clues` / `doc` を報告に使っているため、スキルを更新するか、当面は既定を true にします。

## N-3. `describe_ontology` の `detail`（改善点 1）

**引数の追加**: `detail`（`summary` | `full`）。

- `summary`: `classes` と、`relations` の `kind` / `out` / `edges` だけを返します。`signatures` は省きます。
- `full`: 現状と同じです。

**互換性**: 既定を `summary` にすると破壊的です。まず `full` を既定のまま追加し、実測してから切り替えます。

## N-4. 鮮度の表示（改善点 5）

**対象**: 全ツールの応答（`meta` を 1 つ付ける）。

```json
{"meta": {"analyzed_at": "2026-11-23T09:00:00Z",
          "vcs": {"kind": "git", "repo_url": "https://example.com/team/game.git", "revision": "3aa0764..."},
          "stale": true, "stale_reason": "revision_changed"}}
```

**方針**: 鮮度の基準は、バージョン管理システム（VCS）が発行するリビジョンと、リポジトリ URL とします。ファイルの更新時刻は、VCS が無いときの代替にだけ使います。

### 識別情報

| 項目 | Git | Subversion | VCS なし |
|---|---|---|---|
| `kind` | `git` | `svn` | `none` |
| `repo_url` | `git remote get-url origin` | `svn info` の Repository Root | なし |
| `revision` | `git rev-parse HEAD`（SHA） | `svn info` の Revision | なし |
| 未コミットの変更 | `git status --porcelain` | `svn status` | 更新時刻で判定 |

- 値はすべて文字列で保存し、VCS 固有の型を db のスキーマに持ち込みません。
- `kind` ごとの取得処理は `IVcsProvider`（検出、識別情報の取得、変更ファイルの一覧）に分け、後から Perforce や Plastic SCM などを足せるようにします。

### 保存先（graph.db とは別の db）

識別情報は、graph.db とは別の SQLite ファイル（例: `provenance.db`）に持ちます。graph.db のスキーマは変えないので、`PRAGMA user_version` は上げません。VCS を移行しても、graph.db を作り直さずに済みます。

| テーブル | 列 | 説明 |
|---|---|---|
| `snapshots` | `id`, `analyzed_at`, `vcs_kind`, `repo_url`, `revision`, `dirty` | 解析ごとの記録 |
| `asset_revisions` | `snapshot_id`, `asset_guid`, `revision`, `file_hash` | アセットごとの最終変更リビジョン。キーはアセットの GUID で、パスは持ちません |
| `repo_aliases` | `old_repo_url`, `new_repo_url`, `note` | リポジトリの URL 変更や VCS 移行の対応表。手動または `guidgraph vcs-alias` で登録します |

### graph.db との対応づけ

- 対応づけには、ファイル名やパスではなく、アセットの GUID（`.meta` の `guid`）を使います。ファイルの移動や改名を検知できない VCS（Subversion など）でも、Unity では GUID が変わらないため、同じアセットとして扱えます。
- provenance.db の `asset_guid` は graph.db の `assets.guid` と同じ値です。graph.db には、新しい列もテーブルも追加しません（`PRAGMA user_version` は上げません）。
- 古い db でも、`assets.guid` があれば使えます。provenance.db に記録が無いアセットは、`stale_reason: "unknown"` として扱います。
- provenance.db は、graph.db と同じプロジェクト用のフォルダーに置きます（プラグインでは、プロジェクトごとの db フォルダー）。

### 判定

1. 現在の `kind` / `repo_url` / `revision` を取得し、最新の `snapshots` と比べます。`repo_url` は `repo_aliases` で読み替えます。
2. `repo_url` が一致し `revision` が同じなら、`stale: false` です。ただし未コミットの変更に `.meta` / `.unity` / `.prefab` が含まれていれば、`stale: true`、`stale_reason: "uncommitted_changes"` にします。
3. `revision` が違えば `stale: true`、`stale_reason: "revision_changed"` です。
4. `repo_url` が違い、別名の登録も無ければ `stale: true`、`stale_reason: "repo_mismatch"` にします。別のリポジトリで作った db を使っている可能性があります。
5. `kind` が `none`、または provenance.db が無い場合は、解析時刻と `Assets/` 内の `.meta` / `.unity` / `.prefab` の更新時刻を比べます（`stale_reason: "mtime"`）。
6. VCS コマンドが失敗したときは `stale` を省き、`stale_reason: "unknown"` にします。

**実装上の注意**: 毎回 VCS コマンドを実行すると重くなるため、結果を数十秒キャッシュします。外部コマンドの実行方法（権限や環境の制約）は、実装時に検討します。
**効果**: 「最新か未確認」という確認の往復がなくなります。1 応答あたり約 40 トークン増えます。
**互換性**: 応答へのフィールド追加だけです。graph.db は変わりません。

## N-5. `summarize_project`（改善点 7）

**目的**: プロジェクト概要を数百トークンで返します。引数はありません。

```json
{"scenes": ["Assets/Scenes/Main.unity"], "build_order": ["Assets/Scenes/Main.unity"],
 "folders": [{"path": "Assets/Art", "types": {"Prefab": 120, "Material": 80}}],
 "scripts": {"assets": 40, "packages": 3},
 "readme": "README の先頭 200 文字"}
```

- `scenes`: 全シーン。多いときは 20 件で打ち切り、`truncated` を付けます。
- `build_order`: ビルド設定（EditorBuildSettings）の順。
- `folders`: `Assets/` の最上位フォルダーごとの種別件数（上位 4 種）。
- `readme`: ルートの README の先頭 200 文字。無ければ省略します。
- 出力の大きさは引数で調整できます（下表）。

| 引数 | 既定 | 説明 |
|---|---|---|
| `max_chars` | 1500 | 出力の文字数の目安。超えるときは、シーン、フォルダー、README の順に切り詰めます |
| `scene_limit` | 20 | 列挙するシーンの上限 |
| `folder_depth` | 1 | フォルダーを下りる階層数（1〜3） |
| `readme_chars` | 200 | README の抜粋の文字数。0 なら省略 |

切り詰めた項目には `truncated: true` を付けます。

**効果**: 概要の質問で、グラフなしより少ないトークンで答えられることを目標にします。
**互換性**: ツールの追加だけです。ただしツール定義が 1 つ増えるので、固定コストが約 100 トークン増えます。

## N-6. ラッパー prefab の種別（改善点 4）

`search` の `matches[]` に `role` を追加します。

- `wrapper`: 自身のコンポーネントを持たず、子が別 prefab のインスタンス（`prefab_instance`）だけで構成される prefab。
- `leaf`: それ以外の prefab。

「木」のような概念検索で、ラッパーと中身を数え分けられるようにします。判定は `refs` の `prefab_instance` 辺と、`objects` の中身から行います。
**互換性**: Prefab 以外では付けません。追加フィールドです。

## N-7. 未解決 GUID の手がかり（改善点 6）

`list_unresolved` の各要素に `hint` を追加します。

```json
{"guid": "...", "hint": {"package_candidates": ["com.unity.render-pipelines.universal"], "basis": "path"}}
```

- 参照元のパスや、周辺アセットが依存しているパッケージから、`Packages` / `PackageCache` のどのパッケージにありそうかを推定します。
- 推定できないときは `hint` を省きます。
- 確実ではないため、`basis` に推定の根拠を書きます。

**互換性**: 追加フィールドです。

## N-8. 展開数 `expanded_count`（改善点 3）

**ツール**: `expanded_count`（新規）

| 名前 | 型 | 必須 | 既定 | 説明 |
|---|---|---|---|---|
| `asset` | string | はい | | 数えるアセット（prefab など） |
| `scene` | string | はい | | 展開先のシーン、または prefab |
| `max_depth` | int | いいえ | 10 | 入れ子をたどる深さの上限。1〜50 に丸める |

**出力**

```json
{"asset": "...", "scene": "...", "expanded_count": 128, "max_depth": 10,
 "direct": 20, "via_nested": 108, "removed": 3,
 "depth_limited": false, "unsupported": ["TerrainData"]}
```

`depth_limited` は、上限に達して打ち切った経路があるときに true にします。

### 解析時に保存するもの

配置の情報は解析時に graph.db に保存し、掛け合わせは呼び出し時に行います。深さの上限を引数で変えられるようにするためです。

新しいテーブル `placements`（スキーマ変更のため `PRAGMA user_version` を上げます）:

| 列 | 説明 |
|---|---|
| `from_guid` | 配置する側（シーンまたは prefab）の GUID |
| `to_guid` | 配置される prefab の GUID |
| `instances` | その経路で配置されている PrefabInstance の数 |
| `removed_objects` | `m_RemovedGameObjects` による削除数 |
| `removed_components` | `m_RemovedComponents` による削除数 |

- 1 行は「A が B を直接配置している数」です。入れ子の乗算は保存せず、呼び出し時にシーンから `asset` までの経路をたどって掛け合わせます。
- 経路が複数あるときは合計します。循環は検出して打ち切ります。
- `m_Modifications` による差し替えは、対象が一致するときに反映します。バリアント prefab は元の prefab の中身も含めて数えます。
- 増分更新が全再構築と行単位で一致することを、差分テストで保証します。

**効果**: シーンや prefab の YAML を呼び出し時に読まないので、応答が速く、エージェントが YAML を直接集計する必要もなくなります。
**互換性**: ツールの追加と、テーブルの追加です。古い db では `placements` が無いため、再 analyze を案内するエラーを返します。
**リスク**: 4 件中最も実装が大きく、実際の Unity のデータで正答を確認するまで正しさを保証できません。

## N-9. 未対応のバイナリ資産の明示（改善点 8）

- バイナリ形式の資産（Terrain の `TerrainData.asset` など）は中身を解析していないことを、結果に明示します。
- `search` と `expanded_count` の応答に `unsupported: ["TerrainData"]` のように、結果に関係する未対応の種別を載せます。
- `describe_ontology` の `classes[]` に `parsed: false` を付けます。
- 将来 tree prototype の参照を抽出する場合は別の課題とします。

**互換性**: 追加フィールドです。

## 実装の優先順位（提案）

| 順 | 項目 | 理由 |
|---|---|---|
| 1 | N-1、N-5 | 効果が大きく、既存の出力を変えない |
| 2 | N-4、N-6、N-7、N-9 | 追加フィールドが中心。N-4 は VCS 連携と別 db の設計が要る |
| 3 | N-2、N-3 | 既定の出力が変わるため、実測してから決める |
| 4 | N-8 | 効果は大きいが、実装とデータ検証が最も重い |
