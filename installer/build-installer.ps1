<#
  build-installer.ps1 — publish MicForge self-contained and compile the Inno Setup installer.

  Produces installer\MicForge-Setup-<version>.exe, the asset the in-app updater downloads and the
  file you attach to a GitHub Release. Requires the .NET 10 SDK and Inno Setup 6 (ISCC).

  Usage:
    .\installer\build-installer.ps1
    .\installer\build-installer.ps1 -Version 1.9.0
#>
[CmdletBinding()]
param(
  [string]$Version,
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path $here -Parent
$proj = Join-Path $repo 'MicForge.csproj'

function Step($m) { Write-Host "==> $m" -ForegroundColor Green }

if (-not $Version) {
  $csproj = Get-Content $proj -Raw
  if ($csproj -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
  else { throw "Could not read <Version> from $proj; pass -Version." }
}
Step "MicForge installer v$Version"

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. Install it from https://jrsoftware.org/isdl.php" }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "The .NET SDK ('dotnet') was not found on PATH."
}

$publish = [IO.Path]::GetFullPath((Join-Path $here 'publish'))
$expectedPublish = [IO.Path]::GetFullPath((Join-Path $repo 'installer/publish'))
if ($publish -ne $expectedPublish) { throw "Unexpected publish directory: $publish" }
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
Step "Publishing ($Configuration, $Runtime, self-contained) ..."
& dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -o $publish | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Step "Compiling installer with ISCC ..."
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=publish" (Join-Path $here 'MicForge.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

$setup = Join-Path $here "MicForge-Setup-$Version.exe"
Step "Done -> $setup"
