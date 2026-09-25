param([string]$EvidencePath = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$catalogPath = Join-Path $root 'TotalUpdater.Next\Catalog\plugin-catalog.json'
if (!$EvidencePath) { $EvidencePath = Join-Path $root 'TotalUpdater.Next\Catalog\wcx-registration-harvest.json' }
if (!(Test-Path $EvidencePath -PathType Leaf)) { throw "Evidence file not found: $EvidencePath" }
$catalog = @(Get-Content $catalogPath -Raw | ConvertFrom-Json)
$findings = @(Get-Content $EvidencePath -Raw | ConvertFrom-Json)
$ids = @{}
$findings | Where-Object { $_.status -eq 'VerifiedRegistration' } | ForEach-Object { if($ids.ContainsKey($_.id)){ throw "Duplicate evidence id: $($_.id)" }; $ids[$_.id]=$true }
$merged = 0
foreach ($finding in $findings) {
  if ($finding.status -ne 'VerifiedRegistration' -or !$finding.evidence) { continue }
  $e = $finding.evidence
  $extensions=@($e.extensions | ForEach-Object { "$($_)".Trim().TrimStart('.') })
  if (!$e.packageSha256 -or ($e.packageSha256 -notmatch '^[0-9a-fA-F]{64}$') -or (($e.x86BinarySha256 -notmatch '^[0-9a-fA-F]{64}$') -and ($e.x64BinarySha256 -notmatch '^[0-9a-fA-F]{64}$')) -or $e.packerCaps -lt 0 -or !$extensions -or !$e.verifiedUtc -or !$e.source -or @($extensions | Where-Object { !$_.Length -or $_ -match '[=,\[\]\r\n]' }).Count -gt 0 -or @($extensions | Select-Object -Unique).Count -ne $extensions.Count) { continue }
  $e.extensions=$extensions
  $entry = @($catalog | Where-Object { $_.id -ieq $finding.id -and $_.type -ieq 'Wcx' })
  if ($entry.Count -ne 1) { continue }
  # Deliberately assign this one property only: all catalogue identity/source/version fields remain byte-for-byte as loaded.
  $entry[0].wcxRegistration = $e
  $merged++
}
$temp = "$catalogPath.$([guid]::NewGuid().ToString('N')).tmp"
[IO.File]::WriteAllText($temp, ($catalog | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temp -Destination $catalogPath -Force
Write-Host "Merged VerifiedRegistration=$merged"
