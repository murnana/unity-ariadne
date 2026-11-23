# describe_ontology

このプロジェクトのグラフの語彙（アセット種別と参照種別）と、`search` の使い方の例を返します。引数はありません。

## 出力

```json
{"search_guide": "...",
 "classes": [{"type": "Prefab", "category": "...", "count": 141, "by_source": {"Assets": 100}},
             {"type": "TerrainData", "category": "Geometry", "parsed": false, "count": 1, "by_source": {"Assets": 1}}],
 "relations": [{"kind": "uses_script", "layer": "...", "out": "...", "in": "...", "transitive": false,
                "edges": 300, "ref_count": 450,
                "signatures": ["Prefab->Script: 120"], "more_signatures": 3}]}
```

- `search_guide`: `search` のキーワードの書き方と使用例（継承を含む鎖など）です。`search` の description に載せていた例をここへ移しています。
- `classes`: 種別ごとのカテゴリ、件数、source 別の件数です。
- `relations`: 参照種別ごとの意味（`out`: A→B、`in`: B←A）、推移的にたどる意味があるか、辺の数、参照の数、`FromType->ToType: 件数` の署名（最大 12 件。残りは `more_signatures`）です。
- 用語集にない参照種別は `layer` / `out` / `in` / `transitive` が省略されます。
- 空のグラフでは `classes` と `relations` が空配列です。

## 備考

出力の大きさは relation の数と署名の数で決まり、プロジェクトが大きいほど増えます。縮小案は [new-features.md](new-features.md) を参照してください。

- `classes[].parsed` は、本体がバイナリで読んでいない種類にだけ `false` で付きます（Texture / Model / SpeedTree / AudioClip / VideoClip / Font / TerrainData / Assembly）。付いていない種類は本体を読んでいます。ただし `.asset` はファイルごとにテキストかバイナリかが違うため、ScriptableObject や SerializedAsset の個別のバイナリファイルまでは判定していません。
