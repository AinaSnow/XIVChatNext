# Client guide / 客户端指南

## English

### Install

Download the Windows x64 ZIP from [Releases](https://github.com/AinaSnow/XIVChatNext/releases/latest), extract it into its own directory and run `XIVChat Desktop.exe`. Keep all DLL/resource folders. .NET and Windows App SDK are included; web cards also require Microsoft Edge WebView2 Runtime.

Add `https://raw.githubusercontent.com/AinaSnow/FFXIV-Dalamud-Plugins/main/pluginmaster.json` to Dalamud's custom plugin repositories. Install **XIVChatNext Server**, log in, and use matching client/plugin versions from the same release.

### Connect

The first-run guide sets language, connection and notifications; reopen it from Settings. The desktop supports English, Simplified Chinese and System. New plugin configurations default to English and have an independent language setting.

Use **127.0.0.1:14777** on the same PC, unless the port was changed. On a reachable network, use the game PC's address; from plugin 1.7.17, first turn on **Allow LAN connections** in the plugin's Connection settings (new plugin configurations accept only same-PC connections, existing ones keep LAN access). Ports must match. Compare fingerprints and confirm trust on both ends.

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

Select item/map links to inspect cards; comparisons depend on available data/equipment. Request a screenshot from the client, then preview, save or copy it. Any trusted client can request screenshots while the game and plugin are running; from plugin 1.8.0 you can turn this off in the plugin's Messages tab.

### Contact nicknames and streamer mode (Desktop 1.4.0)

Right-click a friend or conversation and choose **Contact nickname**, or use the nickname button in a conversation. Names must fit on one line, up to 64 characters. Clear the field to restore the character name. Nicknames belong to the connection and current character; they also work for friends without saved messages. They do not replace long notes or change the tell recipient. Friend/conversation search accepts both nicknames and real names. Ordinary history search, copying and exports retain original identities when streamer mode is off.

Toggle **Streamer mode** at the top of the main window. In Settings, choose whether to hide yourself, other players, or both, and optionally set your own display name. Both scopes default on. The app remembers the setting before reopening its windows. Protected players use stable pseudonyms, with their server and avatar hidden; pseudonyms take precedence over nicknames. Open popouts update with the main window.

The same display rules apply to known complete names in chat text, previews, history, favorites, events, notifications, selected/copied text and TXT/RTF exports. Ambiguous names use a generic anonymous label. Switching the policy clears this app's previous notifications, clears export previews and cancels running exports. Already copied text, saved files and notifications outside the app's control cannot be recalled.

**This is not automatic image or free-text redaction.** Game screenshots remain unchanged. Unknown nicknames, unrecognized names, images, search fields and text you type may still identify someone. Turn streamer mode off to edit stored notes, nicknames or avatar mappings; an open identity editor closes when it is enabled. The database keeps original messages. Check the actual screen or export before sharing.

### Update checks (Desktop 1.4.1)

The client checks public GitHub releases shortly after startup. If a newer Windows client is available, a dismissible banner opens **Settings → Updates**, where you can read the release notes and open the download. This page also shows the current version and a **Check for updates** button. Turn off **Check for updates at startup** to use manual checks only; the preference saves immediately. Failed checks do not interrupt chat.

Only stable Windows desktop packages are compared, independently of plugin version tags. Checking sends no chat or character information. Downloads open in your browser; extract into a new folder and close the current client before starting the new version. The client does not replace itself automatically.

### Tray and global shortcut (Desktop 1.5.0)

In **Settings → Window**, turn on **Keep running in the notification area** to close the main window without exiting. Click the tray icon to show or hide the window; right-click it for **Exit**. Hover the icon to see unread tells.

Turn on **Global shortcut** and press a combination in the shortcut field (Ctrl+Alt+X by default). The shortcut shows the window with the message box focused, and hides it again when the window is already in front. It works while the game is focused, so pick a combination the game does not use. If another application already owns it, the settings page says so.

### Data and upgrades

History, favorites, notes and layouts stay local; the relay is not cloud history sync. Configuration: `%APPDATA%\XIVChat for Windows`. Data: `%LOCALAPPDATA%\XIVChatDesktop`. Do not publicly share these files. Protected relay credentials belong to the current Windows user.

Close the client normally and back up configuration/data before upgrading. Extract the new package into a separate directory; never mix runtime DLLs. Update the plugin in Dalamud and retain the old client package for rollback.

Desktop 1.4.0 remains compatible with plugin 1.7.15 and upgrades the history database from schema v5 to v6. Migration makes a `*.before-v6-*.bak` backup next to the database. To roll back, close the client and restore the previous configuration and database together with the previous executable; do not open the v6 database with an older client. Keep a copy of the current data first, since restoring a backup omits messages saved after that backup.

Desktop 1.5.0 encrypts the device private key for the current Windows user on first start. A 1.4.x client cannot read the migrated configuration; restore the configuration backed up before upgrading if you roll back.

Live sending, friends and screenshots require the game, logged-in character and plugin. This app cannot keep a character logged in or send after the game closes. Game/Dalamud updates may require a plugin update.

## 简体中文

### 先安装客户端和插件

打开 [下载页面](https://github.com/AinaSnow/XIVChatNext/releases/latest)，下载 `XIVChatNext-Desktop-…-win-x64.zip`。解压到一个新文件夹后运行 `XIVChat Desktop.exe`。不要只拿出 EXE，旁边的文件和文件夹也要保留。

客户端已带有所需的 .NET 和 Windows App SDK。物品、地图等网页卡片还需要 Microsoft Edge WebView2 Runtime。

在 Dalamud 设置 → 实验性功能 → 自定义插件仓库中，添加下面的地址，再安装 **XIVChatNext Server**：

`https://raw.githubusercontent.com/AinaSnow/FFXIV-Dalamud-Plugins/main/pluginmaster.json`

登录游戏角色，在插件设置中启用连接。客户端和插件请使用同一次发布的版本。

### 第一次连接

客户端第一次启动时，会引导你选择语言、连接方式和通知，以后可以从设置里重新打开。客户端支持简体中文、英文和跟随系统；插件新配置默认英文，语言可以单独设置。

**客户端和游戏在同一台电脑上：**地址填 `127.0.0.1`，默认端口填 `14777`。如果改过插件端口，这里也要改成一样的。

**客户端在另一台局域网电脑上：**插件 1.7.17 起，先在插件“连接”设置中开启“允许局域网设备连接”（旧版本的配置升级后自动保持开启），再填写游戏电脑的局域网地址，端口与插件一致。两台电脑需要能够互相访问。

第一次连接时，对照客户端和插件显示的设备指纹。确认一致后，在两边允许连接并保存信任。

**无法直接访问游戏电脑：**可以选择“自建中继”，粘贴你生成的邀请，再按提示核对指纹并完成配对。中继需要自己部署，具体见 [中继使用说明](SELF_HOSTED_RELAY.md#简体中文)。

以后点击左上角 Logo，就能连接上次成功使用的地址。中继连接意外断开后会自动重试；点击主动断开后停止重试。正常重启不需要重新配对，重新打开客户端也不会自动发消息或替你登录游戏。

### 平时怎么用

| 位置 | 可以做什么 |
| --- | --- |
| 会话 | 给好友发悄悄话、置顶常聊的人、查看未读消息。发送前看清楚收件人。 |
| 频道 | 选择要看的消息类型和发送频道。 |
| 好友 | 查看从游戏同步的好友，选中后打开私聊。“未知”或“已过期”表示没有拿到最新状态，不代表好友离线。 |
| 历史 | 按角色和连接来源查看记录，用关键词搜索。已经保存的记录可以离线查看。 |
| 收藏 | 保存消息或物品、地图卡片，也可以写备注。 |
| 事件 | 查看游戏和连接事件，通知方式可以在设置中调整。 |

聊天可以打开为独立小窗，窗口位置可以保存并在下次恢复。主窗口和小窗使用同一段会话，断线后草稿会保留。查看其他角色的历史记录，不会切换当前发消息的角色。

点击消息里的物品或坐标可以看详情。装备对比需要游戏提供相应数据。在客户端请求截图，收到后可以预览、保存或复制。游戏和插件运行时，所有已信任的客户端都可以请求截图；插件 1.8.0 起可以在插件“消息”页关闭。

点击发送按钮左侧的笑脸可以打开“游戏符号”，主窗口和小窗都能用。选中的符号会插入到光标位置，或替换选中的文字，之后仍需要你自己发送。面板收录当前字体能显示的 165 个游戏专用字符和《》，范围参考 [Lodestone 符号清单](https://jp.finalfantasyxiv.com/lodestone/character/52670623/blog/5654118/)，游戏里最终怎么显示取决于游戏版本。

### 备注名和主播模式（客户端 1.4.0）

右键点击好友或会话可以设置备注名，最多 64 个字符，清空后恢复角色名。备注名按连接和当前角色分别保存，没聊过天的好友也能设置。它与原有的长文本备注分开保存，不会改变实际收件人。好友和会话搜索可以输入备注名或真实姓名。

主窗口顶部可以开关主播模式。在设置里选择隐藏自己、其他玩家或两者都隐藏，也能设置自己的显示名称。被隐藏的玩家使用固定化名，服务器和头像一并隐藏，化名优先于备注名。主窗口、小窗、历史、收藏、事件、通知、已知完整姓名、复制聊天和 TXT/RTF 导出使用同一规则。重启后会记住开关状态。

国服战斗记录里的“发动了”“附加效果”等角色位置也会隐藏；无法确认身份的战斗对象显示为“匿名玩家”，个别怪物名称也可能被匿名。带跨服图标的名字和服务器会一起隐藏，感情动作的发起者和目标也会处理。普通聊天里的类似句子不会被当成战斗记录。

切换隐私设置会清理本应用之前发出的通知，并取消正在进行的导出。已经复制或保存到外部的内容无法撤回。**游戏截图不会自动打码**，未知昵称、图片、搜索框和正在输入的文字也不会自动隐藏。本地数据库仍保留原始记录。开启时不能编辑备注名、备注或头像绑定，分享前请检查实际画面。

### 更新检查（客户端 1.4.1）

启动后会在后台检查 GitHub 上的公开版本。发现新版时，主窗口显示可关闭的提示，点击后进入“设置 → 更新”，可以查看更新说明或打开下载。这一页也能手动检查，或者关闭启动检查。检查只比较正式发布的客户端版本，不发送聊天或角色信息，失败也不影响聊天。下载会在浏览器中打开，客户端不会自动替换自己。

### 托盘和全局快捷键（客户端 1.5.0）

在“设置 → 窗口”中开启“关闭主窗口时保留在通知区域（托盘）”，关闭主窗口后程序继续运行。单击托盘图标显示或隐藏窗口，右键可退出；鼠标悬停可以看到未读私聊数。

开启“使用全局快捷键”（默认 Ctrl+Alt+X），点一下快捷键框再按组合键就能修改。按下后显示窗口并聚焦输入框，窗口已在最前时再按一次会隐藏。快捷键在游戏中同样生效，请避开游戏里正在使用的按键；如果已被其他程序占用，设置页会提示。两项默认关闭。

### 数据存在哪里

历史、收藏、备注和窗口布局都保存在运行客户端的电脑上，中继不提供云同步。

- 配置：`%APPDATA%\XIVChat for Windows`
- 聊天记录等数据：`%LOCALAPPDATA%\XIVChatDesktop`

这些目录含有聊天记录和连接信息，请不要公开上传。本设备密钥和中继凭据只能由保存它的 Windows 用户读取，直接复制到另一台电脑或另一个用户下无法使用，需要重新确认信任或重新配对。

### 怎么升级

先正常退出客户端，备份上面的两个目录。把新版解压到新的文件夹，不要混用旧版 DLL。插件通过 Dalamud 更新，客户端和插件请使用同一次发布的版本。保留旧安装包，方便回退。

- 客户端 1.4.0 会把历史数据库从 v5 升到 v6，升级前自动生成 `*.before-v6-*.bak` 备份。不要用旧客户端打开 v6 数据库。
- 客户端 1.5.0 首次启动时会把本设备私钥改为加密保存，1.4.x 客户端读不了迁移后的配置。

回退时，把旧客户端和升级前备份的配置、数据库一起恢复。恢复备份会失去备份之后新增的本地记录，所以先另外保存一份当前数据。

收发新消息、刷新好友和获取截图，都需要游戏运行、角色在线、插件正常加载。客户端不能让角色保持在线，也不能在游戏关闭后代发消息。游戏或 Dalamud 更新后，可能需要更新插件。
