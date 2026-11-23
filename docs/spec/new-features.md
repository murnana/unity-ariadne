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
{"meta": {"analyzed_at": "2026-11-23T09:00:00Z", "stale": true, "stale_count": 3}}
```

**方針**: 解析時刻と、ファイルの更新時刻を比べるだけにします。バージョン管理システム（VCS）には依存しません。

- `analyzed_at`: graph.db ファイルの最終更新時刻（UTC）です。`analyze`（全再構築と増分更新）が書き終えた時刻にあたります。スキーマは変えません。
- `stale`: `Assets/` 内の `.meta` / `.unity` / `.prefab` のうち、`analyzed_at` より後に更新されたものが 1 つでもあれば true です。
- `stale_count`: その数です。上限 1000 で打ち切ります。
- 削除されたファイルは検出しません。

### プロジェクトの場所

走査にはプロジェクトのパスが必要ですが、graph.db は持っていません。`serve` に任意の引数 `--project <path>` を追加し、プラグインの `launch.mjs` が渡します。

- 指定がないときは、`meta` に `analyzed_at` だけを載せ、`stale` は省きます。
- `--project` で graph.db にパスを保存する方式は、スキーマ変更が要るため、今回は採りません。
- パスが存在しないときも同様です。

### 実装上の注意

- 毎回の走査は重くなるため、結果を数十秒キャッシュします。
- 走査するのは更新時刻だけで、ファイルの中身は読みません。
- graph.db は、コピーや共有をしない前提です。そのため、ファイルの更新時刻を解析時刻として扱えます。
- 時刻の精度やタイムゾーンの違い（ファイルシステムによる）を避けるため、比較は秒単位にします。

**効果**: 「最新か未確認」という確認の往復がなくなります。1 応答あたり約 30 トークン増えます。
**互換性**: 応答へのフィールド追加と、`serve` の任意引数の追加だけです。graph.db は変わりません。

### 将来の拡張（今回は対象外）

VCS のリビジョン（Git の SHA、Subversion のリビジョン）による判定は、今回は扱いません。必要になったときに、別の仕組みとして追加します。

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
サンプルプロジェクト（prefab 172 件）で確認した定義: 入れ子の PrefabInstance があり、自身の持ち物が GameObject・Transform・RectTransform だけなら `wrapper`（88 件）、それ以外は `leaf`（84 件）。
**互換性**: Prefab 以外では付けません。追加フィールドです。1 件あたり約 16 文字増えます。

## N-7. 未解決 GUID の手がかり（改善点 6）

`list_unresolved` の各要素に `hint` を追加します。

```json
{"guid": "...", "hint": {"package_candidates": ["com.unity.render-pipelines.universal"], "basis": "path"}}
```

- 参照元のパスや、周辺アセットが依存しているパッケージから、`Packages` / `PackageCache` のどのパッケージにありそうかを推定します。
- 推定できないときは `hint` を省きます。
- 確実ではないため、`basis` に推定の根拠を書きます。

サンプル（未解決 82 件）での確認: `referrer_package` が 41 件、`referrer_dependencies` が 32 件、手がかりなしが 9 件。`hint` で出力は 18.4k → 25.6k 文字に増えるため、`limit`（既定 20）を追加して打ち切る。
**互換性**: 追加フィールドです。

## N-8. 展開数 `expanded_count`（改善点 3）

**目的**: 「アセット X が、シーン S に、実際にいくつ展開されるか」を返します。X は prefab に限らず、マテリアル、メッシュ、スクリプトなど、どのアセットでも構いません（例: マテリアルを使っているレンダラーの数、スクリプトが付いているコンポーネントの数）。

**ツール**: `expanded_count`（新規）

| 名前 | 型 | 必須 | 既定 | 説明 |
|---|---|---|---|---|
| `asset` | string | はい | | 数えるアセット（種別は問わない） |
| `scene` | string | はい | | 展開先のシーン、または prefab |
| `max_depth` | int | いいえ | 10 | 入れ子をたどる深さの上限。1〜50 に丸める |

**出力**

```json
{"asset": "...", "asset_type": "Material", "scene": "...", "expanded_count": 128,
 "max_depth": 10, "direct": 20, "via_nested": 108, "removed": 3,
 "depth_limited": false, "unsupported": ["TerrainData"]}
```

- `expanded_count` は、`direct + via_nested - removed` です。
- `direct`: `scene` が直接使っている数。`via_nested`: 入れ子の prefab を経由して使っている数（配置数を掛け合わせた後）。
- `depth_limited` は、上限に達して打ち切った経路があるときに true にします。

### 数え方

数える単位は、「そのアセットを直接使っているオブジェクトの数」です。`asset` が prefab のときは、PrefabInstance の数になります。

1. シーンから、入れ子の配置（`contain`、PrefabInstance の連鎖）だけをたどり、各 prefab を何個ずつ展開するかを求めます。経路ごとに、各段の配置数を掛け合わせます。
2. 展開される各 prefab（シーン自身を含む）について、その中で `asset` を直接使っているオブジェクトの数（`use`）に、1 の個数を掛けて合計します。`asset` が prefab のときは、その prefab を直接配置している数（`contain`）を使います。
3. 取り除かれた分を差し引きます（下記）。

- 入れ子の乗算に使うのは `contain` だけです。マテリアルからテクスチャへのような `use` の連鎖は掛け合わせません（テクスチャが何個のマテリアルに使われているかを数えたいのではなく、シーンでの使用箇所を数えたいため）。
- 経路が複数あるときは合計します。循環は検出して打ち切ります。
- バリアント prefab は、元の prefab の中身も含めて数えます。
- `m_Modifications` による差し替え（別のアセットへの付け替え）は、対象が一致するときに反映します。

### 取り除かれた分の差し引き

`m_RemovedGameObjects` と `m_RemovedComponents` の対象を、`placement_removals` に保存します。差し引くのは、取り除かれた対象のうち、`asset` を使っていたものだけです。

- 取り除かれたコンポーネントが `asset` を参照していれば、1 差し引きます。
- 取り除かれた GameObject は、そのコンポーネントと子のうち `asset` を参照するものを、すべて差し引きます（子へのたどり方は `m_Children` の参照によります）。
- 取り除きは PrefabInstance ごとに別なので、インスタンスごとに差し引きます。
- `asset` が prefab のとき、取り除かれたもの自体が入れ子の PrefabInstance であれば、その分を差し引きます。

### 解析時に保存するもの

配置の情報は解析時に graph.db に保存し、掛け合わせは呼び出し時に行います。深さの上限を引数で変えられるようにするためです。定義は [databases/graph-db.md](databases/graph-db.md) の `placements` と `placement_removals` を参照してください（`SchemaVersion` を 4 に上げます）。

- `placements` は、「A が B を直接使っている数」を `contain` / `use` に分けて持ちます。B は prefab に限りません。
- 増分更新が全再構築と行単位で一致することを、差分テストで保証します。

**効果**: シーンや prefab の YAML を呼び出し時に読まないので、応答が速く、エージェントが YAML を直接集計する必要もなくなります。prefab 以外のアセット（「このマテリアルはシーンにいくつあるか」など）にも使えます。
**互換性**: ツールの追加と、テーブルの追加です。古い db では `placements` が無いため、再 analyze を案内するエラーを返します。
**リスク**: 4 件中最も実装が大きく、実際の Unity のデータで正答を確認するまで正しさを保証できません。`placements` を全アセットに広げたため、行数が増えます（参照のあるアセットの組み合わせごとに 1 行）。実測して、大きすぎる場合は `use` を対象種別で絞る案があります。

## N-9. 未対応のバイナリ資産の明示（改善点 8）

- バイナリ形式の資産（Terrain の `TerrainData.asset` など）は中身を解析していないことを、結果に明示します。
- `search` と `expanded_count` の応答に `unsupported: ["TerrainData"]` のように、結果に関係する未対応の種別を載せます。
- `describe_ontology` の `classes[]` に `parsed: false` を付けます。
- 基準（実データで確認）: 本体がバイナリ形式で、解析が一度も開かない種類を固定の一覧で持ちます（Texture / Model / SpeedTree / AudioClip / VideoClip / Font / TerrainData / Assembly）。`unsupported` は、`search` の開始点と結果の種類のうち、この一覧に入るものです（`expanded_count` へは N-8 で足します）。ファイルごとにテキストかバイナリかが違う `.asset` は判定しません。出力は describe_ontology で約 +90 文字です。
- 将来 tree prototype の参照を抽出する場合は別の課題とします。

**互換性**: 追加フィールドです。

## 実装の優先順位（提案）

| 順 | 項目 | 理由 |
|---|---|---|
| 1 | N-1、N-5 | 効果が大きく、既存の出力を変えない |
| 2 | N-4、N-6、N-7、N-9 | 追加フィールドが中心 |
| 3 | N-2、N-3 | 既定の出力が変わるため、実測してから決める |
| 4 | N-8 | 効果は大きいが、実装とデータ検証が最も重い |
