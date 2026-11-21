# Builds src\FoxFix.Core\Tweaks\Katalog.json from a Win11Debloat release (MIT, https://github.com/Raphire/Win11Debloat).
#
# Usage:  powershell -File tools\win11debloat-katalog.ps1 -Zip C:\path\Win11Debloat-2026.10.30.zip
#
# Only plain registry value changes are taken: values set to a number, a text or bytes, and values removed. Everything that
# cannot be put back exactly (whole keys deleted, the classes root, other users' hives, app removal, PowerShell steps) is
# left out and listed at the end. The Turkish texts come from Win11Debloat's own tr-TR translation.

param([Parameter(Mandatory = $true)][string]$Zip)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ("w11d-" + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $Zip -DestinationPath $work
$base = (Get-ChildItem $work -Directory | Select-Object -First 1).FullName

function Read-Json($path) { Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json }

$en = Read-Json "$base\Config\Features.json"
$trFeatures = Read-Json "$base\Config\Languages\tr-TR\Features.json"
$trCategories = Read-Json "$base\Config\Languages\tr-TR\Categories.json"

$categoryIdByName = @{}
foreach ($c in $en.Categories) { $categoryIdByName[$c.Name] = $c.CategoryId }

function Get-Property($object, $name) {
    if ($null -eq $object) { return $null }
    $p = $object.PSObject.Properties[$name]
    if ($p) { return $p.Value }
    return $null
}

$skipped = New-Object System.Collections.Generic.List[string]

# ---- .reg parser -------------------------------------------------------------------------------------------------------
function Convert-RegFile($path) {
    # Most files are UTF-16 (with a byte order mark), a few are UTF-8.
    $bytes = [IO.File]::ReadAllBytes($path)
    $text = if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2) }
            else { [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF) }
    $lines = $text -split "`r?`n"

    # join lines that continue with a trailing backslash
    $joined = New-Object System.Collections.Generic.List[string]
    $buffer = $null
    foreach ($line in $lines) {
        $t = $line.TrimEnd()
        if ($null -ne $buffer) { $t = $buffer + $t.TrimStart(); $buffer = $null }
        if ($t.EndsWith('\') -and $t -match '=hex') { $buffer = $t.Substring(0, $t.Length - 1); continue }
        $joined.Add($t)
    }

    $entries = New-Object System.Collections.Generic.List[object]
    $hive = $null; $key = $null
    foreach ($t in $joined) {
        if ($t -eq '' -or $t.StartsWith(';') -or $t.StartsWith('Windows Registry Editor')) { continue }

        if ($t -match '^\[(-?)(HKEY_[A-Z_]+)\\?(.*)\]$') {
            if ($Matches[1] -eq '-') { return @{ Reason = 'deletes a whole key' } }
            $hive = switch ($Matches[2]) {
                'HKEY_CURRENT_USER' { 'CurrentUser' }
                'HKEY_LOCAL_MACHINE' { 'LocalMachine' }
                default { $null }
            }
            if ($null -eq $hive) { return @{ Reason = "uses $($Matches[2])" } }
            $key = $Matches[3]
            continue
        }

        if ($null -eq $key) { return @{ Reason = "value before any key: $t" } }

        if ($t -match '^(@|"((?:[^"\\]|\\.)*)")=(.*)$') {
            $name = if ($Matches[1] -eq '@') { '' } else { ($Matches[2] -replace '\\(.)', '$1') }
            $data = $Matches[3]
            if ($data -eq '-') {
                $entries.Add([pscustomobject]@{ hive = $hive; key = $key; name = $name; kind = 'delete'; data = '' })
            }
            elseif ($data -match '^dword:([0-9a-fA-F]{8})$') {
                $value = [BitConverter]::ToInt32([BitConverter]::GetBytes([Convert]::ToUInt32($Matches[1], 16)), 0)
                $entries.Add([pscustomobject]@{ hive = $hive; key = $key; name = $name; kind = 'dword'; data = [string]$value })
            }
            elseif ($data -match '^"((?:[^"\\]|\\.)*)"$') {
                $entries.Add([pscustomobject]@{ hive = $hive; key = $key; name = $name; kind = 'string'; data = ($Matches[1] -replace '\\(.)', '$1') })
            }
            elseif ($data -match '^hex:([0-9a-fA-F,\s]*)$') {
                $bytes = ($Matches[1] -replace '\s', '').ToLowerInvariant()
                $entries.Add([pscustomobject]@{ hive = $hive; key = $key; name = $name; kind = 'binary'; data = $bytes })
            }
            else { return @{ Reason = "unsupported value type: $data" } }
            continue
        }

        return @{ Reason = "unrecognised line: $t" }
    }

    if ($entries.Count -eq 0) { return @{ Reason = 'no values' } }
    return @{ Entries = $entries }
}

# ---- features ------------------------------------------------------------------------------------------------------------
$grouped = @{}
foreach ($g in $en.UiGroups) { foreach ($v in $g.Values) { foreach ($f in $v.FeatureIds) { $grouped[$f] = $g.GroupId } } }

$tweaks = New-Object System.Collections.Generic.List[object]
$taken = @{}
foreach ($f in $en.Features) {
    if (-not $f.RegistryKey) { $skipped.Add("$($f.FeatureId): not a registry change (done by a script or app removal upstream)"); continue }

    $parsed = Convert-RegFile (Join-Path "$base\Regfiles" $f.RegistryKey)
    if ($parsed.Reason) { $skipped.Add("$($f.FeatureId): $($parsed.Reason)"); continue }

    $tr = Get-Property $trFeatures.Features $f.FeatureId
    $title = Get-Property $tr 'Label'
    if (-not $title) { $title = $f.Label }
    $description = Get-Property $tr 'ToolTip'
    if (-not $description) { $description = '' }
    $undoTitle = Get-Property $tr 'UndoLabel'

    $categoryId = $null
    if ($f.Category) { $categoryId = $categoryIdByName[$f.Category] }

    # What Windows has by default: Win11Debloat's own undo file, for settings that stand alone (a group's default is its default choice).
    $defaults = $null
    if ($f.RegistryUndoKey -and -not $grouped.ContainsKey($f.FeatureId)) {
        $undo = Convert-RegFile (Join-Path "$base\Regfiles\Undo" $f.RegistryUndoKey)
        if (-not $undo.Reason) { $defaults = $undo.Entries.ToArray() }
        else { $skipped.Add("$($f.FeatureId): no Windows default ($($undo.Reason))") }
    }
    $min = 0; if ($f.MinVersion) { $min = [int]$f.MinVersion }
    $max = 0; if ($f.MaxVersion) { $max = [int]$f.MaxVersion }
    $tweaks.Add([pscustomobject]@{
        id = $f.FeatureId
        category = $categoryId
        title = $title
        description = $description
        minBuild = $min
        maxBuild = $max
        entries = [object[]]$parsed.Entries.ToArray()
        undoTitle = $undoTitle
        defaults = $defaults
    })
    $taken[$f.FeatureId] = $true
}

# ---- groups of choices -----------------------------------------------------------------------------------------------------
$groups = New-Object System.Collections.Generic.List[object]
foreach ($g in ($en.UiGroups | Sort-Object { [int]$_.Priority })) {
    $trGroup = Get-Property $trFeatures.UiGroups $g.GroupId
    $trValues = Get-Property $trGroup 'Values'
    $options = New-Object System.Collections.Generic.List[object]
    foreach ($v in $g.Values) {
        $ids = @($v.FeatureIds | Where-Object { $taken.ContainsKey($_) })
        if ($ids.Count -ne @($v.FeatureIds).Count) { continue }
        $label = $null
        if ($trValues -and $v.FeatureIds.Count -gt 0) { $label = Get-Property $trValues $v.FeatureIds[0] }
        if (-not $label) { $label = $v.Label }
        $options.Add([pscustomobject]@{ label = $label; tweakIds = $ids })
    }
    if ($options.Count -lt 2) { $skipped.Add("group $($g.GroupId): fewer than two usable choices"); continue }

    $title = Get-Property $trGroup 'Label'; if (-not $title) { $title = $g.Label }
    $description = Get-Property $trGroup 'ToolTip'; if (-not $description) { $description = '' }
    # a few groups name their category by its English name instead of its id
    $groupCategory = $g.Category
    if ($categoryIdByName.ContainsKey($groupCategory)) { $groupCategory = $categoryIdByName[$groupCategory] }
    $groups.Add([pscustomobject]@{ id = $g.GroupId; category = $groupCategory; title = $title; description = $description; options = $options.ToArray() })
}

# a tweak that is a choice in a group is shown only there
$inGroup = @{}
foreach ($g in $groups) { foreach ($o in $g.options) { foreach ($id in $o.tweakIds) { $inGroup[$id] = $true } } }
$final = New-Object System.Collections.Generic.List[object]
foreach ($t in $tweaks) {
    if ($grouped.ContainsKey($t.id) -and -not $inGroup.ContainsKey($t.id)) { $skipped.Add("$($t.id): its group was left out") } else { $final.Add($t) }
}

$categories = @($en.Categories | ForEach-Object {
        $label = Get-Property (Get-Property $trCategories $_.CategoryId) 'Label'
        if (-not $label) { $label = $_.Name }
        [pscustomobject]@{ id = $_.CategoryId; title = $label }
    })

$catalog = [pscustomobject]@{
    source = 'Win11Debloat (MIT) https://github.com/Raphire/Win11Debloat'
    categories = $categories
    tweaks = $final.ToArray()
    groups = $groups.ToArray()
}

$out = Join-Path $root 'src\FoxFix.Core\Tweaks\Katalog.json'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$json = $catalog | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($out, $json, (New-Object Text.UTF8Encoding($false)))

Remove-Item $work -Recurse -Force
"Written: $out"
"Tweaks: $($final.Count), groups: $($groups.Count)"
""
"Left out:"
$skipped | ForEach-Object { "  $_" }
