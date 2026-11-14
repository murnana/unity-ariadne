# 変更履歴

このプロジェクトの注目すべき変更はすべてこのファイルに記録します。

フォーマットは [Keep a Changelog](https://keepachangelog.com/en/2.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

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

[Unreleased]: https://github.com/murnana/unity-ariadne/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/murnana/unity-ariadne/compare/v0.1.3...v0.2.0
[0.1.3]: https://github.com/murnana/unity-ariadne/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/murnana/unity-ariadne/compare/v0.1.0...v0.1.2
