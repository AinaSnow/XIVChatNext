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

## Chinese combat privacy follow-up — 2026-09-19

The user's screenshot showed Chinese character IDs and other combat actors remaining visible with streamer mode enabled. Two gaps were reproduced: the complete-name boundary treated adjacent Chinese prose as part of the name, and sender-less combat events did not identify nearby actors who had never chatted. The core test failed before the correction on `星野光发动了攻击，对白露造成了59点伤害。`.

Generated game text now allows known Chinese names directly adjoining Chinese prose. A bounded parser recognizes Chinese combat actor slots for casts/actions, effects and expiration; unregistered actors use the neutral alias when hiding others, without adding speculative identities to the contact directory. Known actors honor self/other settings and existing aliases. Ordinary chat retains the previous conservative matching rules. Because old battle text cannot reliably distinguish an unknown player from a monster, unidentified monster actors in those slots may also be anonymized. Unrecognized text formats remain outside this fallback.

History content now calls the same message-aware projection used by chat chunks and export instead of the generic free-text projection. Raw message bytes, ability text and link metadata are retained. No plugin/protocol or schema changes were made.

- All 100 core/storage checks passed, including the user's action/buff/expiration formats, Chinese names across styled chunks, original message preservation and ordinary-chat safeguards.
- All 58 WinUI checks passed using the new ZIP's runtime/resources in an isolated extracted copy. Verified Battle rendering, Chinese history, on/off refresh while history is visible, TXT export and unchanged stored Chinese payloads. Inspected `privacy-cn-battle.png` and `privacy-cn-history.png`.
- These tests used synthetic Chinese identities and examples from the supplied screenshot. They do not claim live verification on the user's CN game session. No player chat was sent or online release performed.

Newest local package: `artifacts/desktop-1.4.0-cn-privacy-fix/XIVChatNext-Desktop-v1.4.0-win-x64.zip`. SHA-256: `f84c5959ac200dcf238a4471eea6f67fe77f1a3936cb5b7873a44921f679af57`.

## Cross-world names, linked emotes and symbol picker — 2026-09-19

The live CN screenshot and window inspection exposed a missing case: a cross-world icon separates actor and world into different chunks. Plain-text history omitted that icon while chat matching treated it as a barrier. Added an icon-aware actor span and removed the world marker together with hidden names, preserving unrelated ability icons. Linked player payloads now provide exact target boundaries for standard/custom emotes and other linked body text. World IDs resolve through the existing desktop world table; snapshots retain the same resolver. Custom emote sender prefixes are explicitly masked without relying on a whitespace boundary.

- 147 core/storage checks passed, including real icon payload encoding, split rendering, self-only/others-only policies, retained ability icons, linked emote sender/target/worlds, unchanged raw payloads and export consistency.
- 68 WinUI checks passed in a separate fixture. Verified Battle, history, emotes, exports and original stored data. Symbol-picker checks cover selection replacement, caret restoration, length limits, draft persistence and independent popout editing without sending.
- A read-only audit of the three actual CN emote records confirmed masked sender/target/worlds with action wording preserved. No original records were edited. The initial audit's sandbox database access failure and the intentionally failing core regression caused two Windows error dialogs; both console programs now catch exceptions and return exit code 1 normally.
- The symbol picker follows the public Lodestone E000–E11F range, filtering to the bundled font's 165 assigned glyphs plus two brackets. It includes E037/E038. Main/popout composers use the game font and preserve normal insertion/send behavior. Inspected the rendered picker and Chinese emote screenshots. Actual in-game sending of these symbols remains for the user to verify.
- Restored the normal Debug entry point; the self-contained production package contains no fixture entry point. No plugin/protocol/schema changes, online release or push.

Newest package: `artifacts/desktop-1.4.0-symbols-emote-fix/XIVChatNext-Desktop-v1.4.0-win-x64.zip`. SHA-256: `e412b22ca2881e8631a8a78f886fec0e32c90b9012c9afc42cfda3b4813bb03b`.

## Release confirmation — 2026-09-19

After loading the extracted plugin 1.7.16 development package, the user confirmed live retesting was satisfactory and requested publication. Release staging preserves the exact tested desktop package above and plugin package `artifacts/plugin-1.7.16-cn-chat-fix/latest.zip` (SHA-256 `b007899a08d7e8d9c28bab50f017963f958e881626c31149865c4d06d0433bbe`), without rebuilding. The plugin's seven CN data checks passed; see the plugin regression instructions in [tests/README.md](../../tests/README.md). Earlier entries describe the state at their respective test times; this user confirmation supersedes their pending live-retest status without claiming clean-machine or extended weak-network testing.
