# Changelog / 更新说明

## Desktop 1.4.0 — 2026-09-18 (local candidate, not published)

### English

- Added contact nicknames, separate from long notes and scoped by connection, current character and recipient. Friend/conversation search accepts nicknames and real names.
- Added a persistent streamer-mode switch, independent self/other scopes, custom self name and stable pseudonyms. Protected identities hide worlds and avatars across main/popout windows, history, favorites, events and notifications.
- Known complete names are masked across formatted chat segments, copied chat and TXT/RTF exports without changing original messages, item/map links or tell routing. Policy changes clear previous app notifications, dismiss identity editors and cancel exports; cancelled file writes preserve existing content.
- Added schema v6 contact display storage with migration backup and transactional rollback. Restore both the old database and configuration when rolling back to an earlier client.
- Rewrote Chinese getting-started copy and documented privacy limits: screenshots, unknown free text and input fields are not automatically redacted; original history remains local.
- Compatible with plugin 1.7.15; no protocol or plugin changes.
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
- 继续兼容插件 1.7.15，没有修改插件或通信协议。本版本仅为本地候选包，尚未在线发布。
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
