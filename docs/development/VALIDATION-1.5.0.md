# Desktop 1.5.0 · Plugin 1.7.17 — 2026-10-13

Approved for release by the user on 2026-10-13 after live testing of the preview packages built from `feat/phase1-hardening`. The release packages were rebuilt from the release commit with only version numbers and documentation changed, then re-verified below. The relay is unchanged.

- .NET 10: desktop, shared libraries, relay protocol/transport and console suites retargeted from .NET 9. Plugin, desktop and relay builds succeed; the Windows CI workflow passed on `main`.
- Plugin: 13 network-gate checks with a fake clock (per-address handshake limit, one pending trust prompt, three prompts per minute, recovery after the window, pruning). The user reported that live testing of the preview plugin passed; the suggested checklist covered the LAN setting after upgrade and same-PC connections.
- Desktop: 147 core/storage, 26 update and 94 Windows UI checks passed. New UI coverage verifies protected key storage, round trip, plain-text migration including the backup, reset on an unreadable key, shortcut validation, close-to-tray, tray restore, shortcut activation, a conflicting registration, removal and the Chinese Window settings layout (`tray-settings-zh.png`).
- The user reported that live testing of the preview client passed; the suggested checklist covered key migration, the tray menu and the shortcut while the game is focused. Automated checks send window messages directly and do not reproduce real mouse input or full-screen focus changes.
- Packaged versions verified: `XIVChat Desktop.dll` 1.5.0 and plugin manifest 1.7.17. Checksums are in `SHA256SUMS.txt` next to the packages.

Rolling back the desktop client to 1.4.x after the key migration requires restoring the previous configuration file. Existing user configuration and history were not modified by the fixture runs.
