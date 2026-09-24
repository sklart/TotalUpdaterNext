param()

$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\plugin-catalog.json'
$evidencePath = Join-Path $PSScriptRoot '..\TotalUpdater.Next\Catalog\catalog-harvest-evidence.json'
function Save-Evidence([Collections.Generic.List[object]]$items) {
    $temporary = $evidencePath + '.tmp'
    [IO.File]::WriteAllText($temporary, ($items | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $evidencePath) { [IO.File]::Replace($temporary, $evidencePath, $evidencePath + '.bak'); Remove-Item -LiteralPath ($evidencePath + '.bak') -Force -ErrorAction SilentlyContinue }
    else { Move-Item -LiteralPath $temporary -Destination $evidencePath -Force }
}
$existing = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$usedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$usedAliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $existing) {
    [void]$usedIds.Add($entry.id)
    foreach ($alias in $entry.aliases) { [void]$usedAliases.Add($alias) }
}
try {
    [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
    $index = (Invoke-WebRequest -Uri 'https://totalcmd.net/get_plugins_list.php' -UseBasicParsing -TimeoutSec 20).Content
    $types = @{ packer='Wcx'; lister='Wlx'; fsplugin='Wfx'; content='Wdx' }
    $priority = 'catalogmaker|hlp|iclread|^img$|instexpl|mhtunpack|^msc$|multiarc|nscopy|3dsmax|ampview|archview|baseview|cadview|^font$|htmlview|scrlist|slister|swfview|tlister|ulister|^cloud$|acehelper|badcopy|devman|iecache|services|shelldetails|readpe|bitchaos|xpdfsearch'
    $rows = foreach ($line in ($index -split "`n")) {
        $f = $line.TrimEnd("`r") -split '\|'
        if ($f.Length -lt 5 -or -not $types.ContainsKey($f[4])) { continue }
        $date = [datetime]::MinValue
        [void][datetime]::TryParse($f[3], [ref]$date)
        [pscustomobject]@{ Id=$f[0].Trim(); Name=$f[1].Trim(); Type=$types[$f[4]]; Date=$date; Priority=([int]([regex]::IsMatch($f[0] + '|' + $f[1], $priority, 'IgnoreCase'))) }
    }
    $rows = $rows | Sort-Object @{Expression='Priority';Descending=$true}, @{Expression='Date';Descending=$true}
    $evidence = [Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $evidencePath) {
        foreach ($item in @(Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json)) {
            if ($null -eq $item -or [string]::IsNullOrWhiteSpace($item.id)) { continue }
            $evidence.Add($item)
            [void]$usedIds.Add($item.id)
            foreach ($alias in @($item.aliases)) { [void]$usedAliases.Add($alias) }
        }
    }
    foreach ($row in $rows) {
        if ($usedIds.Contains($row.Id)) { continue }
        $url = 'https://totalcmd.net/download.php?id=' + [uri]::EscapeDataString($row.Id)
        try {
            $bytes = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20).Content
            if ($bytes -is [string]) { continue }
            if ($bytes.Length -gt 8000000) { continue }
            $stream = [IO.MemoryStream]::new($bytes)
            try {
                $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
                try {
                    $code = $row.Type.Substring(1).ToLowerInvariant()
                    $pattern = '^.+\.(?:u?w' + $code + '(?:64)?)$'
                    $aliases = @($archive.Entries | ForEach-Object { [IO.Path]::GetFileName($_.FullName) } | Where-Object { $_ -match $pattern } | Sort-Object -Unique)
                } finally { $archive.Dispose() }
            } finally { $stream.Dispose() }
            if ($aliases.Count -eq 0 -or @($aliases | Where-Object { $usedAliases.Contains($_) }).Count -gt 0) { continue }
            $stems = @($aliases | ForEach-Object { $_ -replace '(?i)\.(?:u?w(?:cx|lx|fx|dx)(?:64)?)$', '' } | Sort-Object -Unique)
            if ($stems.Count -ne 1) { continue }
            $evidence.Add([pscustomobject]@{ id=$row.Id; name=$row.Name; type=$row.Type; aliases=$aliases; packageUrl=$url; verifiedUtc=(Get-Date).ToUniversalTime().ToString('yyyy-MM-dd') })
            [void]$usedIds.Add($row.Id)
            foreach ($alias in $aliases) { [void]$usedAliases.Add($alias) }
            Save-Evidence $evidence
            Write-Host ($evidence.Count.ToString() + ': ' + $row.Id + ' => ' + ($aliases -join ','))
        } catch { continue }
    }
    Save-Evidence $evidence
    Write-Host ('Harvested=' + $evidence.Count + '; catalog+harvest=' + ($existing.Count + $evidence.Count) + '; no artificial target')
} finally { }
