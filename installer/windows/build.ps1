# Build the Windows installer (.msi). Needs the .NET SDK; WiX is restored as an MSBuild SDK from NuGet.
#   .\installer\windows\build.ps1 -Version 1.2.3 [-OutDir dist]
param(
  [Parameter(Mandatory)][string]$Version,
  [string]$OutDir = 'dist'
)
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$publish = Join-Path ([IO.Path]::GetTempPath()) "ariadne-publish-$([guid]::NewGuid())"
$numericVersion = $Version -replace '-.*$', ''   # MSI versions are numeric only

dotnet publish (Join-Path $repo 'src\Analyzer\UnityAriadne.Analyzer.csproj') `
  -c Release -r win-x64 --self-contained "-p:Version=$Version" --nologo -v quiet -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet build (Join-Path $PSScriptRoot 'UnityAriadne.wixproj') `
  -c Release "-p:ProductVersion=$numericVersion" "-p:PublishDir=$publish" -o $OutDir
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

Write-Host "Built $(Join-Path $OutDir "unity-ariadne-$numericVersion-win-x64.msi")"
