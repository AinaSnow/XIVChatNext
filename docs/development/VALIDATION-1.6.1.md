# Desktop 1.6.1 — 2026-10-15

Approved for release by the user on 2026-10-15 after testing the preview package built from `feat/summary-only`. Desktop only; the plugin stays at 1.8.0 and the relay is unchanged. The release package was rebuilt from the release commit with only version numbers and documentation changed.

- Desktop: 157 core/storage, 35 update, 30 AI and 124 Windows UI checks passed. The five new UI checks cover the summary button following the AI setting, the default one-day range, hidden export options, and a summary shown in the window without writing a file. AI checks use a local fake service.
- Packaged versions verified: `XIVChat Desktop.dll` 1.6.1 and update feed `releases.win.json` 1.6.1. Checksums are in `SHA256SUMS.txt` next to the packages.
- This is the first update that 1.6.0 installations receive through **Settings → Updates** from GitHub Releases.
