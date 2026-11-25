# 変更履歴

このプロジェクトの注目すべき変更はすべてこのファイルに記録します。

フォーマットは [Keep a Changelog](https://keepachangelog.com/en/2.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

### Changed

- **破壊的変更**: プロジェクト名を Unity Ariadne から GuidGraph に変更した。実行コマンドは `ariadne` から `guidgraph` に、環境変数は `ARIADNE_FIXTURES` / `ARIADNE_BIN` / `ARIADNE_RETENTION_DAYS` から `GUIDGRAPH_*` に、MCP サーバー名とプラグイン名は `unity-ariadne` から `guidgraph` に、名前空間とプロジェクト名は `UnityAriadne.*` から `GuidGraph.*` になった。スキルのコマンドは `/guidgraph:search-graph` / `/guidgraph:update-graph` になり、プラグインのデータディレクトリも変わるため、`graph.db` は新しい場所で作り直される。Windows インストーラーの UpgradeCode は変えていないため、旧版の上に上書きできる。
- Claude Code プラグインの `plugin.json` から `version` を外した。アーリーアクセス版の marketplace が `develop` のコミットに追従できるようにするため。正式版の `version` は外部 marketplace のエントリにだけ書く。`release.yml` の `verify-plugin-version` ジョブは、タグとの一致ではなく `version` が無いことを検証する。

### Fixed

- `analyze --db` に guidgraph のグラフではないファイル(テキストファイル、ほかのアプリの SQLite データベースなど)を指定すると、確認なしで削除していた。中身があるそのようなファイルは、`--full` の有無にかかわらず変更せず、エラーで終了するようにした。読み取れないグラフ(破損、ロック中)も同様。古いスキーマのグラフは、これまでどおり自動で作り直す。

## [0.3.0] - 2026-11-18

### Added

- オントロジー検索を ariadne 本体に追加した。MCP ツール `search` と CLI コマンド `ariadne search`(同じ JSON のクエリと結果)で、出発点(アセット 1 つ、または種類・出どころ・パス・キーワードで絞ったアセット群)から、参照の種類・向き・ホップ数・終点の種類・パスを指定したステップをたどる。キーワードはファイル名・フォルダー名・C# の型名とメンバー名・XML ドキュメントコメント(日本語も可)と照合して関連の強い順に並べる。0 件のときは条件を段階的に緩め、結果ごとに根拠(参照経路、当たった手がかり、コメントの抜粋、宣言している型)と注記(Editor / Resources フォルダー、パッケージ由来など)、経路に出てくる関係の意味を返す。 https://github.com/murnana/unity-ariadne/pull/42
- キーワードと `path_contains` の照合で、全角・半角(NFKC)、大文字・小文字、ひらがな・カタカナの違いを無視するようにした(`ぼす` / `ﾎﾞｽ` で `ボス` に当たる)。 https://github.com/murnana/unity-ariadne/pull/42
- MCP ツール `describe_ontology`(プロジェクトに実在するアセットの種類と参照の種類、関係の意味・推移性・種類の組み合わせ、種類のカテゴリ)と `list_folders`(フォルダー構成と種類別のアセット数)を追加した。 https://github.com/murnana/unity-ariadne/pull/42
- MCP ツール `search_nodes` に、アセットの種類(`type`)・出どころ(`source`)での絞り込みと、どこからも参照されていないアセットだけを返す `unreferenced` を追加した。`query` は省略できるようになった。 https://github.com/murnana/unity-ariadne/pull/42
- `analyze` が C# の型宣言と、XML ドキュメントコメント(`<summary>` / `<remarks>`)付きのメンバーを新しい `code_symbols` テーブルに記録するようにした。memory.jsonl ではスクリプトのエンティティに `declares class X: <要約>` の observation として出力する。**スキーマを v3 に上げたため、既存の `graph.db` は次の `analyze` でフル再構築される。** https://github.com/murnana/unity-ariadne/pull/42
- 質問を `search` のクエリに直して呼び出し、根拠を添えて答える `search-graph` スキル(`/unity-ariadne:search-graph`)を追加した。 https://github.com/murnana/unity-ariadne/pull/42

## [0.2.0] - 2026-11-14

### Added

- Claude Code プラグインが `graph.db` をプロジェクトごとに自動で作成・管理するようにした。起動スクリプト `claude-plugins/scripts/launch.mjs` が `${CLAUDE_PLUGIN_DATA}` 配下に保存し、初回は `ariadne analyze` で作成し、30 日(`ARIADNE_RETENTION_DAYS` で変更可)使われていないプロジェクトを削除する。ariadne 本体は変更していない。**利用者の PC に Node.js(20.12 以降)が必要になる。** https://github.com/murnana/unity-ariadne/pull/38
- グラフを作成・差分更新して差分の内訳を報告する `update-graph` スキル(`/unity-ariadne:update-graph`)を追加した。 https://github.com/murnana/unity-ariadne/pull/38

### Changed

- GitHub Actions でのリリースタグの自動作成をやめ、タグは手動で push する手順に戻した(`GITHUB_TOKEN` でのタグ作成が HTTP 422 で失敗するため)。 https://github.com/murnana/unity-ariadne/pull/37

### Fixed

- `update-graph` スキルの `Code layer: re-analyzed` の説明を、実際の再解析条件(`.cs` の変更、アセットパス集合の変化、Addressables グループの変更)に合わせた。 https://github.com/murnana/unity-ariadne/pull/39

## [0.1.3] - 2026-11-09

### Fixed

- リリースワークフローが、公開済みのイミュータブルリリースへ後からアセットを追加しようとして失敗していたのを修正した。リリースを下書き(draft)で作成し、ソース zip・MSI・pkg・プラグイン zip をすべて添付したあとに、`publish-release` ジョブで公開するようにした。途中のジョブが失敗しても下書きのままなので、タグ名を再利用して再実行できる。 https://github.com/murnana/unity-ariadne/pull/34

## [0.1.2] - 2026-11-05

### Changed

- リリースのバージョンを 0.1.1 から 0.1.2 に変更した。`v0.1.1` のタグはイミュータブルリリースの制約で再利用できず、0.1.1 は公開されていない。0.1.1 に記載した変更はすべて 0.1.2 に含まれる。

## 0.1.1 - 2026-11-04(未公開)

### Changed

- インストーラー(MSI / pkg)のビルドを、リリースタグの push と Actions からの手動実行(`Release` ワークフロー)のときだけ行うようにした。通常の push / PR ではビルドしない。手動実行ではインストーラーが実行ページの Artifacts(`installer-msi` / `installer-pkg`)に出力され、`version` 入力は省略すると `0.0.0-dev` になる。 https://github.com/murnana/unity-ariadne/pull/27
- リリースタグの push 時に、MSI / pkg が GitHub のリリースページにも添付されるようにした。 https://github.com/murnana/unity-ariadne/pull/27
- 依存ライブラリを更新した: MCP サーバーが使う ModelContextProtocol 2.2.0 → 2.3.0、Microsoft.Data.Sqlite と Microsoft.Extensions.Hosting 10.0.13 → 10.0.14。 https://github.com/murnana/unity-ariadne/pull/19 https://github.com/murnana/unity-ariadne/pull/15 https://github.com/murnana/unity-ariadne/pull/17

### Fixed

- macOS 向け pkg をリリースワークフローでビルドすると `installer/macos/build-pkg.sh` が `Permission denied` で失敗していたのを修正した。 https://github.com/murnana/unity-ariadne/pull/25

[Unreleased]: https://github.com/murnana/unity-ariadne/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/murnana/unity-ariadne/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/murnana/unity-ariadne/compare/v0.1.3...v0.2.0
[0.1.3]: https://github.com/murnana/unity-ariadne/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/murnana/unity-ariadne/compare/v0.1.0...v0.1.2
