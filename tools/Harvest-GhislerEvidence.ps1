$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\plugin-catalog.json'
$evidencePath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\catalog-harvest-evidence.json'
$catalog = @(Get-Content -Raw $catalogPath | ConvertFrom-Json)
$evidence = [Collections.Generic.List[object]]::new()
if (Test-Path $evidencePath) { foreach ($item in @(Get-Content -Raw $evidencePath | ConvertFrom-Json)) { $evidence.Add($item) } }
$usedAliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @($catalog | Where-Object { $_.id -notlike 'ghisler-*' })) { foreach ($alias in @($entry.aliases)) { [void]$usedAliases.Add($alias) } }
foreach ($item in @($evidence | Where-Object { $_.id -notlike 'ghisler-*' })) { foreach ($alias in @($item.aliases)) { [void]$usedAliases.Add($alias) } }
function Save-Atomic([string]$path, [string]$text) {
    $temp = $path + '.tmp'; [IO.File]::WriteAllText($temp, $text, [Text.UTF8Encoding]::new($false))
    if (Test-Path $path) { [IO.File]::Replace($temp, $path, $path + '.bak'); Remove-Item -LiteralPath ($path + '.bak') -Force -ErrorAction SilentlyContinue }
    else { Move-Item -LiteralPath $temp -Destination $path -Force }
}
$updated = 0
# Repair any partial prior run: an alias already owned by a non-Ghisler rule is never Ghisler evidence.
$invalid = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @($catalog | Where-Object { $_.id -like 'ghisler-*' -and $_.identityEvidence -eq 'VerifiedPackage' })) {
    if (@($entry.aliases | Where-Object { $usedAliases.Contains($_) }).Count -gt 0) {
        $entry.aliases = @(); $entry.identityEvidence = 'MetadataOnly'; $entry.verifiedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        foreach ($source in @($entry.sources | Where-Object { $_.provider -eq 'ghisler-plugins' })) { $source.purpose = 'Metadata' }
        [void]$invalid.Add($entry.id)
    } else { foreach ($alias in @($entry.aliases)) { [void]$usedAliases.Add($alias) } }
}
if ($invalid.Count -gt 0) {
    $kept = @($evidence | Where-Object { -not $invalid.Contains($_.id) }); $evidence.Clear(); foreach ($item in $kept) { $evidence.Add($item) }
}
foreach ($entry in $catalog | Where-Object { $_.identityEvidence -eq 'MetadataOnly' -and @($_.sources | Where-Object { $_.provider -eq 'ghisler-plugins' -and $_.packageUrl }).Count -gt 0 }) {
    $source = @($entry.sources | Where-Object { $_.provider -eq 'ghisler-plugins' -and $_.packageUrl })[0]
    try {
        $bytes = (Invoke-WebRequest -Uri $source.packageUrl -UseBasicParsing -TimeoutSec 20).Content
        if ($bytes -is [string] -or $bytes.Length -gt 8000000) { continue }
        $stream = [IO.MemoryStream]::new($bytes)
        try {
            $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                $code = $entry.type.Substring(1).ToLowerInvariant(); $pattern = '^.+\.(?:u?w' + $code + '(?:64)?)$'
                $aliases = @($archive.Entries | ForEach-Object { [IO.Path]::GetFileName($_.FullName) } | Where-Object { $_ -match $pattern } | Sort-Object -Unique)
            } finally { $archive.Dispose() }
        } finally { $stream.Dispose() }
        if ($aliases.Count -eq 0 -or @($aliases | Where-Object { $usedAliases.Contains($_) }).Count -gt 0) { continue }
        $entry.aliases = @($aliases); $entry.identityEvidence = 'VerifiedPackage'; $entry.verifiedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $source.purpose = 'MetadataAndDownload'
        foreach ($alias in $aliases) { [void]$usedAliases.Add($alias) }
        $evidence.Add([pscustomobject]@{ id=$entry.id; name=$entry.name; type=$entry.type; aliases=$aliases; packageUrl=$source.packageUrl; verifiedUtc=$entry.verifiedAt })
        $updated++; Write-Host ($updated.ToString() + ': ' + $entry.id + ' => ' + ($aliases -join ','))
    } catch { continue }
}
Save-Atomic $catalogPath (($catalog | ConvertTo-Json -Depth 12) + "`n")
Save-Atomic $evidencePath (($evidence | ConvertTo-Json -Depth 8) + "`n")
Write-Host ('Ghisler verified packages=' + $updated + '; evidence=' + $evidence.Count)
