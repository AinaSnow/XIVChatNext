# Regression checks

Run from the repository root on Windows with the project's .NET SDK installed.

## Protocol and configuration files

```powershell
dotnet run --project tests/Regression/Regression.csproj
```

These tests exercise fragmented and truncated encrypted frames, frame size validation,
handshake cancellation/key agreement, TCP disposal, backup rotation/recovery, and failed
or interrupted configuration writes. They only use loopback sockets and a unique temporary directory.

Workbench coverage additionally checks legacy MessagePack readers/writers, identity isolation,
cursor pagination and packet-size limits, SQLite reopen/deduplication, retained favorite sources,
FTS5 and short Chinese searches, cancellation, failed-write rollback, corrupt/future database
preservation, and filtered pagination across 100,000 rows. Timing is printed for the large fixture.

## WinUI desktop smoke test

```powershell
$smokeTargets = Join-Path (Get-Location) 'tests/DesktopSmoke.targets'
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug "-p:CustomAfterMicrosoftCommonTargets=$smokeTargets"
& '.\XIVChat Desktop\bin\Debug\net9.0-windows10.0.26100.0\win-x64\XIVChat Desktop.exe'
```

This explicitly replaces the entry point for the test build only. It creates an in-memory
configuration and simulated messages; it does not read or save the user's configuration
or connect to the game. The window closes automatically and writes `desktop-smoke-results.txt`
beside the executable. A successful run exits with code 0.

Checks cover 10,000 variable-height messages across two tabs, realized control count,
bottom-following, reading position across arrivals/pruning/tab changes, unread counts,
returning to latest, clearing messages, backlog ordering, and desktop cleanup after EOF.

A simulated modern server also exercises capability negotiation, messages arriving before player
identity, replay/live interleaving, database persistence of another character's records, checkpoint
commit ordering and character switches. All history files are created in a unique temporary directory.

Rebuild without the test targets before running the regular desktop app:

```powershell
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild
```

## Game integration

```powershell
dotnet build XIVChatPlugin/XIVChatPlugin.csproj -c Debug
```

With Dalamud and the game running, connect the desktop, change the plugin port, and reconnect.
Verify that new chat, outgoing messages, login/logout, and territory changes still update.
Repeat the port change and unload/reload the plugin; verify the old port is immediately reusable
and each message is delivered once. This integration check requires a live game session.
