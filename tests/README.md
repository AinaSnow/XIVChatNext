# Desktop regression checks

Run from the repository root with the SDKs described in [BUILD](../docs/development/BUILD.md).

```powershell
dotnet run --project tests/PrivacyRegression/PrivacyRegression.csproj -c Release
```

The console suite checks identity projection, nickname isolation, pseudonym persistence and collisions, original message/link preservation, SQLite v5-to-v6 migration and rollback, and streaming TXT/RTF export. It uses a unique temporary database and deletes only its own fixture.

The Windows UI suite exercises real WinUI windows with synthetic identities and fake notifications. It does not read your normal configuration or history, connect to a game or issue Windows notifications. It creates uniquely named fixture directories, `privacy-smoke-results.txt` and Chinese screenshots next to the test executable.

The suite also floods a disconnected channel with repeated error messages, reads older messages, resizes the main window and checks a popout with multiline history. Rendered pixel comparisons with the list visible/hidden assert that no chat pixels appear outside its viewport, including over the return-to-latest button and composer. It checks that returning to the newest message still works and saves `chat-disconnected-*-zh.png` screenshots. These synthetic checks do not reproduce every live compositor timing issue.

The popout receives 80 multiline messages after opening, covering the former insertion-animation blank interval. Additional checks exercise Chinese/emoji drafts, disabled sending, view switching, layout database persistence, reopening popouts, failed-send draft preservation and recovery of off-screen bounds. A connection object with no opened transport checks actual encoded outbound packets and rejects stale owners/logins, oversized UTF-8 payloads, empty text and unavailable/cancelled connections. No test packet is sent over a network.

Chinese combat coverage includes names adjacent to generated prose, unknown actors without sender metadata, cross-world icon chunks, action/buff/expiration samples, linked emote targets, preservation of ordinary chat and longer actor names, original bytes and export consistency. The real WinUI suite checks Battle/emote rendering, toggling privacy over already-visible history, switching owners and exporting the same Chinese records. It also verifies symbol selection, cursor/selection replacement, length limits and independent main/popout draft persistence without sending. It saves `privacy-cn-battle.png`, `privacy-cn-history.png`, `privacy-cn-emote.png` and `game-symbol-picker.png`. The Windows suite currently reports 80 checks; the core/storage suite reports 147. Console assertion failures exit with code 1 instead of causing a Windows crash dialog.

```powershell
$privacyTargets = Join-Path (Get-Location) 'tests/DesktopPrivacySmoke.targets'
dotnet publish 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false "-p:CustomAfterMicrosoftCommonTargets=$privacyTargets" -o artifacts/desktop-ui-fixture
$privacyExe = Join-Path (Get-Location) 'artifacts/desktop-ui-fixture/XIVChat Desktop.exe'
Start-Process -FilePath $privacyExe -WindowStyle Hidden -Wait
Get-Content (Join-Path (Split-Path $privacyExe) 'privacy-smoke-results.txt')
```

Look for `All desktop privacy checks completed.`; any `FAIL` line is a failure. This suite temporarily replaces the Debug entry point. Restore a normal Debug build with `dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild -p:PublishSingleFile=false` before using that output interactively. Normal Release packaging does not include the test entry point.

这些检查使用虚构角色和独立数据库，不读取日常配置、不连接游戏。控制台检查覆盖存储和显示规则，Windows 检查覆盖主窗口、小窗、通知、导出及中英文设置。它们不能替代实际游戏联调或干净系统上的安装测试。

## Desktop update checks

`dotnet run --project tests/UpdateRegression -c Release` exercises the production release parser and update state against fake HTTP responses. It covers combined plugin/desktop releases, numeric version ordering, unsupported assets, URL validation, prereleases, pagination, failures/retries, shared requests and startup preferences. It does not access GitHub or the game. Add `-- --live` to perform a read-only check against the public GitHub release list.

The opt-in Windows suite also tests update progress, the nonmodal banner, release notes, multiple settings windows, immediate preference persistence and English/Chinese refresh. All update responses in that suite are synthetic; no ZIP is downloaded or installed. Captures include `updates-settings-zh.png` and `updates-banner-zh.png`.

## CN plugin item-link regression

`PluginChatRegression` reads an installed CN `game/sqpack` directory without starting Dalamud, connecting to the game or sending chat. It checks the two reported items, equipment restrictions without an English sheet, metadata failure caching and chat fallback preserving text/raw bytes. Override `DalamudLibPath` for a different local CN runtime version; pass the same directory at runtime for dependency resolution.

```powershell
dotnet run --project tests/PluginChatRegression -c Release -- '<game>/sqpack' '<CN Dalamud library directory>'
```

The 2026-09-19 check passed all seven checks against CN Dalamud 15.0.3.5 and the installed CN data. The unavailable English ClassJob sheet reproduced the same `UnsupportedLanguageException` found in the live plugin log at 15:17:24 and 15:17:55. Original dropped messages never reached desktop storage and cannot be recovered from its history.
