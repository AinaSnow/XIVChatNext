# Client guide / 客户端指南

## English

### Install

Download the Windows x64 ZIP from [Releases](https://github.com/AinaSnow/XIVChatNext/releases/latest), extract it into its own directory and run `XIVChat Desktop.exe`. Keep all DLL/resource folders. .NET and Windows App SDK are included; web cards also require Microsoft Edge WebView2 Runtime.

Add `https://raw.githubusercontent.com/AinaSnow/FFXIV-Dalamud-Plugins/main/pluginmaster.json` to Dalamud's custom plugin repositories. Install **XIVChatNext Server**, log in, and use matching client/plugin versions from the same release.

### Connect

The first-run guide sets language, connection and notifications; reopen it from Settings. The desktop supports English, Simplified Chinese and System. New plugin configurations default to English and have an independent language setting.

Use **127.0.0.1:14777** on the same PC, unless the port was changed. On a reachable network, use the game PC's address. Ports must match. Compare fingerprints and confirm trust on both ends.

Without direct access, choose **Self-hosted relay**, paste a fresh invitation from your relay administrator, independently compare the endpoint fingerprint with the plugin and save. Complete the chat-device trust confirmation too. Saved pairing survives normal restarts; re-pair after loss, revocation or identity changes. See [Relay](SELF_HOSTED_RELAY.md).

Click the Logo to reconnect to the most recently successful target. Interrupted successful relay sessions retry; explicit Disconnect cancels retries. Reopening the app does not send chat or log in to the game.

### Everyday use

| Area | Action |
| --- | --- |
| Conversations | Open tells, pin contacts and check unread counts. Confirm the fixed recipient above the composer. |
| Channels | Select the sending channel and customize views/filters. |
| Friends | Open a conversation from synced friends. Unknown/expired presence is not proof of being offline. |
| History | Choose a character/source, search and read saved messages offline. |
| Favorites | Save messages/cards and add notes. |
| Events | Review events and configure notifications in Settings. |

Open conversations as popouts and save/restore layouts. They share conversation state and preserve disconnected drafts. Reading another character's history does not switch the live sender.

Select item/map links to inspect cards; comparisons depend on available data/equipment. Allow screenshots in the plugin, request one from the client and preview/save/copy the result.

### Contact nicknames and streamer mode (Desktop 1.4.0)

Right-click a friend or conversation and choose **Contact nickname**, or use the nickname button in a conversation. Names must fit on one line, up to 64 characters. Clear the field to restore the character name. Nicknames belong to the connection and current character; they also work for friends without saved messages. They do not replace long notes or change the tell recipient. Friend/conversation search accepts both nicknames and real names. Ordinary history search, copying and exports retain original identities when streamer mode is off.

Toggle **Streamer mode** at the top of the main window. In Settings, choose whether to hide yourself, other players, or both, and optionally set your own display name. Both scopes default on. The app remembers the setting before reopening its windows. Protected players use stable pseudonyms, with their server and avatar hidden; pseudonyms take precedence over nicknames. Open popouts update with the main window.

The same display rules apply to known complete names in chat text, previews, history, favorites, events, notifications, selected/copied text and TXT/RTF exports. Ambiguous names use a generic anonymous label. Switching the policy clears this app's previous notifications, clears export previews and cancels running exports. Already copied text, saved files and notifications outside the app's control cannot be recalled.

**This is not automatic image or free-text redaction.** Game screenshots remain unchanged. Unknown nicknames, unrecognized names, images, search fields and text you type may still identify someone. Turn streamer mode off to edit stored notes, nicknames or avatar mappings; an open identity editor closes when it is enabled. The database keeps original messages. Check the actual screen or export before sharing.

### Update checks (Desktop 1.4.1)

The client checks public GitHub releases shortly after startup. If a newer Windows client is available, a dismissible banner opens **Settings → Updates**, where you can read the release notes and open the download. This page also shows the current version and a **Check for updates** button. Turn off **Check for updates at startup** to use manual checks only; the preference saves immediately. Failed checks do not interrupt chat.

Only stable Windows desktop packages are compared, independently of plugin version tags. Checking sends no chat or character information. Downloads open in your browser; extract into a new folder and close the current client before starting the new version. The client does not replace itself automatically.

### Data and upgrades

History, favorites, notes and layouts stay local; the relay is not cloud history sync. Configuration: `%APPDATA%\XIVChat for Windows`. Data: `%LOCALAPPDATA%\XIVChatDesktop`. Do not publicly share these files. Protected relay credentials belong to the current Windows user.

Close the client normally and back up configuration/data before upgrading. Extract the new package into a separate directory; never mix runtime DLLs. Update the plugin in Dalamud and retain the old client package for rollback.

Desktop 1.4.0 remains compatible with plugin 1.7.15 and upgrades the history database from schema v5 to v6. Migration makes a `*.before-v6-*.bak` backup next to the database. To roll back, close the client and restore the previous configuration and database together with the previous executable; do not open the v6 database with an older client. Keep a copy of the current data first, since restoring a backup omits messages saved after that backup.

Live sending, friends and screenshots require the game, logged-in character and plugin. This app cannot keep a character logged in or send after the game closes. Game/Dalamud updates may require a plugin update.

## 简体中文

### 安装

从 [Releases](https://github.com/AinaSnow/XIVChatNext/releases/latest) 下载 Windows x64 ZIP，解压到独立目录后运行 `XIVChat Desktop.exe`，保留所有 DLL/资源文件夹。随包包含 .NET 与 Windows App SDK，网页卡片还需 Microsoft Edge WebView2 Runtime。

在 Dalamud 自定义插件仓库添加 `https://raw.githubusercontent.com/AinaSnow/FFXIV-Dalamud-Plugins/main/pluginmaster.json`，安装 **XIVChatNext Server** 并登录角色。请使用同次发布的两端版本。

### 连接

首次引导设置语言、连接与通知，可在设置中重开。客户端支持英文、简体中文及跟随系统；插件新配置默认英文，可独立设置语言。

同机默认 **127.0.0.1:14777**，以插件设置为准；局域网使用游戏电脑地址。端口一致，核对指纹并在两端确认信任。

无法直连时，选择“自建中继”，粘贴有效邀请，与插件独立核对端点指纹后保存，再完成聊天设备信任。正常重启保留配对，授权丢失、撤销或身份变化等情况才需重新配对。详见 [中继指南](SELF_HOSTED_RELAY.md#简体中文)。

点击 Logo 连接最近成功目标，中继成功会话异常中断后自动重试，主动断开可取消。重新打开客户端不会自动发消息或代替游戏登录。

### 日常操作

| 区域 | 用途 |
| --- | --- |
| 会话 | 打开悄悄话、置顶、查看未读；发送前确认固定收件人。 |
| 频道 | 选择发送频道，调整视图和过滤器。 |
| 好友 | 从同步列表进入会话；未知、过期不能当作离线。 |
| 历史 | 选择角色/来源、搜索、离线查看消息。 |
| 收藏 | 保存消息/卡片并加备注。 |
| 事件 | 查看事件，在设置中调整通知。 |

会话可打开为小窗并保存/恢复布局，共享状态，断线保留草稿。其他角色的历史不改变当前发送角色。点击物品/地图链接查看卡片，对比依赖可用数据和装备。截图需插件允许，再从客户端请求并预览、保存、复制。

### 备注名和主播模式（客户端 1.4.0）

右键点击好友或会话，选择“备注名”，也可以点击聊天窗口里的备注名按钮。备注名单行、最多 64 个字符，清空就恢复角色名。不同连接和当前角色各自保存，没聊过天的好友也能设置。它不会覆盖原有的长文本备注，也不会改变消息实际发给谁。好友和会话搜索可以输入备注名或真实姓名；关闭主播模式时，历史全文搜索、复制和导出仍使用原始记录。

主窗口顶部可以一键开关“主播模式”。在设置里选择隐藏自己、其他玩家，或者两者都隐藏；自己默认显示“我”，也能自定义。首次开启默认隐藏双方，重启后会在显示聊天前恢复上次设置。被隐藏的玩家使用固定化名，服务器和头像也会隐藏；即使设置了备注名，也优先显示化名。已经打开的小窗会一起更新。

聊天正文中能确认的完整姓名，以及列表、预览、历史、收藏、事件、通知、复制选区、复制消息和 TXT/RTF 导出，都按同一规则处理。无法确定是哪位同名玩家时，会显示通用匿名名称。修改隐私设置会清理本应用之前发出的通知、清空导出预览并取消正在进行的导出。已经复制出去或保存到别处的内容无法撤回。

国服中文战斗记录中的“正在发动”“发动了”“附加效果”“效果消失”等角色位置也会处理。没有完整角色身份的战斗对象在隐藏他人时显示为“匿名玩家”，不要求对方先聊过天；旧日志也能使用这条规则。仅凭这类文本无法区分所有玩家和怪物，因此无法确认身份的怪物名称也可能匿名。该规则只用于战斗频道的游戏文本，不会把普通聊天中的同类句子当成战斗角色。

带跨服图标的角色名和服务器会一起隐藏。感情动作（包括自定义动作）中的发起者和角色链接目标也按主播模式设置处理，保留动作文字。没有身份链接的自由文本仍受上述识别限制。

点击发送按钮左侧的笑脸可打开“游戏符号”，主窗口和聊天小窗都支持。选中符号会插入光标位置，或替换输入框中的选中文字；随后仍需自己发送。面板收录当前字体可显示的 165 个游戏专用字符和《》两个括号，含 Lodestone 所述的新字符；未分配的空位不列入候选。范围参考 [Lodestone 符号清单](https://jp.finalfantasyxiv.com/lodestone/character/52670623/blog/5654118/)，游戏内最终显示取决于游戏版本。

**游戏截图不会自动打码。** 未知昵称、无法识别的姓名、图片、搜索框和你正在输入的内容仍可能包含身份信息。主播模式开启时不能编辑备注名、原有备注或头像绑定，已经打开的相关编辑框会关闭。本地数据库仍保留原始消息。分享前请检查实际画面或导出文件。

### 更新检查（客户端 1.4.1）

客户端启动后会在后台检查 GitHub 上的公开版本。发现新版 Windows 客户端时，主窗口显示可关闭的提示，点击后进入“设置 → 更新”，查看更新说明或打开下载。这一页也会显示当前版本，并提供“检查更新”按钮。关闭“启动时检查更新”后只保留手动检查，选项立即保存；检查失败不影响聊天。

只比较正式发布的 Windows 客户端安装包，不会把插件版本号当成客户端更新，也不发送聊天或角色信息。下载在浏览器中打开，解压到新文件夹后，先关闭当前客户端再启动新版；程序不会自动覆盖自己。

### 数据与升级

历史、收藏、备注、布局在本机，中继不提供云同步。配置在 `%APPDATA%\XIVChat for Windows`，数据在 `%LOCALAPPDATA%\XIVChatDesktop`，不要公开；受保护凭据绑定当前 Windows 用户。

升级前正常退出并备份，新包解压到独立目录，不混用旧 DLL。通过 Dalamud 更新插件，并保留旧客户端包回退。

客户端 1.4.0 继续兼容插件 1.7.15，历史数据库会从 v5 升到 v6，升级前会在数据库旁生成 `*.before-v6-*.bak` 备份。要退回旧版，先退出客户端，备份当前数据，再把旧客户端、之前的配置和数据库一起恢复；不要用旧客户端打开 v6 数据库。恢复旧备份后，备份之后的新记录不会出现在旧数据库中。

实时发送、好友、截图需要游戏运行、角色在线、插件加载。本应用不保持角色在线，也不能在游戏关闭后发送。游戏/Dalamud 更新可能需要新版插件。
