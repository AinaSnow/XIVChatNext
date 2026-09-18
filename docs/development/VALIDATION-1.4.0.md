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

## Disconnect-message clipping follow-up

The user supplied a screenshot showing repeated disconnect errors painting below the message list, over the return-to-latest button and composer. The shared `ChatMessageList` now clips its ListView to its arranged size and updates the clip when the window, composer or latest-button row changes size. Main and popout windows use the same control.

- Added eight WinUI checks (33 total). They cover repeated 409/closed-channel error text with a disabled composer, history reading while more errors arrive, resizing, return-to-latest and multiline offline history in a popout. Pixel comparisons require visible content inside the list and zero changed pixels outside its bounds.
- All 33 checks passed against the self-contained ZIP's resources/runtime in a separate extracted copy with the isolated test entry point. Inspected Chinese main/popout screenshots; no message text overlaps the composer. The distributed ZIP retains the production entry point.
- The exact live overflow was not reproduced reliably in the synthetic baseline; this change explicitly enforces the missing outer drawing boundary. The user's running client has not been replaced, so confirmation of the original live case requires restarting with the new package.
- A separate synthetic case inserting 80 messages during popout initialization produced a blank latest view even with the clip removed. The follow-up below identified the temporary blank interval as staggered insertion animation and restored this case to the passing suite.

Latest local package: `artifacts/desktop-1.4.0-chat-clip-fix/XIVChatNext-Desktop-v1.4.0-win-x64.zip` (includes the earlier privacy fix). SHA-256: `7eca8cc9fd11ba98bb07fd28398445be5682fda72f44ae42beb8dc220265e002`. No game connection, outgoing player chat, online release or push was performed for this regression run.

## Full self-test follow-up — 2026-09-18

The game and client were not running when this pass started. Reproduced the blank popout with 80 messages arriving after the window opened. Text and layout existed, and waiting another 1.5 seconds restored the text without changing its layout: the cause was the staggered row insertion animation, not missing history. Disabled item-container transitions in the shared chat list. The original burst case now passes the render check after the same 300 ms settling interval as the other layout checks.

- 49 Windows checks passed using the new self-contained ZIP's resources/runtime with an isolated fixture entry point in a separate extracted copy. Added coverage for Chinese/emoji/multiline drafts, offline main/popout/tell send rejection, view changes, layout database round trips, reopening popouts, preservation of newer drafts after failed sends and off-screen bounds correction.
- Inspected actual queued channel/tell packets without opening any transport: real recipient identities and multiline UTF-8 content were preserved with streamer mode enabled; packets carried owner/login/channel guards. Stale identities, oversized UTF-8 content, whitespace and unavailable/cancelled connections were rejected.
- Re-ran all 62 core/storage checks, including migration, original-record preservation and streaming export of 1,201 records.
- Launched the normal production executable with the user's existing local configuration. It restored the previously open popout, nickname and local history. Returned to the main window and attempted the existing relay connection. The offline plugin returned HTTP 409; the client returned to its disconnected state and displayed the error within the chat area without overlapping the composer. Left the new production client open. No player chat was sent, and no new crash log was generated.
- The distributed assembly contains no fixture entry point; restored the normal Debug build after testing. No online release or push.

Remaining live boundaries: successful reconnection after the game/plugin comes online, actual outgoing/incoming game chat on this build, fresh friend presence, real Windows notification delivery, live streamer-toggle behavior and clean-machine/mixed-DPI installation checks. The automated privacy checks use synthetic identities; they do not constitute a manual privacy-toggle test on the live account.

Newest local package (supersedes the packages above): `artifacts/desktop-1.4.0-selftest-fix/XIVChatNext-Desktop-v1.4.0-win-x64.zip`. SHA-256: `e23cae695427351c9301032a982bf003dbf847e6775c1c5638810ee549add34c`.
