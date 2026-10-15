# Desktop 1.6.0 · Plugin 1.8.0 — 2026-10-15

Approved for release by the user on 2026-10-15 after live testing of the preview packages (encryption v2, exports, translation and AI, and the installer). The release packages were rebuilt from the release commit with only version numbers and documentation changed, then re-verified below. The relay is unchanged.

- Protocol: 13 checks, covering the v2 key exchange, confirmation tags, per-direction counters, replay and downgrade rejection and v1 fallback.
- Plugin: 22 network checks and 8 chat-conversion checks against CN Dalamud 15.0.3.6 and the installed CN game data.
- Desktop: 157 core/storage, 35 update, 30 AI and 119 Windows UI checks passed. AI and translation checks use local fake services; no request reached a real provider.
- Installer: the user installed a 1.4.9 test build with `XIVChatNext-win-Setup.exe` and updated it in place to 1.5.0 from a local feed (`XIVCHAT_UPDATE_FEED`), confirming shortcuts, **Download and install**, shutdown and restart. Updating from GitHub Releases is first exercised by this release.
- Package layout: the .NET runtime is in `runtime\`, and the portable folder has 129 files beside the executable. The host trace of a test build confirmed that `hostfxr` and `coreclr` load from `runtime\`.
- Packaged versions verified: `XIVChat Desktop.dll` 1.6.0, plugin manifest 1.8.0 and update feed `releases.win.json` 1.6.0. Checksums are in `SHA256SUMS.txt` next to the packages.

Clients before 1.6.0 cannot update themselves: users switch to the installer once, or keep using the portable ZIP. Existing user configuration and history were not modified by the fixture runs.
