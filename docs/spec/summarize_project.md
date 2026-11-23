# summarize_project

プロジェクトの概要（シーン、ビルド順、フォルダー、スクリプト数、README の抜粋）を 1 回で返します。

## 引数

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `max_chars` | int | 1500 | 出力の文字数の目安。200〜20000 に丸める。超えるときは、シーン、フォルダー、README の順に切り詰める |
| `scene_limit` | int | 20 | 列挙するシーンの上限 |
| `folder_depth` | int | 1 | `Assets/` から下りる階層数。1〜3 に丸める |
| `readme_chars` | int | 200 | README の抜粋の文字数。0 なら省略。0〜2000 に丸める |

## 出力

```json
{"scenes": ["Assets/Scenes/Main.unity"], "scenes_truncated": true,
 "build_order": ["Assets/Scenes/Main.unity"],
 "folders": [{"path": "Assets/Art", "assets": 676, "types": {"Prefab": 136}}], "folders_truncated": true,
 "scripts": {"assets": 17, "packages": 5588},
 "readme": "...", "readme_truncated": true}
```

- `scenes` は `Assets/` 内のシーン、`build_order` は `ProjectSettings` の `m_Scenes` の順です。どちらも切り詰め時は同じ件数に揃えます。
- `folders` はアセット数の多い順、`types` は多い順に上位 4 件です。
- `scripts.packages` は `Packages` と `PackageCache` の合計です。
- `readme` は、サーバーがプロジェクトのフォルダーを知っているときだけ付きます（`GraphMcpOptions.ProjectPath`。`serve --project` は N-4 で追加予定。それまでは付きません）。
- `*_truncated` は、切り詰めたときだけ `true` で付きます。

## 備考

- 引数名は snake_case です（C# の引数名がそのまま MCP の引数名になるため）。
- `build_order` は有効・無効の区別を持ちません（グラフに保存していないため）。
