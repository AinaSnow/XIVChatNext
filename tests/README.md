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

Friend coverage checks bounded complete pagination, legacy fields, duplicate IDs, login episodes,
cached/empty snapshots, refresh failures, concurrent request coalescing, timeout, queue limits,
and the v1-to-v2 database migration with its pre-migration backup and source/owner partitions.

Stability coverage checks outgoing byte/count budgets including active writes, atomic game-command
batches, owner/login/channel guards, disconnect cancellation, immediate memory-history limits,
subscription unions and legacy layouts, bounded metadata caches, UTF-8 splitting with complete
Tell targets, and relay buffering/transport completion. The regression runner has 30 checks.

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

The modern server also sends a 70-person friend snapshot in three pages. Checks verify automatic
and manual requests, ownership fields, no partial publication/persistence, error preservation,
and rejection of pages from the previous character.

The stability additions verify channel/message submission order, captured owner/channel guards,
localized command rejection, and the history/view/notification subscription union. There are
36 desktop checks, followed by the completion marker.

Rebuild without the test targets before running the regular desktop app:

```powershell
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild
```

## Game integration

The live single-character send/receive, SQLite persistence, offline replay and two reconnect checks
performed on 2026-09-09 are recorded in [the integration report](../docs/LIVE_INTEGRATION_2026-09-09.md),
including the scenarios that still need real-game verification.

The 2026-09-10 stability run additionally verifies plugin reload, native channel initialization and
change/restoration, concurrent friend requests from two clients, stale-command rejection,
live channel subscriptions and explicit port application. See [the stability report](../docs/PHASE2_STABILITY_2026-09-10.md).

Before loading a Debug build, check the real plugin entry point using the same uninitialized-object
constructor invocation used by Dalamud:

```powershell
dotnet run --project tests/PluginLoadRegression/PluginLoadRegression.csproj -- 'XIVChatPlugin/bin/Debug/XIVChatNext.dll' "$env:APPDATA\XIVLauncher\addon\Hooks\dev"
```

This requires .NET 10 and the local Dalamud assemblies. It supplies proxy services to the actual
compiled constructor and deliberately stops at `GetPluginConfig`, before hooks or networking.
It verifies all constructor dependencies were assigned and that the development manifest and
debug symbols exist. It does not load the plugin into the game or read/write game configuration.

```powershell
dotnet build XIVChatPlugin/XIVChatPlugin.csproj -c Debug
```

With Dalamud and the game running, connect the desktop, change the plugin port, and reconnect.
Verify that new chat, outgoing messages, login/logout, and territory changes still update.
Repeat the port change and unload/reload the plugin; verify the old port is immediately reusable
and each message is delivered once. This integration check requires a live game session.
