$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\plugin-catalog.json'
$evidencePath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\catalog-harvest-evidence.json'
$catalogText = [IO.File]::ReadAllText($catalogPath)
$catalog = @($catalogText | ConvertFrom-Json)
$evidence = @(Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json)
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$aliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Normalize-Alias([string]$name) {
    return ($name -replace '(?i)\.uw(cx|lx|fx|dx)$', '.w$1' -replace '(?i)\.w(cx|lx|fx|dx)64$', '.w$1').ToLowerInvariant()
}
foreach ($entry in $catalog) {
    [void]$ids.Add($entry.id)
    foreach ($alias in $entry.aliases) { [void]$aliases.Add((Normalize-Alias $alias)) }
}
$additions = [Collections.Generic.List[string]]::new()
foreach ($item in $evidence) {
    $id = if ($item.id -match '^\d+$') { ($item.name -replace '[^A-Za-z0-9-]+', '-').Trim('-').ToLowerInvariant() } else { $item.id.ToLowerInvariant() }
    if ($ids.Contains($id) -or @($item.aliases | Where-Object { $aliases.Contains((Normalize-Alias $_)) }).Count -gt 0) { continue }
    $entry = [ordered]@{
        id = $id; name = $item.name; type = $item.type; aliases = @($item.aliases); registrationAliases = @(); identityEvidence = 'VerifiedPackage'; verifiedAt = $item.verifiedUtc
        sources = @(
            [ordered]@{ provider='totalcmd.net-index'; id=$item.id; authority='CommunityCatalog'; purpose='Metadata'; priority=150 },
            [ordered]@{ provider='totalcmd.net'; id=$item.id; authority='CommunityCatalog'; purpose='MetadataAndDownload'; priority=100 }
        )
    }
    $additions.Add('  ' + ($entry | ConvertTo-Json -Compress -Depth 12))
    [void]$ids.Add($id)
    foreach ($alias in $item.aliases) { [void]$aliases.Add((Normalize-Alias $alias)) }
}
if ($additions.Count -gt 0) {
    $catalogText = $catalogText.TrimEnd() -replace '\s*\]$', (",`r`n" + ($additions -join ",`r`n") + "`r`n]")
    [IO.File]::WriteAllText($catalogPath, $catalogText + "`r`n", [Text.UTF8Encoding]::new($false))
}
Write-Host ('Added=' + $additions.Count + '; total=' + ($catalog.Count + $additions.Count))
