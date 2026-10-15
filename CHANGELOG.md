# Changelog / 更新说明

## Unreleased

- Desktop: chat history can be exported as Markdown (.md) in addition to text and RTF. Each message keeps the existing `time [channel] sender: message` form; Markdown adds a title and a heading per day and escapes chat text so it displays literally.
- Exports name channels in the interface language ("Tell (Incoming)", "私聊（接收）") instead of internal identifiers. The plugin now attaches the linkshell or cross-world linkshell name to each linkshell message, read from the game, and exports show it as `[Cross-world Linkshell [1] · Shell name]`. The name is stored with the message, so later renames do not change history; streamer mode omits it.
- Desktop: the export window was only reachable from the menu behind the "XIVChat Next" title. It now also opens from an export button in each conversation or channel header and from **Settings → History**, and the menu title shows a drop-down arrow.
- 客户端：聊天记录导出新增 Markdown (.md)，文本和 RTF 保留。每条消息仍是 `时间 [频道] 发送者: 内容`；Markdown 另加标题和按日期分组的小标题，并对聊天内容转义，保证原样显示。
- 导出时频道按界面语言显示（“私聊（接收）”等），不再显示内部名称。插件会从游戏读取通讯贝和跨界通讯贝的名字并附在每条消息上，导出显示为 `[跨界通讯贝 [1] · 贝名]`。贝名随消息保存，之后改名不影响旧记录；主播模式下不显示。
- 客户端：导出窗口以前只能从 “XIVChat Next” 标题后面的菜单打开。现在会话或频道标题栏有导出按钮，“设置 → 历史”里也能打开；菜单标题加上了下拉箭头。
- Encryption v2 between the desktop client and the plugin (Desktop 1.6.0, Plugin 1.8.0). Each connection adds fresh X25519 keys, both ends prove ownership of their long-term keys, and packets use per-direction counters. Protocol 1 reused the same session keys on every connection and accepted replayed packets, so someone who recorded encrypted traffic could replay a client's commands into a new connection; v2 rejects that and adds forward secrecy. Fingerprints are unchanged. See docs/protocol/CHAT_V2.md.
- Compatibility: the client tries v2 first and falls back to v1 for older plugins with a warning. After a plugin has used v2, the client refuses v1 for it; the plugin likewise refuses v1 from a client key that has used v2. The plugin's **Allow clients using old encryption (v1)** setting (on by default) can turn v1 off entirely, and the Devices tab labels each device's encryption.
- 客户端与插件之间改用加密 v2（客户端 1.6.0、插件 1.8.0）。每次连接增加新的 X25519 临时密钥，双方证明持有自己的长期私钥，数据包使用分方向的计数器。v1 每次连接都使用相同的会话密钥，并且接受重放的数据包，录下加密流量的人可以把客户端的指令重放到新连接中；v2 会拒绝这种重放，并提供前向保密。设备指纹不变。详见 docs/protocol/CHAT_V2.md。
- 兼容：客户端先尝试 v2，遇到旧插件时回退到 v1 并提示更新。插件用过 v2 后，客户端会拒绝再用 v1 连接它；插件同样拒绝用过 v2 的客户端以 v1 连接。插件新增“允许旧版加密 (v1) 的客户端”（默认开启），可完全关闭 v1；“设备”页会标出每台设备使用的加密版本。
- Plugin: new **Allow clients to request game screenshots** setting in the Messages tab (on by default). Turning it off rejects new requests, stops a capture in progress and tells connected clients, which show that screenshots are turned off rather than asking for a plugin update.
- Desktop: the navigation rail shows **Chats** instead of a truncated "Conversations" in English; the full name remains in the tooltip and for screen readers.
- 插件：“消息”页新增“允许客户端请求游戏截图”（默认开启）。关闭后会拒绝新请求、停止正在进行的截图并通知已连接的客户端，客户端会提示截图已被关闭，而不是要求更新插件。
- 客户端：英文界面左侧导航不再把 “Conversations” 截断，改为显示 “Chats”，完整名称保留在提示和读屏信息中。

## Desktop 1.5.0 · Plugin 1.7.17 — 2026-10-13

- Desktop: optional notification-area icon. With **Keep running in the notification area** on, closing the main window hides it; click the icon to show it again or right-click to exit. The tooltip shows unread tells.
- Desktop: optional global shortcut (Ctrl+Alt+X by default, configurable) that shows the main window with the composer focused and hides it when it is already in front. Conflicts with other applications are reported in settings. Both features are off by default.
- Plugin: new configurations listen on this computer only (127.0.0.1). A new **Allow LAN connections** setting listens on all interfaces; configurations saved by earlier versions turn it on automatically so existing LAN setups keep working. Relay connections are unaffected.
- Plugin: each remote address may hold at most 4 unauthenticated connections, one pending trust prompt and 3 trust prompts per minute, so an untrusted device on the network cannot flood approval windows. Diagnostics show the current counts.
- Desktop: the device private key is now stored with Windows DPAPI for the current user instead of plain text. Existing configurations migrate on first start, and the plain-text backup is replaced. If the key cannot be read (for example, the configuration was copied from another Windows account), a new key is created, other settings are kept, and the plugin asks to trust the device again. Downgrading to 1.4.x after this migration requires restoring the old configuration.
- 插件：新配置默认只允许本机（127.0.0.1）直接连接；新增“允许局域网设备连接”开关。旧版本保存的配置会自动开启该开关，已有局域网连接不受影响；中继连接不受影响。
- 插件：每个来源地址最多 4 个未认证连接、同时 1 个待确认信任请求、每分钟最多 3 个信任请求，避免网络中未信任设备反复弹出确认窗口。诊断页显示当前计数。
- 客户端：可选托盘图标。开启“关闭主窗口时保留在通知区域”后，关闭主窗口只会隐藏；单击图标重新显示，右键可退出，悬停显示未读私聊数。
- 客户端：可选全局快捷键（默认 Ctrl+Alt+X，可自定义），显示主窗口并聚焦输入框，窗口已在最前时再按一次隐藏；与其他程序冲突时在设置页提示。两项功能默认关闭。
- 客户端：本设备私钥改用 Windows DPAPI（当前用户）加密保存，不再明文写入配置。首次启动自动迁移，并替换旧的明文备份。若无法读取（例如配置从其他 Windows 账户复制而来），会生成新密钥并保留其他设置，连接时需在插件中重新确认信任。迁移后若退回 1.4.x，需要恢复旧配置。
- Moved the desktop client, shared libraries, relay protocol/transport and regression suites from .NET 9 to .NET 10 LTS ahead of .NET 9 end of support on 2026-11-10. The plugin and relay server already targeted .NET 10. The packaged desktop ZIP remains self-contained, so users do not need to install a runtime.
- 客户端、共享库、中继协议/传输层和回归测试从 .NET 9 升级到 .NET 10 LTS（.NET 9 于 2026-11-10 停止支持）。插件和中继服务端原本已使用 .NET 10。客户端 ZIP 仍自带运行时，用户无需另外安装。

## Desktop 1.4.1 — 2026-09-19

- Added a background update check after startup and a bilingual Updates settings page with a manual check, current version, release notes and Windows download link. Startup checks can be disabled; failures do not interrupt chat. Available updates use a dismissible, nonmodal main-window banner.
- Compare numeric desktop ZIP versions from public GitHub releases, rather than plugin release tags. Ignore drafts, prereleases and plugin-only releases; requests time out, share one in-flight check and send no character or chat information. Downloads open in the browser and never replace a running client automatically.
- 新增启动后后台更新检查，以及中英文“更新”设置页，可手动检查、查看当前版本和更新说明、下载 Windows 客户端。可关闭启动检查，失败不影响聊天；发现新版时在主窗口显示可关闭的提示。
- 按客户端 ZIP 的版本比较，避免把插件发布标签误认为客户端版本；排除草稿、预发布和仅插件更新。检查不发送角色或聊天信息，点击下载后打开浏览器。

## Plugin 1.7.16 — 2026-09-19

- 修复国服物品链接整条聊天丢失：装备职业限制读取本服数据，不再要求英文 ClassJob 表。物品或地图附加资料失败时保留聊天文字，并缓存缺失结果，避免相同资料重复报错。
- 聊天格式化失败时回退到原始消息解析，仍转发并记录消息；无需更换 1.4.0 客户端，之前未被插件记录的消息无法自动追回。
- Fixed CN item-link messages being lost before forwarding when equipment restrictions requested an unavailable English sheet. Resolve schema job columns from stable row IDs against local sheets. Optional metadata failures degrade to missing card details; rendering failures fall back to original text without changing raw message bytes.
- Validated the reported CN item IDs 49874 and 50289 against installed CN data, equipment restrictions, the former English-sheet exception, negative caching and fault-injected chat fallback. The user confirmed live retesting passed.

## Desktop 1.4.0 — 2026-09-19

### English

- Added contact nicknames, separate from long notes and scoped by connection, current character and recipient. Friend/conversation search accepts nicknames and real names.
- Added a persistent streamer-mode switch, independent self/other scopes, custom self name and stable pseudonyms. Protected identities hide worlds and avatars across main/popout windows, history, favorites, events and notifications.
- Known complete names are masked across formatted chat segments, copied chat and TXT/RTF exports without changing original messages, item/map links or tell routing. Policy changes clear previous app notifications, dismiss identity editors and cancel exports; cancelled file writes preserve existing content.
- Added schema v6 contact display storage with migration backup and transactional rollback. Restore both the old database and configuration when rolling back to an earlier client.
- Rewrote Chinese getting-started copy and documented privacy limits: screenshots, unknown free text and input fields are not automatically redacted; original history remains local.
- Compatible with plugin 1.7.15; upgrade to plugin 1.7.16 for the CN item-link fix. No relay update is required.
- Added an explicit, resize-aware clip to the shared chat list to prevent disconnect messages painting over the return-to-latest button and composer in main/popout windows.
- Disabled staggered chat-row insertion animations, which temporarily left a newly opened popout blank during message bursts. Expanded the Windows suite to cover drafts, offline send protection, outbound packet guards and off-screen window recovery.
- Fixed Chinese character names adjacent to generated game prose. Chinese combat actor slots without sender identity now use a neutral alias when hiding others; live chat rendering, history previews and exports share that protection.
- Fixed cross-world actor names separated by icon chunks, and emote sender/target leaks. Embedded player links now identify targets precisely; their worlds and cross-world markers are hidden with their names.
- Added a game-symbol picker beside Send in main/popout composers. It inserts supported FFXIV Unicode glyphs at the cursor or replaces the selection, preserving drafts without sending.

Live follow-up: fixed masking of Latin character names adjoining Japanese particles or Chinese prose (for example `Alice Snowの攻撃`). Added seven regression checks, including export/rendering and partial-name safeguards. See [live validation](docs/development/VALIDATION-1.4.0.md).

Initial validation: 55 core/storage checks and 25 Windows UI checks, including bilingual controls, multiple windows, migration failure, streaming export and cancellation. The self-contained Release ZIP's runtime also passed the UI suite in a separate extracted copy with an isolated fixture entry point. The live follow-up increased core coverage to 62 checks; remaining live-test boundaries are listed in the validation report. Clean-machine installation is still unverified.

### 简体中文

- 新增好友备注名，与原有备注分开保存。不同连接和角色各自保存，好友和会话可以用备注名或真实姓名搜索。
- 主窗口可以一键开关主播模式，选择隐藏自己、其他玩家或两者都隐藏。自己可自定义显示名，其他玩家使用固定化名；服务器和头像也会隐藏，重启后保留设置。
- 主窗口、小窗、历史、收藏、事件、通知、聊天复制和 TXT/RTF 导出使用同一显示规则。正文只处理能确认的完整姓名，保留格式与物品、地图链接，不改变实际收件人或原始记录。
- 切换隐私设置会清理之前的应用通知、关闭身份编辑框并取消导出。取消导出不会覆盖原有文件。
- 数据库从 v5 升到 v6，升级前自动备份。回退旧客户端时需一起恢复旧数据库和配置。
- 中文介绍和安装说明改用直接易懂的说法。游戏截图、未知自由文本和输入框内容不会自动打码，已复制或保存到外部的内容无法撤回。
- 客户端继续兼容插件 1.7.15；国服物品链接修复需要同时更新插件到 1.7.16，无需更新中继。
- 为主窗口和聊天小窗共用的消息列表补充随尺寸更新的裁剪边界，防止断线消息穿过列表、叠到“回到最新消息”和输入框上。
- 取消聊天消息逐条入场动画，避免新开小窗在大量消息到达时暂时空白；补测草稿保存、离线发送保护、发送报文校验及屏外窗口恢复。
- 修复国服中文角色名紧接战斗正文时的主播模式遗漏。没有发送者身份的中文战斗角色位置在隐藏他人时使用通用匿名名称，实时列表、历史预览与导出使用相同规则。
- 修复跨服图标分隔姓名导致的遗漏，并补齐感情动作发起者和目标的保护；角色链接中的姓名、服务器与跨服标记一起隐藏。
- 主窗口和聊天小窗的发送按钮左侧新增游戏符号面板，支持在光标处插入或替换选中文字，保存到草稿，不会自动发送。

后续实机检查修复了日文助词、中文正文紧接拉丁角色名时的脱敏遗漏，例如 `Alice Snowの攻撃`。补充 7 项回归检查，覆盖显示、导出和姓名前缀保护，详见[实测记录](docs/development/VALIDATION-1.4.0.md)。

首轮通过 55 项显示与存储检查、25 项 Windows 窗口检查，覆盖中英文、多窗口、迁移失败、流式导出和取消。Release ZIP 解压到独立目录后，也通过隔离测试入口验证了随包运行库启动。后续实机检查后，核心检查增加到 62 项；联调范围和未验证项见实测记录，干净系统安装仍未测试。

## Desktop 1.3.7 — 2026-09-14

### English

- Fixed chat and main windows reopening outside the screen on a secondary monitor.
- Previously saved off-screen positions are moved back into the available display area automatically.
- Compatible with plugin 1.7.15; this update requires replacing only the desktop client.

### 简体中文

- 修复副屏上的聊天小窗和主窗口在恢复布局时跑到屏幕外的问题。
- 之前保存的屏幕外位置会自动收回可用显示区域。
- 兼容插件 1.7.15，本次只需更新桌面客户端。

## Plugin 1.7.15 · Desktop 1.3.6 — 2026-09-14

### English

- New chat workspace: conversations, fixed-target tells, unread counts, friends and optional avatars.
- Character-scoped local history, search, favorites, notes and export.
- Item/map cards, equipment comparisons and game screenshots with preview/save/copy.
- Chat popouts, saved layouts, event notifications and first-run guidance.
- Optional self-hosted relay with HTTPS, persistent pairing and English/Chinese web administration.
- New plugin configurations default to English; existing language choices are preserved.
- English-first bilingual website, README and maintained guides.
- Fixed portable startup, short-window navigation and nested plugin ZIPs.
- Runtime resources retain English, Japanese, German, Chinese and French; application UI remains English/Chinese.
- Local tests, process documentation and retired artwork moved outside the product tree.

Upgrade the client and plugin together. Back up configuration, history and relay data, and retain the previous package for rollback. Saved relay pairing normally survives restarts; legacy public-relay codes do not work with the new service.

Validation includes local regression, desktop startup and previous live game integration. Clean-machine installation and extended weak-network/game-restart scenarios have not all been independently verified.

## 简体中文

### 插件 1.7.15 · 客户端 1.3.6 — 2026-09-14

- 新工作台：会话、固定悄悄话、未读、好友和可选头像。
- 按角色隔离的历史、搜索、收藏、备注与导出。
- 物品/地图卡片、装备对比、游戏截图及预览/保存/复制。
- 聊天小窗、布局保存、事件通知和首次引导。
- 可选自建中继：HTTPS、持久配对和中英文网页后台。
- 插件新配置默认英文，已有选择保留。
- 介绍页、README 和维护中的说明提供中英文，英文优先。
- 修复独立分发启动、小窗口导航和插件 ZIP 嵌套。
- 运行库保留英、日、德、中、法，应用界面为中英文。
- 测试、过程文档和旧素材移至产品仓库外本地归档。

请配套升级两端，备份配置、历史和中继数据，并保留旧包回退。已保存配对通常跨重启保留，旧公共中继认证码不能用于新服务。

已完成本地回归、启动检查和此前游戏联调；干净机器、长期弱网与游戏重启场景尚未全部独立验收。
