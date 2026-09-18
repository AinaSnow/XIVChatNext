# Desktop 1.4.0 live validation — 2026-09-18

Tested the local Windows x64 distribution against a running game through the user's existing relay connection. No chat messages were sent to other players.

## Results

- Backed up configuration and data while the client was stopped. The baseline contained 837 messages and seven conversations on schema v5.
- The normal production entry point started successfully. Migration produced a v6 backup, and SQLite integrity returned `ok`.
- Compared every baseline message's payload, note and bookmark against the upgraded database: all 837 were unchanged.
- Confirmed new live game messages and current character/location information arrived through the existing connection. History loaded current records.
- Temporarily changed an existing nickname. Main-window heading, message labels, fixed-recipient label and the already-open popout updated; the database persisted the nickname. Restored the exact prior nickname and verified no temporary test nickname remained.
- Friend refresh returned `Busy` while the game was in a duty. The client retained and clearly marked its old snapshot. Fresh friend presence was not verified in this session.

## Issue found and corrected

Japanese game text places particles directly after Latin character names, such as `Alice Snowは…` and `Alice Snowの攻撃`. Treating all adjacent Unicode letters as part of a name caused streamer-mode projection to miss these names.

The correction recognizes the transition between a Latin name and CJK prose. Longer Latin names, accented/combining suffixes and longer CJK names still retain partial-match protection. Seven new checks cover Japanese/Chinese prose, rendered/copied chunks and exports.

Read-only projection audit of 2,205 actual stored messages found 796 bodies containing their owner's name. After the correction, none retained that name in projected display text or export lines; no serialized source message was modified. The output retained 248 item/map link chunks. Only aggregate audit results are recorded here.

## Verification and boundaries

- 62 core/storage regression checks passed; streaming export covered 1,201 rows.
- Corrected self-contained Release package built successfully. The 25-check synthetic WinUI suite also passed using its runtime in a separate extracted copy with an isolated fixture entry point.
- The read-only live-data projection audit does not replace manually toggling streamer mode in the running game-connected UI. That interactive check awaits the user's switch; the session's Windows automation rules prohibit changing app privacy settings.
- Live outgoing chat, fresh friend presence, real OS notification delivery and installation on a clean Windows system were not verified here.
- The running original candidate remains separate from the corrected package at `artifacts/desktop-1.4.0-live-fix/`. Restart from the corrected package to apply the fix. No online release or push was performed.
