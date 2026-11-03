# 変更履歴

このプロジェクトの注目すべき変更はすべてこのファイルに記録します。

フォーマットは [Keep a Changelog](https://keepachangelog.com/en/2.0.0/) に基づいており、
このプロジェクトは [Semantic Versioning](https://semver.org/spec/v2.0.0.html) に準拠しています。

## [Unreleased]

### Changed

- インストーラー(MSI / pkg)のビルドを、リリースタグの push と Actions からの手動実行(`Release` ワークフロー)のときだけ行うようにした。通常の push / PR ではビルドしない。手動実行ではインストーラーが実行ページの Artifacts(`installer-msi` / `installer-pkg`)に出力され、`version` 入力は省略すると `0.0.0-dev` になる。 https://github.com/murnana/unity-ariadne/pull/27
- リリースタグの push 時に、MSI / pkg が GitHub のリリースページにも添付されるようにした。 https://github.com/murnana/unity-ariadne/pull/27
- 依存ライブラリを更新した: MCP サーバーが使う ModelContextProtocol 2.2.0 → 2.3.0、Microsoft.Data.Sqlite と Microsoft.Extensions.Hosting 10.0.13 → 10.0.14。 https://github.com/murnana/unity-ariadne/pull/19 https://github.com/murnana/unity-ariadne/pull/15 https://github.com/murnana/unity-ariadne/pull/17

### Fixed

- macOS 向け pkg をリリースワークフローでビルドすると `installer/macos/build-pkg.sh` が `Permission denied` で失敗していたのを修正した。 https://github.com/murnana/unity-ariadne/pull/25

[Unreleased]: https://github.com/murnana/unity-ariadne/compare/v0.1.0...HEAD
