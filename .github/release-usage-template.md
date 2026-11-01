# Unity Ariadne {{VERSION}}

CLI 実行に必要な最小限のソース一式です(`src/Analyzer` + `src/Graph`)。
コンパイル済みバイナリではなく、.NET 10 SDK でその場でビルド・実行してください。
.NET SDK なしで使いたい場合は、同じ Release のインストーラー(`.msi` / `.pkg`)をご利用ください。

## 前提

- .NET 10 SDK が必要です。`dotnet` が PATH にない場合は環境変数 PATH に SDK の
  インストール先を追加してください(例: `export PATH="$HOME/.dotnet:$PATH"`)。

## 実行例

```sh
dotnet run --project src/Analyzer -- analyze <unity-project-path> --db graph.db
dotnet run --project src/Analyzer -- report --db graph.db
dotnet run --project src/Analyzer -- export --db graph.db --jsonl out.jsonl
dotnet run --project src/Analyzer -- serve --db graph.db
```

詳しいマニュアルは GitHub リポジトリの `docs/manual/` を参照してください:
{{DOCS_URL}}

---

> Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。
>
> Ariadne is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
