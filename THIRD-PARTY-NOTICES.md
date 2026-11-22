# Third-party notices

Unity Ariadne は、以下のサードパーティ製ソフトウェアを利用しています。各ソフトウェアの著作権は、それぞれの権利者に帰属します。

このファイルは `scripts/generate-third-party-notices.sh` で自動生成されます。手で編集しないでください(CI が生成結果との差分を検出して失敗します)。

## NuGet パッケージ

`src/Analyzer`(`src/Graph` を含む)が直接・間接に参照するパッケージです。テスト専用の依存は含みません。

| パッケージ | バージョン | ライセンス | URL |
|---|---|---|---|
| Microsoft.CodeAnalysis.Analyzers | 5.9.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.Common | 5.12.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.CSharp | 5.12.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.Data.Sqlite | 11.0.0 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.Data.Sqlite.Core | 11.0.0 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.Extensions.AI.Abstractions | 10.11.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Caching.Abstractions | 10.0.13 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Binder | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.CommandLine | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.FileExtensions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.Json | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Configuration.UserSecrets | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Diagnostics | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Diagnostics.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileProviders.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileProviders.Physical | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.FileSystemGlobbing | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Hosting | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Hosting.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Configuration | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Console | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Debug | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.EventLog | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.EventSource | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options.ConfigurationExtensions | 11.0.0 | MIT | https://dot.net/ |
| Microsoft.Extensions.Primitives | 11.0.0 | MIT | https://dot.net/ |
| ModelContextProtocol | 2.3.0 | Apache-2.0 | https://csharp.sdk.modelcontextprotocol.io/ |
| ModelContextProtocol.Core | 2.3.0 | Apache-2.0 | https://csharp.sdk.modelcontextprotocol.io/ |
| SQLite | 3.53.4 | LICENSE.txt | https://sqlite.org/ |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Apache-2.0 | - |
| SQLitePCLRaw.config.e_sqlite3 | 3.0.5 | Apache-2.0 | - |
| SQLitePCLRaw.core | 3.0.5 | Apache-2.0 | - |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.5 | Apache-2.0 | - |
| System.Diagnostics.DiagnosticSource | 11.0.0 | MIT | https://dot.net/ |
| System.Diagnostics.EventLog | 11.0.0 | MIT | https://dot.net/ |
| System.IO.Pipelines | 11.0.0 | MIT | https://dot.net/ |
| System.Text.Encodings.Web | 11.0.0 | MIT | https://dot.net/ |
| System.Text.Json | 11.0.0 | MIT | https://dot.net/ |
| YamlDotNet | 18.1.0 | MIT | https://github.com/aaubry/YamlDotNet/wiki |

## インストーラーに同梱されるもの

MSI / pkg は self-contained 発行のため、次のものも同梱されます。

| コンポーネント | ライセンス | URL |
|---|---|---|
| .NET ランタイム / ライブラリ(Microsoft) | MIT | https://github.com/dotnet/runtime |
| SQLite(`e_sqlite3` ネイティブライブラリ) | Public Domain | https://sqlite.org/copyright.html |

## 商標

Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。Unity Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。
