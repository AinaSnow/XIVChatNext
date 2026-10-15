param(
    [ValidateSet('all','desktop','plugin')][string]$Component = 'all',
    [string]$Label = ('preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    # Overrides the project version, e.g. to build an older installer for testing an update.
    [string]$VersionOverride
)
$ErrorActionPreference = 'Stop'
if ($VersionOverride -and $VersionOverride -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'VersionOverride must look like 1.2.3.' }
if ($Label -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$' -or $Label.Contains('..')) { throw 'Label must be a simple release name.' }
$releaseRoot = Join-Path $PSScriptRoot "artifacts/$Label"
if (Test-Path -LiteralPath $releaseRoot) { throw "Output already exists: $releaseRoot. Choose a new label." }
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
Push-Location -LiteralPath $PSScriptRoot
try {
    if ($Component -in @('all','desktop')) {
        $desktopRoot = Join-Path $releaseRoot 'desktop'
        # The .NET runtime ships in runtime\ instead of next to the app, so the folder stays readable.
        # Users still need nothing installed. WinUI's native files must stay beside the executable.
        $versionArgs = if ($VersionOverride) { @("-p:Version=$VersionOverride", "-p:AssemblyVersion=$VersionOverride", "-p:FileVersion=$VersionOverride") } else { @() }
        dotnet publish 'XIVChat Desktop/XIVChat Desktop.csproj' -c Release -r win-x64 --self-contained false -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false `
            -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=runtime @versionArgs -o $desktopRoot
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publishing failed.' }
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
        $runtimeVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App') -Directory |
            Where-Object { $_.Name -match '^10\.0\.[0-9]+$' -and (Test-Path -LiteralPath (Join-Path $dotnetRoot "host/fxr/$($_.Name)/hostfxr.dll")) } |
            Sort-Object { [Version]$_.Name } | Select-Object -Last 1 -ExpandProperty Name
        if (!$runtimeVersion) { throw 'No installed .NET 10 runtime to bundle.' }
        $runtimeRoot = Join-Path $desktopRoot 'runtime'
        New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'host/fxr'), (Join-Path $runtimeRoot 'shared/Microsoft.NETCore.App') | Out-Null
        Copy-Item -LiteralPath (Join-Path $dotnetRoot "host/fxr/$runtimeVersion") -Destination (Join-Path $runtimeRoot 'host/fxr') -Recurse
        Copy-Item -LiteralPath (Join-Path $dotnetRoot "shared/Microsoft.NETCore.App/$runtimeVersion") -Destination (Join-Path $runtimeRoot 'shared/Microsoft.NETCore.App') -Recurse
        # Windows metadata is only needed at build time.
        Get-ChildItem -LiteralPath $desktopRoot -Filter '*.winmd' -File | Remove-Item

        # WinAppSDK native MUI folders are unaffected by SatelliteResourceLanguages.
        # Keep English, Japanese, German, Chinese, French and neutral fallback resources.
        $cultures = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [Globalization.CultureInfo]::GetCultures([Globalization.CultureTypes]::AllCultures) | ForEach-Object { [void]$cultures.Add($_.Name) }
        $desktopFull = [IO.Path]::GetFullPath($desktopRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        $removed = @()
        foreach ($directory in Get-ChildItem -LiteralPath $desktopRoot -Directory) {
            $hasWinUiMui = Test-Path -LiteralPath (Join-Path $directory.FullName 'Microsoft.ui.xaml.dll.mui')
            if ($directory.Name -eq 'runtime' -or (!$cultures.Contains($directory.Name) -and !$hasWinUiMui) -or $directory.Name -match '^(en|ja|de|zh|fr)(-|$)') { continue }
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
        $desktopVersion = if ($VersionOverride) { $VersionOverride } else { $desktopProject.SelectSingleNode('//AssemblyVersion').InnerText }
        [IO.Compression.ZipFile]::CreateFromDirectory($desktopRoot, (Join-Path $releaseRoot "XIVChatNext-Desktop-v$desktopVersion-win-x64.zip"))
        Write-Output "Removed $($removed.Count) unused runtime language folders; retained English, Japanese, German, Chinese, French and neutral resources."
        Write-Output "Bundled .NET runtime $runtimeVersion in runtime\; $((Get-ChildItem -LiteralPath $desktopRoot -File).Count) files beside the executable."

        # Setup.exe installs to %LocalAppData%\XIVChatNext with desktop and Start menu shortcuts. Installed copies update from
        # the GitHub release, so upload every file in installer\ next to the ZIP.
        $installerRoot = Join-Path $releaseRoot 'installer'
        dotnet tool restore | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restore the vpk tool.' }
        dotnet vpk pack --packId XIVChatNext --packVersion $desktopVersion --packDir $desktopRoot --mainExe 'XIVChat Desktop.exe' `
            --packTitle 'XIVChat Next' --packAuthors AinaSnow --icon 'XIVChat Desktop/Resources/logo-c1.ico' --noPortable --outputDir $installerRoot
        if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
        if (!(Test-Path -LiteralPath (Join-Path $installerRoot 'releases.win.json'))) { throw 'The update feed is missing.' }
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
    $hashes = @(Get-ChildItem -LiteralPath $releaseRoot -Filter '*.zip') + @(Get-ChildItem -LiteralPath (Join-Path $releaseRoot 'installer') -File -Include '*.exe','*.nupkg' -Recurse -ErrorAction SilentlyContinue) | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" }
    $hashes | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Distribution ready: $releaseRoot"
} finally { Pop-Location }
