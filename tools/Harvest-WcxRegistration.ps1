param(
    [string]$PackageDirectory = "",
    [string]$ProbeExe = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testExe = Join-Path $root 'TotalUpdater.Next.Tests\bin\Release\net48\TotalUpdater.Next.Tests.exe'
if (-not (Test-Path -LiteralPath $testExe)) { throw "Build TotalUpdater.Next.Tests Release before harvesting WCX registration." }

# This maintenance entry point intentionally does not download arbitrary URLs or
# write catalog evidence on its own. A caller must first place verified ZIPs in
# PackageDirectory; unverified packages never receive caps/evidence.
& $testExe --audit-wcx-registration
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    Write-Host 'No PackageDirectory supplied: audit only; no WCX registration evidence was created.'
    exit 0
}
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) { throw "PackageDirectory does not exist: $PackageDirectory" }
if ([string]::IsNullOrWhiteSpace($ProbeExe) -or -not (Test-Path -LiteralPath $ProbeExe -PathType Leaf)) { throw 'ProbeExe must be the built TotalUpdater.exe for isolated --wcx-probe execution.' }
Write-Host 'Local verified-package harvest requires explicit review of ZIP identity and will not mutate catalog automatically.'
