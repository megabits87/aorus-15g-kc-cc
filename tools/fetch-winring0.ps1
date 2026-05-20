#requires -Version 5.1
<#
.SYNOPSIS
  Downloads the WinRing0 native binaries needed by Gbt.Service and Gbt.Tools.DumpEc.

.DESCRIPTION
  WinRing0 (WinRing0x64.sys + WinRing0x64.dll) is the ring-0 driver that allows
  the service to read/write EC ports (0x62/0x66) and MSRs (notably 0x610 for
  PL1/PL2). The binaries are NOT committed to this repository; this script
  pulls a pinned release, verifies its SHA256, and copies it into the layout
  the projects expect.

  Default destination is src/Gbt.Hardware/runtimes/win-x64/native/. Override
  with -Destination if you want it next to the installed service.

.PARAMETER Version
  The WinRing0 release tag to fetch. The default tracks a known-signed build.

.PARAMETER Destination
  Where to place WinRing0x64.dll and WinRing0x64.sys.

.PARAMETER Force
  Re-download even if the files already exist with a matching checksum.

.EXAMPLE
  pwsh ./tools/fetch-winring0.ps1
  pwsh ./tools/fetch-winring0.ps1 -Destination "C:\Program Files\GbtControlCenter"
#>
[CmdletBinding()]
param(
    [string] $Version = '1.3.1',
    [string] $Destination,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Resolve repo root from the script location so the script works from any cwd.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $Destination) {
    $Destination = Join-Path $repoRoot 'src/Gbt.Hardware/runtimes/win-x64/native'
}
$null = New-Item -ItemType Directory -Force -Path $Destination

# Pinned expectations. Update when bumping -Version.
# Leave SHA256 entries empty to skip verification; the script will log a loud
# warning and require -Force to proceed when the checksum is empty.
$expected = @{
    'WinRing0x64.dll' = ''
    'WinRing0x64.sys' = ''
}

# We do not redistribute WinRing0 ourselves. The canonical signed release is
# published at https://github.com/GermanAizek/WinRing0 . When the SHA256s above
# are filled in for a known good release, this script switches to a pinned
# download. Until then, it prints clear remediation steps and refuses to
# silently grab a binary off the internet.
$source = "https://github.com/GermanAizek/WinRing0/releases/tag/$Version"

Write-Host "WinRing0 fetcher" -ForegroundColor Cyan
Write-Host "  version    : $Version"
Write-Host "  source     : $source"
Write-Host "  destination: $Destination"

$allKnown = $true
foreach ($file in $expected.Keys) {
    if ([string]::IsNullOrWhiteSpace($expected[$file])) {
        $allKnown = $false
        break
    }
}

if (-not $allKnown) {
    Write-Host ''
    Write-Warning 'No pinned SHA256 hashes have been recorded for WinRing0 yet.'
    Write-Host 'Manual procedure for now:' -ForegroundColor Yellow
    Write-Host '  1. Download a signed release from the source URL above.'
    Write-Host '  2. Extract WinRing0x64.dll and WinRing0x64.sys to:'
    Write-Host "       $Destination"
    Write-Host '  3. Compute SHA256 of each file and paste the values into'
    Write-Host '     the $expected table in this script.'
    Write-Host '  4. Re-run this script — it will then verify on every run.'
    Write-Host ''
    Write-Host 'See docs/THIRD-PARTY.md for the rationale.'
    return
}

foreach ($file in $expected.Keys) {
    $target = Join-Path $Destination $file
    if (Test-Path $target -and -not $Force) {
        $hash = (Get-FileHash $target -Algorithm SHA256).Hash
        if ($hash -ieq $expected[$file]) {
            Write-Host "  $file already present and checksum matches; skipping."
            continue
        }
        Write-Warning "$file present but checksum differs (have $hash, want $($expected[$file]))."
    }

    # Pinned download URL pattern. WinRing0 release assets are named consistently:
    $url = "https://github.com/GermanAizek/WinRing0/releases/download/$Version/$file"
    Write-Host "  Downloading $file from $url"
    Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing

    $hash = (Get-FileHash $target -Algorithm SHA256).Hash
    if ($hash -ine $expected[$file]) {
        Remove-Item $target -ErrorAction SilentlyContinue
        throw "$file SHA256 mismatch (got $hash, expected $($expected[$file])). Aborting."
    }

    Write-Host "  $file verified: $hash" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
