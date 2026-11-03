# コントリビューションガイド

Unity Ariadne への貢献を歓迎します。参加にあたっては [行動規範](CODE_OF_CONDUCT.md) に従ってください。脆弱性は Issue ではなく [SECURITY.md](SECURITY.md) の手順で報告してください。

## 進め方

1. 大きな変更は、先に Issue で相談してください(残課題は [GitHub Issues](https://github.com/murnana/unity-ariadne/issues) で管理しています)。
2. リポジトリを fork し、`develop` ブランチから作業ブランチを切って変更します(`main` からは切りません)。`develop` がない場合は、先に `main` から `develop` を作成してください。
3. `dotnet test` が通ることを確認して、`develop` 宛てに Pull Request を作成します。PR テンプレートのチェックリストに沿って確認してください。

## 開発環境

- .NET 10 SDK(`dotnet` が PATH にない場合は `export PATH="$HOME/.dotnet:$PATH"`)
- Unity Editor は不要です。Unity のファイルは直接パースします。

```sh
dotnet build
dotnet test                                    # フィクスチャを使うテストは ARIADNE_FIXTURES が無いとスキップされます
dotnet run --project src/Analyzer -- analyze <unity-project-path> --db graph.db
```

構成は [README.md](README.md) の「リポジトリ構成」と [docs/development/internals.md](docs/development/internals.md) を参照してください。**パーサーや差分更新を変更する前に、`internals.md` を必ず読んでください。**

## テストフィクスチャ

実プロジェクトを使うテストは、Asset Store 由来の第三者コンテンツを外部から渡したときだけ走ります(`ARIADNE_FIXTURES`、詳細は [docs/development/fixtures.md](docs/development/fixtures.md))。

- **フィクスチャのファイル(アセット、.meta、YAML のダンプ)を絶対にコミットしないでください。** Issue や PR にも貼らないでください。
- フィクスチャの YAML は実際の Unity Editor の出力です。手で(あるいは AI に)生成・編集しないでください。単体テストに書く小さなインライン YAML は構いません。
- フィクスチャに依存するテストには `[Fact]` ではなく `[FixtureFact]` / `[FixtureTheory]` を付けます。

## 守ってほしい制約

- Unity Editor API を使わないこと(ファイルを直接パースします)。
- 差分テスト(`RegexRefOracle` と完全パーサーの比較)を削除しないこと。回帰を防ぐ要です。
- スキーマを変えるときは `PRAGMA user_version` を上げること。差分更新の結果は、フル再構築と行単位で一致させてください。
- CLI オプション、MCP ツール、ref_kind、スキーマを変えたら、[docs/manual/](docs/manual/) を同じ PR で更新すること。

## Claude Code プラグインを更新するとき

プラグイン本体は `claude-plugins/` にあります。変更するときは次の点に注意してください。

- 配布されるのは外部 marketplace([murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin))経由です。プラグインを更新したら、marketplace 側の `unity-ariadne` エントリの `version` も `claude-plugins/.claude-plugin/plugin.json` に合わせて更新する必要があります(別リポジトリでの作業です)。手順は [docs/development/release.md](docs/development/release.md) の「marketplace のエントリを更新する」を参照してください。
- `plugin.json` の `version` は、リリースタグ(`v` を除く)と一致させます。リリース手順の中で更新します(通常の PR では変えません)。
- 導入手順([docs/manual/claude-code-plugin.md](docs/manual/claude-code-plugin.md))は、プラグインの挙動や前提を変えたときに同じ PR で更新します。

## 依存パッケージを更新するとき

NuGet の依存を変えたら、サードパーティのライセンス一覧を再生成してコミットしてください(Dependabot の PR でも必要です。CI の `third-party-notices` ジョブが差分を検出します)。

```sh
scripts/generate-third-party-notices.sh   # .NET 10 SDK と jq が必要です
```

## コミットメッセージ

先頭に gitmoji を付けます(例: `:sparkles: 〜を追加` / `:bug: 〜を修正` / `:memo: ドキュメント更新`)。1 つのコミットは 1 つの目的にまとめてください。

## ライセンス

貢献された内容は、このプロジェクトと同じ [MIT License](LICENSE) で提供されるものとします。
