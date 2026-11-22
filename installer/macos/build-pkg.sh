#!/usr/bin/env bash
# Build the macOS installer package (.pkg). Needs the .NET SDK and, on macOS, pkgbuild.
#   installer/macos/build-pkg.sh <osx-arm64> <version> [out-dir]
# Installs a self-contained build to /usr/local/lib/guidgraph and links /usr/local/bin/guidgraph.
set -euo pipefail

rid="${1:?usage: build-pkg.sh <osx-arm64> <version> [out-dir]}"
version="${2:?version required}"
out_dir="${3:-dist}"

repo="$(cd "$(dirname "$0")/../.." && pwd)"
export PATH="$HOME/.dotnet:$PATH"
numeric_version="${version%%-*}"
work="$(mktemp -d "${TMPDIR:-/tmp}/guidgraph-pkg.XXXXXX")"
trap 'rm -rf "$work"' EXIT

root="$work/root"
lib="$root/usr/local/lib/guidgraph"
mkdir -p "$lib" "$root/usr/local/bin" "$out_dir"

dotnet publish "$repo/src/Analyzer/GuidGraph.Analyzer.csproj" \
  -c Release -r "$rid" --self-contained -p:Version="$version" --nologo -v quiet -o "$lib"
cp "$repo/installer/NOTICE.txt" "$repo/LICENSE" "$repo/THIRD-PARTY-NOTICES.md" "$lib/"
ln -s ../lib/guidgraph/guidgraph "$root/usr/local/bin/guidgraph"

pkg="$out_dir/guidgraph-$version-$rid.pkg"
pkgbuild --root "$root" --install-location / \
  --identifier com.murnana.guidgraph --version "$numeric_version" \
  --ownership recommended "$pkg"
echo "Built $pkg"
