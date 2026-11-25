# search_nodes

パスの部分文字列または GUID の完全一致でアセットを探します。短いパスが先に並びます。

## 引数

| 名前 | 型 | 既定 | 説明 |
|---|---|---|---|
| `query` | string | `""` | パスの部分文字列（大文字小文字を区別しない）または GUID。空なら全件 |
| `type` | string | なし | アセット種別（`Prefab` など。大文字小文字を区別しない） |
| `source` | string | なし | `Assets` / `Packages` / `PackageCache` / `ProjectSettings` / `BuiltIn` / `Unresolved` |
| `unreferenced` | bool | false | 他から参照されないアセットだけにする |
| `limit` | int | 20 | 1〜100 に丸める |

空白だけの `type` / `source` は指定なしとして扱います。

## 出力

```json
{"query": "", "type": "Scene", "source": null, "unreferenced": true,
 "matches": [{"name": "...", "type": "Scene", "source": "Assets"}], "truncated": true}
```

一致なしは `matches: []` で、エラーにはなりません。

## 備考

使える種別名と source は `describe_ontology` で確認できます。
