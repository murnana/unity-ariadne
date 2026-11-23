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

- `analyzed_at`: db に記録された解析時刻です。
- `stale`: 解析時刻より後に更新された `Assets/` 内の `.meta` / `.unity` / `.prefab` が 1 つでもあれば true です。
- `stale_count`: その数です。上限 1000 で打ち切ります。
- 判定にはファイルの更新時刻を使います。db がプロジェクトパスを持っていない場合（別マシンで作った db など）は `stale` を省きます。

**実装上の注意**: 毎回のディレクトリ走査は重くなるため、結果を数十秒キャッシュするか、`.meta` / `.unity` / `.prefab` の更新時刻だけを見ます。スキーマを変えて解析時刻を保存する場合は `PRAGMA user_version` を上げ、増分更新が全再構築と行単位で一致することを保つ必要があります。
**効果**: 「最新か未確認」という確認の往復がなくなります。1 応答あたり約 30 トークン増えます。
**互換性**: フィールドの追加だけです。

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
- 出力は 1,500 文字以内に収めます。

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

| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| `asset` | string | はい | 数えるアセット（prefab など） |
| `scene` | string | はい | 展開先のシーン、または prefab |

**出力**

```json
{"asset": "...", "scene": "...", "expanded_count": 128,
 "direct": 20, "via_nested": 108, "removed": 3, "unsupported": ["TerrainData"]}
```

**算出方法**

- シーンから `asset` までの各経路（`prefab_instance` の連鎖）をたどり、各段の配置数を掛け合わせます。
- `m_RemovedGameObjects` に含まれる GameObject と、`m_RemovedComponents` に含まれるコンポーネントを差し引きます。
- `m_Modifications` による差し替えは、対象が一致するときに反映します。
- バリアント prefab は、元の prefab の中身も含めて数えます。

**制約**: シーンや prefab の YAML を読み直す必要があるため、グラフ（`refs` テーブル）だけでは算出できません。解析時に配置数を保存するか、呼び出し時に該当ファイルだけ読むかを設計で決めます。入れ子が深い場合は上限（例: 深さ 10）を設けます。
**互換性**: ツールの追加です。スキーマを変える場合は `PRAGMA user_version` を上げ、増分更新と全再構築の一致を保ちます。
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
| 1 | N-1、N-5、N-4 | 効果が大きく、既存の出力を変えない |
| 2 | N-6、N-7、N-9 | 追加フィールドだけで済む |
| 3 | N-2、N-3 | 既定の出力が変わるため、実測してから決める |
| 4 | N-8 | 効果は大きいが、実装とデータ検証が最も重い |
