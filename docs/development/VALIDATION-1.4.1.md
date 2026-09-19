# Desktop 1.4.1 update checks — 2026-09-19

Local preview; not published as a GitHub release. Plugin 1.7.16 and the relay are unchanged.

- 26 update checks passed using the production release client and state manager with fake HTTP. Coverage includes numeric desktop asset versions inside plugin-tagged releases, plugin-only releases, drafts/prereleases, matching repository URLs, pagination, empty/invalid responses, rate limiting, cancellation/failure, retries, one shared request and enabled/disabled startup behavior.
- A read-only check against the public GitHub API selected desktop **1.4.0** from release **1.7.16**, with the expected Windows ZIP URL. No download, install, credentials, character data or chat content was sent by the checker.
- 80 Windows fixture checks passed, including the prior privacy, clipping, symbols, draft and history checks. New coverage verifies the current version, check progress, shared settings state, persisted opt-out, default migration for older configurations, notes/download controls, the nonmodal banner, dismissal, language changes and unsubscribing closed settings windows.
- Inspected Chinese and English settings captures and the Chinese banner. Fixed a transparent content background and changed settings tabs to size to their labels so the Updates tab remains visible. Long notes scroll inside the settings window.
- The first attempt to combine a framework-dependent Debug fixture assembly with a self-contained runtime exited before tests started. Publishing the complete fixture with matching self-contained settings resolved startup; the passing run used `artifacts/update-ui-final`. Updated the test instructions accordingly.
- Normal Release packaging succeeds. The distributed client uses its production entry point; fixture code is only included by the explicit test target. Restore the normal Debug output after fixture publishing.

The UI uses a simulated 1.4.2 release to exercise the newer-version path. That version is not a real published update. Download buttons are checked as UI controls and their URLs are validated by the core tests; browser download/install and a future real update are not exercised automatically. Existing user configuration and history were not modified by the fixture.

Final package: `artifacts/desktop-1.4.1-update-check-final/XIVChatNext-Desktop-v1.4.1-win-x64.zip`. See its adjacent `SHA256SUMS.txt` for the package digest.
