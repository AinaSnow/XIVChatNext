$ErrorActionPreference = 'Stop'
$desktop = Join-Path $PSScriptRoot '../XIVChat Desktop'
$resources = @{}
foreach ($language in @('en-US', 'zh-CN')) {
    $xml = [xml][IO.File]::ReadAllText((Join-Path $desktop "Strings/$language/Resources.resw"))
    $map = @{}
    foreach ($entry in $xml.root.data) {
        if ($map.ContainsKey($entry.name)) { throw "Duplicate key in ${language}: $($entry.name)" }
        if ([string]::IsNullOrWhiteSpace($entry.value)) { throw "Empty translation: $language $($entry.name)" }
        $map[$entry.name] = $entry.value
    }
    $resources[$language] = $map
}
$english = $resources['en-US']; $chinese = $resources['zh-CN']
if (Compare-Object @($english.Keys) @($chinese.Keys)) { throw 'Language resource keys differ' }
foreach ($key in $english.Keys) {
    $en = [regex]::Matches($english[$key], '\{\d+[^}]*\}').Value | Sort-Object
    $zh = [regex]::Matches($chinese[$key], '\{\d+[^}]*\}').Value | Sort-Object
    if (($en -join '|') -ne ($zh -join '|')) { throw "Format arguments differ: $key" }
}
$references = [Collections.Generic.HashSet[string]]::new()
foreach ($file in Get-ChildItem $desktop -Recurse -File -Include '*.cs','*.xaml' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*.Designer.cs' }) {
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, '(?:GetString\(|\bL\(|Localize\.(?:Text|Content|Header)=)"([\w.]+)"')) {
        $key = $match.Groups[1].Value
        # Prefixes ending in a dot are completed by an enum or runtime state.
        if (!$key.EndsWith('.')) { [void]$references.Add($key) }
    }
}
foreach ($key in $references) { if (!$english.ContainsKey($key)) { throw "Missing referenced resource: $key" } }
"PASS $($english.Count) keys per language; no duplicates, blanks or format mismatches; $($references.Count) literal references resolve"
