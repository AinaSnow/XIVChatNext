# Active friend presence

The desktop queries the selected friend's status when opening a conversation. While
the window is active, a two-second timer checks the current conversation and visible
friend rows. Offscreen rows are not queried. Each friend has a thirty-second retry
interval; results older than one minute display an expired label. Failed requests
display unknown with a timeout/unavailable reason. Querying never sends a chat message.

## Protocol and lifecycle

`ServerCapabilities.FriendPresence` (appended key 8) enables new client operation 11
and server operation 14. Every query and response carries request ID, owner key,
login epoch and friend CID. Legacy connections do not send these operations.

Presence is an ephemeral overlay, independent of complete friend-list snapshots.
It is cleared on disconnection, source changes and login changes, and never restored
as fresh status from SQLite. The desktop accepts only the current matching request.
Successful responses carry their original UTC check time; cache hits do not reset it.

The plugin has a bounded 256-request input queue, at most one pending query per
connection, and one native request at a time. Same-CID requests coalesce. Native
starts are spaced by at least five seconds, the shared cache lasts thirty seconds,
and a native request times out after ten seconds (desktop: fifteen seconds).

## Native boundary

Verified against the local international game build `2026.09.01.0000.0000` on
2026-09-10, using the installed FFXIVClientStructs bindings. Native addresses from
static analysis are not embedded in production code.

* Requests use typed `AgentFriendlist.RequestFriendInfo(CID)` on Dalamud's framework
  thread. The CID must exist in the current typed `InfoProxyFriendList.CharDataSpan`,
  with a valid bounded buffer and matching logged-in owner.
* InfoModule dispatches individual status responses to info-proxy virtual slot 9
  with `(proxy, response, CID)`. The friend-list implementation applies the result
  through its common-list base. Slot 8 follows and is a no-op for this proxy.
* The reader hooks slot 9, calls the original first, then copies only the matching
  response's CID at offset `0x08` and result code at `0x14`. A verified long function
  signature is scanned through Dalamud's original module copy and must resolve to
  that virtual slot before hooking. This tolerates live hook jumps after reload;
  a changed signature or mismatched slot fails closed as unavailable.
* Result 0 enables the native Online flag; result 1 represents offline; result 2
  does not establish a resolvable online state and is exposed as unknown. Result 3
  skips the native update and must not freshen any prior status. Unknown codes
  likewise return unavailable.
* The native response's first qword is an additional status mask, written into the
  entry's undocumented `0x10` field. It is not interchangeable with the typed
  entry's `State` at `0x08`. Neither the cached State nor friendship timestamps are
  used as proof that an individual status query has completed.

Cancellation disarms the managed request and quarantines its native in-flight CID
until the callback drains. This prevents a late pre-timeout/pre-login reply from
being mistaken for a new one, while allowing other friends to continue. If that
callback never arrives, that CID reports busy until the proxy changes or the plugin
reloads. Quarantine is bounded at 200 CIDs. Revalidate this undocumented callback
ABI after game updates before relaxing the guard.

## Validation

Regression tests cover wire round trips, capability gating, ownership/CID/request
matching, invalid response enums/times, freshness boundaries, timeout rejection,
reconnect clearing, shared cache timestamps, coalescing, canceled peers and queue
bounds. The desktop smoke suite additionally exercises encrypted presence requests,
visible friend status and role changes through its simulated server.

On 2026-09-10, 36 regression groups, 69 WinUI checks, localization audit (350 keys
per language), normal desktop build, plugin build and plugin-entry checks passed.
Live testing at 21:42–21:43 China time returned Online for El Cid@Asura (result 0)
and Offline for several same-world/cross-world friends (result 1). Matched native
responses arrived about 0.2–0.6 seconds after each request; five-second scheduling
and a later repeat of the same online friend were verified. The conversation
header displayed the same checked-at time as the friend row.

The initial two-second schedule produced a missing response, so the final interval
is five seconds. Timeout was never interpreted as offline. The lost-reply case and
plugin reload motivated per-CID quarantine and original-module signature scanning.
No chat messages were sent in this presence test. Actual online-to-offline transitions
and a second real owner character were not exercised; timeout/epoch cases are covered
by regression tests. Cross-DC unresolved replies remain conservatively unknown.
