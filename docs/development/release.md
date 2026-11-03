# リリースパイプライン(2026-08-16)

`v*.*.*` タグの push をトリガーに、`.github/workflows/release.yml` が GitHub Release を作成します。

## 何を配布するか、なぜその形か

**コンパイル済みバイナリではなく、CLI実行に必要な最小限のソース一式(`src/Analyzer` +
`src/Graph` の csproj + 全 `.cs`)を zip にして配布します。** 受け取った人は自分の環境で
`dotnet run --project src/Analyzer -- ...`(または `dotnet build`)を実行して使います。

これは意図的な選択です。当初はプラットフォームごとにコンパイル済み実行ファイル(self-contained
publish)を配ることを検討しましたが、未署名の Windows 向け exe は Windows Defender の
SmartScreen にほぼ確実に引っかかります。回避するには Azure Trusted Signing 等の外部サービスで
Authenticode 署名する必要があり、Azure アカウント・証明書プロファイル・OIDC フェデレーションの
セットアップという運用コストが発生します。ソース配布であればダウンロード直後に「未署名の実行
ファイルそのもの」を渡すことがなくなるため、この問題を配布方式ごと回避できます。

- `tests/`・`docs/` は zip に含めません。
- zip のルートに `LICENSE` / `THIRD-PARTY-NOTICES.md` / `NOTICE.txt` を同梱します。MSI / pkg にも同じ3ファイルが入ります(`THIRD-PARTY-NOTICES.md` は `scripts/generate-third-party-notices.sh` で生成し、`ci.yml` の `third-party-notices` ジョブが最新であることを検証します)。
- zip のファイル名は `unity-ariadne-<version>-src.zip`(`<version>` はタグ名から先頭の `v` を
  除いたもの)。ソース配布はプラットフォーム非依存なので matrix 化せず1個だけ作ります。
- zip のルートには、ワークフローが生成する簡易 `USAGE.md`(前提条件と最小限の実行例、詳しい
  マニュアルは `docs/manual/` を見るよう案内)を同梱します。
- タグのバージョンをアセンブリバージョンと一致させるため、`src/Analyzer` と `src/Graph` の
  親にあたる `src/` 直下に `Directory.Build.props`(`<Version>` にタグからGitのfixed`v`を
  除いた値を設定)を生成して同梱します。MSBuildは同じディレクトリ以下の全csprojに自動適用する
  ため、両プロジェクトのcsprojを個別に書き換える必要はありません。受け取った人が追加オプション
  なしで `dotnet build`/`dotnet run` するだけで、`AssemblyVersion`/`FileVersion` はタグの
  major.minor.patchに`.0`を付けた4分割形式(例: タグ `v1.2.3` → `1.2.3.0`。.NETの
  `AssemblyVersion` は4パートの数値のみでprerelease接尾辞を持てないため)、
  `InformationalVersion` はタグそのまま(例: `1.2.3`、prerelease接尾辞込み)になります。
  この `Directory.Build.props` はリポジトリにはコミットせず、リリース時にのみ生成するため、
  通常の開発ビルドには影響しません。

## インストーラー(MSI / pkg)

ソース zip とは別に、同じ Release へインストーラーを添付します。いずれも self-contained
publish なので利用者に .NET SDK / ランタイムは不要で、実行ファイル名は `ariadne`
(`src/Analyzer` の `AssemblyName`)です。`package-and-release` が Release を作成した後に、
`build-msi` / `build-pkg` ジョブが同じ Release へ追加アップロードします。

| ファイル | ビルド | 入る場所 |
|---|---|---|
| `unity-ariadne-<version>-win-x64.msi` | `installer/windows/build.ps1`(WiX v5、windows-latest) | `C:\Program Files\Unity Ariadne`、システム PATH に追加(アンインストールで除去) |
| `unity-ariadne-<version>-osx-arm64.pkg`(Intel Mac 向けは配布しません) | `installer/macos/build-pkg.sh`(pkgbuild、macos-latest) | `/usr/local/lib/unity-ariadne` と `/usr/local/bin/ariadne` へのリンク |

- MSI のバージョンは数値のみのため、タグのプレリリース接尾辞(`-rc1` 等)を除いた値を使います。
  `installer/windows/Package.wxs` の UpgradeCode は固定です(変えると上書きアップグレードが効きません)。
- `ci.yml` の `installer-windows` / `installer-macos` ジョブが push / PR ごとに MSI / pkg のビルドを検証します。

### 署名について(未署名)

**これらのインストーラーは未署名です。** 上で避けた問題が配布物に戻ってきます: Windows では
SmartScreen の警告(「詳細情報」→「実行」)、macOS では Gatekeeper の警告(右クリック→「開く」、
またはシステム設定の「このまま開く」)が出ます。回避には Authenticode 署名(Azure Trusted Signing 等)と
Apple Developer ID 署名+notarization が必要で、アカウントと証明書の運用コストが発生します。
必要になった時点でワークフローに署名ステップを足してください。

## `verify-build` ジョブが保証すること

配布前に `windows-latest` と `macos-latest` の2ランナーで `src/Analyzer` を
ビルドし、さらに実際に `analyze` → `report` を空のダミープロジェクトに対して実行するスモーク
テストを行います。`ci.yml` の `test` ジョブも `ubuntu-latest` / `windows-latest` / `macos-latest` の
マトリクスで走るため、通常の push / PR でも同じ3OSでテストされます。このスモークテストは、
`Microsoft.Data.Sqlite`(SQLitePCLRaw 経由のネイティブ SQLite バイナリ)が Windows / macOS
ランナー上でも実際にロードできることを、配布直前にもう一度確認する意味を持ちます。

## リリース手順

1. `develop` で `dotnet test` が green であることを確認する。
2. `develop` → `main` の Pull Request を作成してマージする(マージ後、`main` の CI が green であることを確認する)。
3. `main` に切り替えて最新を取得し、`claude-plugins/.claude-plugin/plugin.json` の `version` をリリースするバージョン(タグから `v` を除いた値)に更新してコミットする。**一致しないと `verify-plugin-version` ジョブが失敗します。** このコミットは `main` に積んだあと、`develop` にも取り込む(`main` → `develop` のマージ)。
4. `git tag vX.Y.Z && git push origin vX.Y.Z` — これだけで `release.yml` が発火します。
5. Actions の実行結果を確認し、`package-and-release` が作成した GitHub Release に
   `unity-ariadne-<version>-src.zip` が添付されていることを確認する。
6. 外部 marketplace([murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin))の `unity-ariadne` エントリの `version` を新バージョンに更新する(別リポジトリでの作業。下記「marketplace のエントリを更新する」)。

## Claude Code プラグイン

プラグイン本体は `claude-plugins/` にあります。`build-plugin` ジョブが、プラグイン本体と `LICENSE` を zip のルート直下に置いた `unity-ariadne-<version>-claude-plugin.zip` と、その SHA-256(`.sha256`)を同じ Release に添付します。SHA-256 はジョブのサマリーにも出力されます。

外部 marketplace のエントリは、次のどちらかで参照します(`claude plugin validate` と公式ドキュメントで確認済みの `source` 型です)。

```json
{ "name": "unity-ariadne",
  "source": { "source": "archive",
              "url": "https://github.com/murnana/unity-ariadne/releases/download/vX.Y.Z/unity-ariadne-X.Y.Z-claude-plugin.zip",
              "sha256": "<.sha256 ファイルの値>" } }
```

```json
{ "name": "unity-ariadne",
  "source": { "source": "git-subdir",
              "url": "https://github.com/murnana/unity-ariadne.git",
              "path": "claude-plugins",
              "ref": "vX.Y.Z" } }
```

現在の marketplace は、`ref` を付けない `git-subdir`(`main` に追従)で参照しています。`archive` は `sha256` で改ざんを検出できます(切り替えるなら推奨)。`git-subdir` は Release の成果物に依存せず、タグから直接取得します。プラグインは `ariadne` コマンド(MSI / pkg が PATH に入れる)だけに依存するため、.NET SDK は不要です。

### marketplace のエントリを更新する

プラグインを更新(`claude-plugins/` を変更してリリース)したら、marketplace 側の `unity-ariadne` エントリの `version` を `claude-plugins/.claude-plugin/plugin.json` の `version` に合わせます。利用者は `version` の文字列が変わるまでキャッシュ済みのコピーを使い続けるため、これを上げないと更新が届きません。

1. このリポジトリのリリース(上記の手順 1〜5)を終え、`main` の `plugin.json` に新しい `version` が入っていることを確認する。
2. [murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin) で `develop` から `feature/unity-ariadne-<version>` を切る(`develop` が無ければ `main` から作って push してから)。
3. `.claude-plugin/marketplace.json` の `unity-ariadne` の `version`(必要なら `description` も)を更新し、`claude plugin validate .` を実行する。
4. `develop` 宛てに PR を作る。利用者に届くのは、marketplace 側の `develop` → `main` のリリース PR がマージされた後。

詳しい手順は marketplace 側の [docs/plugins/unity-ariadne/README.md](https://github.com/murnana/murnana-claude-code-plugin/blob/main/docs/plugins/unity-ariadne/README.md) と `CONTRIBUTING.md` にあります。marketplace 側の `Check plugin versions` ワークフローはこのプラグインを検査しないため、`version` の一致は手で確認します。

## 公開時のリポジトリ設定(GitHub 上で手作業)

OSS として公開するとき、リポジトリの設定で次を行います。

- Public にし、Description と Topics(`unity` / `mcp` / `claude-code` / `dependency-graph` / `sqlite`)を設定する
- Settings → Security で **Private vulnerability reporting**、Dependabot alerts、Secret scanning を有効にする([SECURITY.md](../../SECURITY.md) の報告窓口です)
- `main` と `develop` のブランチ保護で、必須チェックに `test`(3 OS)/ `installer-windows` / `third-party-notices` / `analyze`(CodeQL)を指定し、直接 push を禁止して Pull Request 経由に限定する
- `main` へマージできる PR の head ブランチを `develop`(と Dependabot の `dependabot/*`)に限定するため、`main` のブランチ保護の必須チェックに `check-source-branch`([`main-source-branch.yml`](../../.github/workflows/main-source-branch.yml))を加える。GitHub の設定だけでは PR の head ブランチを制限できないための措置
- Settings → Actions → General の Workflow permissions を Read only にする(各ジョブは必要な権限を明示しています)
