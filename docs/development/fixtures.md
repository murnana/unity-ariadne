# テストフィクスチャ

テストの多くは実際の Unity プロジェクトを解析して件数(census)や既知の参照を検証しますが、そのプロジェクトは Asset Store コンテンツのため**このリポジトリには含めていません**。フィクスチャが無い環境(CI を含む)では、それらのテストは `[FixtureFact]` / `[FixtureTheory]` により **Skipped** になり、インラインデータだけで完結するユニットテストのみが走ります。

## フィクスチャを用意して全テストを走らせる

環境変数 `ARIADNE_FIXTURES` に、次の構成のディレクトリを指定します。

```
$ARIADNE_FIXTURES/
├─ SampleProject/                  # Unity プロジェクト(Assets/ ProjectSettings/ Packages/)
├─ Addressables/                   # Addressables 設定一式(Assets/ 相対配置)
└─ unity-select-dependencies.txt   # AssetDatabase.GetDependencies のダンプ
```

```sh
ARIADNE_FIXTURES=/path/to/fixtures dotnet test
```

- `SampleProject/` — [Fantasy Kingdom in Unity 6 | URP](https://assetstore.unity.com/packages/essentials/tutorial-projects/fantasy-kingdom-in-unity-6-urp-298128) を Unity **6000.0.78f1** で開き、Force Text シリアライズにしてから一部を間引いたもの。テストの期待値はこの間引き後の内容に固定されているため、Asset Store 版をそのまま置いても census 系テストは一致しません。
- `SampleProject/Library/PackageCache` がある場合のみ、PackageCache を前提とするテストも有効になります(Unity でプロジェクトを一度開くと生成されます)。
- `Addressables/` — SampleProject のクローンに `com.unity.addressables` 2.3.16 を追加し、batchmode で生成した設定・グループ・スモークシーン。エントリは SampleProject のアセットを指します。
- `unity-select-dependencies.txt` — 6 アセットに対する `AssetDatabase.GetDependencies` の結果(self 除外)。`UnitySelectDependenciesTests` が突合に使います。

## ルール

- フィクスチャの YAML は実 Unity Editor の出力です。**手で(あるいは AI に)生成・編集させないでください。** 変更が必要なら、プロジェクトを APFS クローン等で複製し、使い捨てのエディタスクリプトを batchmode で実行して作り直します。
- フィクスチャに依存するテストを追加するときは `[Fact]` ではなく `[FixtureFact]` を付けます。`Fixtures.SampleProject` 等は未設定時に例外を投げるため、静的フィールド初期化子では参照しないでください(プロパティにする)。
