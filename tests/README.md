# Regression checks

## Phase 5 cards and favorites

Live equipment follow-up (2026-09-12): the installed-game suite now has 16 checks,
including ordinary default restriction rows, BST's unnamed category flag, real HQ deltas,
and special scaling equipment. See [live results](../docs/PHASE5_LIVE_INTEGRATION_2026-09-12.md).
To inspect the latest equipment received and persisted by the normal desktop client:

```powershell
dotnet run --project tests/EquipmentSnapshotInspect/EquipmentSnapshotInspect.csproj
# Optional argument: a different history.sqlite3 path. The database is opened read-only.
```

The persisted snapshot is always non-live; verify the current UI and connection separately.

The regression runner now has 47 groups. Card coverage includes bounded requests and malformed
payloads, HQ/ring/job/level comparisons, v5 migration, source/owner isolation, annotations that
survive snapshot refresh, and deterministic Chinese API cache/offline/oversize/cancellation checks.

The card UI suite uses a loopback encrypted server and a local HTTP fixture (no account or user
configuration is loaded). It exercises WebView card actions, map metadata, pagination, late replies,
favorites/source context, Chinese/original text, logout/role isolation and offline snapshots.

```powershell
$cardTargets = Join-Path (Get-Location) 'tests/DesktopCardsSmoke.targets'
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug "-p:CustomAfterMicrosoftCommonTargets=$cardTargets"
& '.\XIVChat Desktop\bin\Debug\net9.0-windows10.0.26100.0\win-x64\XIVChat Desktop.exe'
# Inspect cards-smoke-results.txt beside the executable; success ends with All card checks completed.
# Restore the real entry point after testing:
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild
```

Installed static-game-data checks require the local Dalamud SDK and an existing game installation:

```powershell
dotnet run --project tests/GameDataRegression/GameDataRegression.csproj -- 'C:/path/to/FINAL FANTASY XIV Online/game/sqpack'
```

This reads files only. It does not load the plugin into a game or validate real equipment changes.
See [phase 5 implementation and limits](../docs/PHASE5_CARDS_2026-09-12.md).

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

Active presence adds wire/CID/request/epoch validation, thirty-second retry limits,
one-minute freshness, cache timestamps, coalescing, bounded queues and timeout/reconnect
isolation. The full regression runner has 36 groups. The WinUI suite now has 69 checks,
including encrypted presence requests, friend-row/conversation-header status, and role
changes. Native integration findings are in [friend presence](../docs/friend-presence.md).

Stability coverage checks outgoing byte/count budgets including active writes, atomic game-command
batches, owner/login/channel guards, disconnect cancellation, immediate memory-history limits,
subscription unions and legacy layouts, bounded metadata caches, UTF-8 splitting with complete
Tell targets, and relay buffering/transport completion. The regression runner has 30 checks.

## WinUI desktop smoke test

Localization resources can be checked with `./tests/LocalizationAudit.ps1`. This verifies
key parity, duplicate/empty values, format arguments and static resource references.
The desktop smoke suite also checks language changes on already open dialogs, generated
channel options and preservation of unsaved input. The live integration follow-up adds
offline composer guidance after a prior send result (63 checks as of 2026-09-10).

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
localized command rejection, and the history/view/notification subscription union.

The fourth-phase suite has 41 regression groups and 87 desktop checks, followed by a completion
marker. `DesktopNotificationsSmoke.cs` supplies a recording notification sink: it tests encrypted
live Tell/keyword delivery, foreground suppression, DND, expiry, deduplication, event read state,
old-role navigation, intentional versus abnormal disconnects and notification settings localization.
It does not register or display system notifications. Duty callbacks also have a concurrent bounded
queue test, including logout and late previous-login deliveries. See [the notification report](../docs/PHASE4_NOTIFICATIONS_2026-09-11.md)
for the system notification and real-game validation boundaries.

Rebuild without the test targets before running the regular desktop app:

```powershell
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild
```

## Game-card source checks

The regression runner includes item kind handling, HQ deltas joined by parameter ID,
and structured metadata wire compatibility (43 groups total). To check the real card WebViews,
build the desktop with `-p:CustomAfterMicrosoftCommonTargets=<absolute path to tests/DesktopCardsSmoke.targets>`
and run the resulting executable. The eight checks use fixtures and do not connect to the game
or load/save user configuration. Results and a rendered card image are written beside the executable.
Always rebuild the desktop with `-t:Rebuild` without the test target afterward to restore the normal entry point.
See [data-source findings](../docs/GAME_CARD_SOURCES_2026-09-12.md) for source choices and remaining live checks.

## Remote screenshot checks

The regression runner has 52 groups, including screenshot wire compatibility, malformed metadata,
bounded JPEG assembly, duplicate/conflicting chunks, size limits, one capture across clients,
owner/login cancellation and backpressure. The screenshot WinUI suite has 31 checks using
an encrypted loopback TCP fixture and a real Windows-encoded JPEG. It does not read/save user
configuration or connect to the game. It covers native preview, zoom, exact saved bytes,
language changes, old plugins, cancellation, role/login changes, EOF and application shutdown.

```powershell
$screenshotTargets = Join-Path (Get-Location) 'tests/DesktopScreenshotsSmoke.targets'
$screenshotOutput = Join-Path (Get-Location) 'artifacts/phase6/smoke/'
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug "-p:CustomAfterMicrosoftCommonTargets=$screenshotTargets" "-p:OutDir=$screenshotOutput"
& (Join-Path $screenshotOutput 'XIVChat Desktop.exe')
```

Results, English/Chinese rendered windows and the saved fixture appear beside that executable.
Build with `-t:Rebuild` without test targets afterward to restore the regular entry point.
See [phase-six validation and live boundaries](../docs/PHASE6_SCREENSHOTS_2026-09-12.md).

## Card text polish

Card text polish (2026-09-13): the combined `DesktopCardsSmoke.targets` workflow now emits
61 passing checks including protocol fixture validation. Added cases cover delayed Chinese text,
marked original-language fallbacks, item/name cache refresh, localized copying/favorites, and DOM
updates retaining reading position and expanded provenance. See [the polish report](../docs/CARD_TEXT_POLISH_2026-09-13.md).

## Workspace, layouts and history export

The regression runner now has 56 groups, including validated layout storage, view identity
isolation, streaming TXT/Unicode RTF export, full query scopes and cancellation. Favorite-source
exports retain the original connection/owner and export only the linked records.

The workspace WinUI suite has 44 checks. It uses an encrypted loopback server and a unique
fixture database beside the executable; it does not load/save user configuration or contact
the game. Checks cover independent drafts and send-failure recovery across layout recreation,
owner/login/channel guards, history anchors, shared read state, notification routing, hidden
main-window lifetime, startup recovery, presets, named layouts, display bounds, and transactionally
written exports. It also checks that closing the last chat window stops the connection and closes
auxiliary windows.

```powershell
$workspaceTargets = Join-Path (Get-Location) 'tests/DesktopWorkspaceSmoke.targets'
$workspaceOutput = Join-Path (Get-Location) 'artifacts/phase7/smoke/'
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug "-p:CustomAfterMicrosoftCommonTargets=$workspaceTargets" "-p:OutDir=$workspaceOutput"
& (Join-Path $workspaceOutput 'XIVChat Desktop.exe')
```

Results are in `workspace-smoke-results.txt`; PNGs and exported fixture files are beside it.
Rebuild with `-t:Rebuild` without test targets before launching the normal client.
See [phase-seven results and remaining live checks](../docs/PHASE7_WORKSPACE_2026-09-12.md).

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
debug symbols exist. It also invokes the real identity and player-data readers over 300 simulated
logout frames with a cleared world RowRef, including when loading flags remain true.
It does not load the plugin into the game or read/write game configuration.

```powershell
dotnet build XIVChatPlugin/XIVChatPlugin.csproj -c Debug
```

With Dalamud and the game running, connect the desktop, change the plugin port, and reconnect.
Verify that new chat, outgoing messages, login/logout, and territory changes still update.
Repeat the port change and unload/reload the plugin; verify the old port is immediately reusable
and each message is delivered once. This integration check requires a live game session.
