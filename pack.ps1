param(
    [ValidateSet('all','desktop','plugin')][string]$Component = 'all',
    [string]$Label = ('preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)
$ErrorActionPreference = 'Stop'
if ($Label -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$' -or $Label.Contains('..')) { throw 'Label must be a simple release name.' }
$releaseRoot = Join-Path $PSScriptRoot "artifacts/$Label"
if (Test-Path -LiteralPath $releaseRoot) { throw "Output already exists: $releaseRoot. Choose a new label." }
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
Push-Location -LiteralPath $PSScriptRoot
try {
    if ($Component -in @('all','desktop')) {
        $desktopRoot = Join-Path $releaseRoot 'desktop'
        dotnet publish 'XIVChat Desktop/XIVChat Desktop.csproj' -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $desktopRoot
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publishing failed.' }

        # WinAppSDK native MUI folders are unaffected by SatelliteResourceLanguages.
        # Keep English, Japanese, German, Chinese, French and neutral fallback resources.
        $cultures = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [Globalization.CultureInfo]::GetCultures([Globalization.CultureTypes]::AllCultures) | ForEach-Object { [void]$cultures.Add($_.Name) }
        $desktopFull = [IO.Path]::GetFullPath($desktopRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        $removed = @()
        foreach ($directory in Get-ChildItem -LiteralPath $desktopRoot -Directory) {
            $hasWinUiMui = Test-Path -LiteralPath (Join-Path $directory.FullName 'Microsoft.ui.xaml.dll.mui')
            if ((!$cultures.Contains($directory.Name) -and !$hasWinUiMui) -or $directory.Name -match '^(en|ja|de|zh|fr)(-|$)') { continue }
            $target = [IO.Path]::GetFullPath($directory.FullName)
            if (!$target.StartsWith($desktopFull, [StringComparison]::OrdinalIgnoreCase)) { throw 'Language directory escaped the publish directory.' }
            $removed += $directory.Name
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        if (!(Test-Path -LiteralPath (Join-Path $desktopRoot 'XIVChat Desktop.exe'))) { throw 'Desktop executable is missing.' }
        foreach ($language in @('en-US','zh-CN')) {
            if (!(Test-Path -LiteralPath (Join-Path $desktopRoot "Strings/$language/Resources.resw"))) { throw "Missing application translation: $language" }
        }
        $desktopProject = [xml](Get-Content -LiteralPath 'XIVChat Desktop/XIVChat Desktop.csproj' -Raw)
        $desktopVersion = $desktopProject.SelectSingleNode('//AssemblyVersion').InnerText
        [IO.Compression.ZipFile]::CreateFromDirectory($desktopRoot, (Join-Path $releaseRoot "XIVChatNext-Desktop-v$desktopVersion-win-x64.zip"))
        Write-Output "Removed $($removed.Count) unused runtime language folders; retained English, Japanese, German, Chinese, French and neutral resources."
    }
    if ($Component -in @('all','plugin')) {
        dotnet build 'XIVChatPlugin/XIVChatPlugin.csproj' -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
        # Distribute the SDK's final archive, never zip the entire build directory.
        $pluginArchive = Join-Path $PSScriptRoot 'XIVChatPlugin/bin/Release/XIVChatNext/latest.zip'
        $archive = [IO.Compression.ZipFile]::OpenRead($pluginArchive)
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
            if ('XIVChatNext.dll' -notin $entries -or 'XIVChatNext.json' -notin $entries) { throw 'Plugin files must be at the archive root.' }
            if ($entries | Where-Object { $_ -match '(?i)\.zip$|(^|/)XIVChatNext/' }) { throw 'Nested plugin package detected.' }
        } finally { $archive.Dispose() }
        Copy-Item -LiteralPath $pluginArchive -Destination (Join-Path $releaseRoot 'latest.zip')
    }
    $hashes = Get-ChildItem -LiteralPath $releaseRoot -Filter '*.zip' | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" }
    $hashes | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Distribution ready: $releaseRoot"
} finally { Pop-Location }
