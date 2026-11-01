#!/usr/bin/env bash
# THIRD-PARTY-NOTICES.md を再生成します(リポジトリ直下に出力)。
#   scripts/generate-third-party-notices.sh
# 必要なもの: .NET 10 SDK、jq。ツールは .config/dotnet-tools.json の nuget-license(バージョン固定)。
# 依存パッケージ(NuGet の推移的依存を含む)を src/Analyzer から列挙し、名前順に整形します。
# 同じ依存なら出力は常に同一になります(CI が git diff で差分を検出します)。
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"

tmp="$(mktemp -d "${TMPDIR:-/tmp}/ariadne-notices.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT

dotnet tool restore >/dev/null
dotnet restore src/Analyzer/UnityAriadne.Analyzer.csproj >/dev/null
dotnet nuget-license \
  --input src/Analyzer/UnityAriadne.Analyzer.csproj \
  --include-transitive \
  --output Json \
  --file-output "$tmp/licenses.json" >/dev/null

# nuget-license がライセンス種別を判定できないパッケージは、ここで明示的に補います。
#   SourceGear.sqlite3 — SQLite 本体のビルド。SQLite はパブリックドメインです。
rows="$(jq -r '
  map(
    if .PackageId == "SourceGear.sqlite3" then .License = "Public Domain (SQLite)" else . end
    | .License = (if (.License // "") == "" then "(要確認)" else .License end)
  )
  | sort_by([(.PackageId | ascii_downcase), .PackageVersion])
  | .[]
  | "| \(.PackageId) | \(.PackageVersion) | \(.License) | \(.PackageProjectUrl // "-") |"
' "$tmp/licenses.json" | tr -d '\r')"

out="THIRD-PARTY-NOTICES.md"
{
  cat <<'HEADER'
# Third-party notices

Unity Ariadne は、以下のサードパーティ製ソフトウェアを利用しています。各ソフトウェアの著作権は、それぞれの権利者に帰属します。

このファイルは `scripts/generate-third-party-notices.sh` で自動生成されます。手で編集しないでください(CI が生成結果との差分を検出して失敗します)。

## NuGet パッケージ

`src/Analyzer`(`src/Graph` を含む)が直接・間接に参照するパッケージです。テスト専用の依存は含みません。

| パッケージ | バージョン | ライセンス | URL |
|---|---|---|---|
HEADER
  printf '%s\n' "$rows"
  cat <<'FOOTER'

## インストーラーに同梱されるもの

MSI / pkg は self-contained 発行のため、次のものも同梱されます。

| コンポーネント | ライセンス | URL |
|---|---|---|
| .NET ランタイム / ライブラリ(Microsoft) | MIT | https://github.com/dotnet/runtime |
| SQLite(`e_sqlite3` ネイティブライブラリ) | Public Domain | https://sqlite.org/copyright.html |

## 商標

Unity は、米国およびその他の地域における Unity Technologies またはその関連会社の商標または登録商標です。Unity Ariadne は Unity Technologies およびその関連会社によるスポンサーを受けておらず、提携関係にもありません。
FOOTER
} > "$out"

echo "Wrote $out ($(printf '%s\n' "$rows" | wc -l | tr -d ' ') packages)"
