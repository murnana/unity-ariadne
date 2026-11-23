# list_unresolved

未解決 GUID（参照先のアセットがプロジェクトに存在しない参照）を、参照数の多い順に返します。引数はありません。

## 出力

```json
{"total_unresolved": 4,
 "unresolved": [{"guid": "...", "ref_count": 10, "referrer_count": 3, "sample_referrers": ["Assets/A.unity"],
                  "hint": {"package_candidates": ["com.unity.visualeffectgraph"], "basis": "referrer_package"}}],
 "truncated": true}
```

`unresolved` は最大 200 件です。`{fileID: 0}` の null 参照は数えません。

## 備考

`hint` は、どのパッケージにありそうかの推定です（推定できないときは省きます）。確実ではありません。根拠 `basis` は次の 2 種類で、この順に試します。

- `referrer_package`: 参照元が `Packages/` や `Library/PackageCache/` の中にある。そのパッケージ名（ハッシュなし）を、参照元の多い順に最大 3 件。
- `referrer_dependencies`: 参照元は `Assets/` などだが、同じ参照元がパッケージのアセットも使っている。そのパッケージ名を、多い順に最大 3 件。

推定したパッケージが実際にその GUID を持っているかは確かめていません。ProjectSettings や、他に手がかりのない `Assets/` の参照元だけのものには、`hint` は付きません。出力は、サンプルでは 1 件あたり約 90 文字増えます。
