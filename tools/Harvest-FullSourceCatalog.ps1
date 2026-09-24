$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\plugin-catalog.json'
$catalogText = [IO.File]::ReadAllText($catalogPath)
$catalog = @($catalogText | ConvertFrom-Json)
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $catalog) { [void]$ids.Add($entry.id) }
$types = @{ packer='Wcx'; lister='Wlx'; fsplugin='Wfx'; content='Wdx' }
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
$index = (Invoke-WebRequest -Uri 'https://totalcmd.net/get_plugins_list.php' -UseBasicParsing -TimeoutSec 20).Content
$additions = [Collections.Generic.List[string]]::new()
foreach ($line in ($index -split "`n")) {
    $f = $line.TrimEnd("`r") -split '\|'
    if ($f.Length -lt 5 -or -not $types.ContainsKey($f[4]) -or [string]::IsNullOrWhiteSpace($f[0]) -or [string]::IsNullOrWhiteSpace($f[1])) { continue }
    $id = $f[0].Trim().ToLowerInvariant(); if ($ids.Contains($id)) { continue }
    $entry = [ordered]@{ id=$id; name=$f[1].Trim(); type=$types[$f[4]]; version=$f[2].Trim(); aliases=@(); registrationAliases=@(); identityEvidence='MetadataOnly'; verifiedAt=(Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'); sources=@([ordered]@{ provider='totalcmd.net-index'; id=$f[0].Trim(); authority='CommunityCatalog'; purpose='Metadata'; priority=150 }) }
    $additions.Add('  ' + ($entry | ConvertTo-Json -Compress -Depth 8)); [void]$ids.Add($id)
}
$ghisler = (Invoke-WebRequest -Uri 'https://www.ghisler.com/plugins.htm' -UseBasicParsing -TimeoutSec 20).Content
$sections = @{
    'Packer extensions \(plugins\)' = 'Wcx'; 'File system extensions \(plugins\)' = 'Wfx'
    'Lister extensions \(plugins\)' = 'Wlx'; 'Content plugins' = 'Wdx'
}
foreach ($section in $sections.GetEnumerator()) {
    $match = [regex]::Match($ghisler, '(?is)<h4[^>]*>.*?' + $section.Key + '.*?</h4>.*?<table[^>]*>(?<rows>.*?)</table>')
    if (-not $match.Success) { continue }
    foreach ($row in [regex]::Matches($match.Groups['rows'].Value, '(?is)<tr[^>]*>(?<row>.*?)</tr>')) {
        $cells = @([regex]::Matches($row.Groups['row'].Value, '(?is)<td[^>]*>(?<cell>.*?)</td>'))
        if ($cells.Count -lt 2) { continue }
        $firstCell = $cells[0].Groups['cell'].Value -replace '(?is)<br\s*/?>', "`n" -replace '(?is)<.*?>', ''
        $lines = @(([Net.WebUtility]::HtmlDecode($firstCell) -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($lines.Count -eq 0 -or $lines[0] -eq 'Name/Ver.') { continue }
        $name = $lines[0]; $version = if ($lines.Count -gt 1 -and $lines[1] -match '^[0-9]+(?:\.[0-9A-Za-z]+){0,4}') { $lines[1] } else { '' }
        $zip = [regex]::Match($cells[1].Groups['cell'].Value, 'https?://[^"''\s>]+\.zip(?:\?[^"''\s>]*)?', 'IgnoreCase').Value
        if ([string]::IsNullOrWhiteSpace($zip)) { continue }
        $baseId = ('ghisler-' + $section.Value.ToLowerInvariant() + '-' + ($name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-'))
        if ([string]::IsNullOrWhiteSpace($baseId) -or $baseId.EndsWith('-')) { continue }
        $id = $baseId; $ordinal = 2; while ($ids.Contains($id)) { $id = $baseId + '-' + $ordinal; $ordinal++ }
        $entry = [ordered]@{ id=$id; name=$name; type=$section.Value; version=$version; aliases=@(); registrationAliases=@(); identityEvidence='MetadataOnly'; verifiedAt=(Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'); sources=@([ordered]@{ provider='ghisler-plugins'; id=$name; packageUrl=$zip; authority='OfficialTotalCommander'; purpose='Metadata'; priority=200 }) }
        $additions.Add('  ' + ($entry | ConvertTo-Json -Compress -Depth 8)); [void]$ids.Add($id)
    }
}
if ($additions.Count -gt 0) { $updated = $catalogText.TrimEnd() -replace '\s*\]$', (",`r`n" + ($additions -join ",`r`n") + "`r`n]"); $temporary = $catalogPath + '.tmp'; [IO.File]::WriteAllText($temporary, $updated + "`r`n", [Text.UTF8Encoding]::new($false)); [IO.File]::Replace($temporary, $catalogPath, $catalogPath + '.bak'); Remove-Item -LiteralPath ($catalogPath + '.bak') -Force -ErrorAction SilentlyContinue }
Write-Host ('Added metadata-only source records=' + $additions.Count + '; total=' + ($catalog.Count + $additions.Count))
