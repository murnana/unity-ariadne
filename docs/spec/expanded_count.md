# expanded_count

`scene`（シーンまたは prefab）の中で `asset` を直接使っているオブジェクトが、入れ子の prefab を展開すると何個になるかを返します。

## 引数

| 名前 | 型 | 必須 | 既定 | 説明 |
|---|---|---|---|---|
| `asset` | string | はい | | 数えるアセット（種別は問わない）。パス、一意のファイル名、GUID |
| `scene` | string | はい | | 展開先のシーン、または prefab |
| `max_depth` | int | いいえ | 10 | 入れ子をたどる深さ。1〜50 に丸める |

## 出力

```json
{"asset": "...", "asset_type": "Material", "scene": "...", "expanded_count": 6651, "max_depth": 10,
 "direct": 0, "via_nested": 6651, "removed": 0, "unresolved_removals": 8381,
 "depth_limited": true, "unsupported": ["Model"], "notes": "..."}
```

- `expanded_count` は `direct + via_nested - removed`（0 未満にはしません）。
- `direct`: `scene` 自身の中で `asset` を使っている（`use`）か、入れ子にしている（`contain`）数。`via_nested`: 入れ子の prefab の中の同じ数に、その prefab の展開数を掛けて合計したもの。
- `unresolved_removals` と `depth_limited` と `unsupported` は、該当するときだけ付きます。

## 数え方

1. `scene` から `placements` の `contain` だけをたどり、各 prefab が何個ずつ展開されるかを、経路ごとに配置数を掛けて合計します（`max_depth` で打ち切り）。バリアントは元の prefab を `contain` するので、元の中身も数えます。
2. 展開される各 prefab（`scene` 自身を含む）の `use` + `contain` を足して、展開数を掛けます。
3. `placement_removals` の対象のうち `asset` を使っていたものを差し引きます。コンポーネントは、それが `asset` を参照していれば 1。GameObject は、それ自身と、`m_Children` でたどれる子孫のコンポーネントのうち、`asset` を参照するもの。

## 限界（実データで確認）

- 取り除かれた対象の fileID が、`to_guid` のアセットの `objects` に無いことが多く、その場合は差し引けません（Unity が入れ子の prefab・モデルの中のオブジェクトに派生 ID を使うため。単純な XOR では解決できませんでした）。サンプルプロジェクトでは、Demo.unity から到達する取り除きのほぼすべてがこれにあたります。件数を `unresolved_removals` で返し、その分だけ `expanded_count` が多めになりえることを明示します。
- `m_Modifications` による差し替えは、新しいアセットを PrefabInstance ごとに 1 回数え、元のアセットからは引きません。
- バイナリ本体のアセット（N-9）の中身は数えません。

## エラー

`not_found` / `ambiguous`（共通仕様）。`scene` が Scene でも Prefab でもないときは `invalid_scene`。
