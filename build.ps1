param(
    [ValidateSet('relay','desktop','plugin','relay-tests','regression')][string]$Component = 'relay',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    switch ($Component) {
        'relay' { dotnet build src/XIVChat.Relay/XIVChat.Relay.csproj -c $Configuration }
        'desktop' { dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c $Configuration }
        'plugin' { dotnet build XIVChatPlugin/XIVChatPlugin.csproj -c $Configuration }
        'relay-tests' { dotnet run --project tests/Relay/RelayTests.csproj -c Debug --no-launch-profile }
        'regression' { dotnet run --project tests/Regression/Regression.csproj -c $Configuration }
    }
    if ($LASTEXITCODE -ne 0) { throw "Component failed: $Component ($LASTEXITCODE)" }
} finally { Pop-Location }
