# Third-party notices

Unity Ariadne は、以下のサードパーティ製ソフトウェアを利用しています。各ソフトウェアの著作権は、それぞれの権利者に帰属します。

このファイルは `scripts/generate-third-party-notices.sh` で自動生成されます。手で編集しないでください(CI が生成結果との差分を検出して失敗します)。

## NuGet パッケージ

`src/Analyzer`(`src/Graph` を含む)が直接・間接に参照するパッケージです。テスト専用の依存は含みません。

| パッケージ | バージョン | ライセンス | URL |
|---|---|---|---|
| Microsoft.CodeAnalysis.Analyzers | 5.3.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.Common | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.CSharp | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.Data.Sqlite | 10.0.11 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.Data.Sqlite.Core | 10.0.11 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | MIT | https://dot.net/ |
| Microsoft.Extensions.Caching.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Binder | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Json | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Diagnostics | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileProviders.Physical | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Hosting | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Configuration | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Console | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Debug | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.EventLog | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.EventSource | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.10 | MIT | https://dot.net/ |
| Microsoft.Extensions.Primitives | 10.0.10 | MIT | https://dot.net/ |
| ModelContextProtocol | 2.2.0 | Apache-2.0 | https://csharp.sdk.modelcontextprotocol.io/ |
| ModelContextProtocol.Core | 2.2.0 | Apache-2.0 | https://csharp.sdk.modelcontextprotocol.io/ |
| SourceGear.sqlite3 | 3.50.4.5 | Public Domain (SQLite) | https://sqlite.org/ |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 | - |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.3 | Apache-2.0 | - |
| SQLitePCLRaw.core | 3.0.3 | Apache-2.0 | - |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.3 | Apache-2.0 | - |
| System.Diagnostics.EventLog | 10.0.10 | MIT | https://dot.net/ |
| YamlDotNet | 18.1.0 | MIT | https://github.com/aaubry/YamlDotNet/wiki |

## インストーラーに同梱されるもの

MSI / pkg は self-contained 発行のため、次のものも同梱されます。

| コンポーネント | ライセンス | URL |
|---|---|---|
| .NET ランタイム / ライブラリ(Microsoft) | MIT | https://github.com/dotnet/runtime |
| SQLite(`e_sqlite3` ネイティブライブラリ) | Public Domain | https://sqlite.org/copyright.html |

## 商標

Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。Unity Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。
