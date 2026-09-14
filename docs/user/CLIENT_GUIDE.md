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

### Data and upgrades

History, favorites, notes and layouts stay local; the relay is not cloud history sync. Configuration: `%APPDATA%\XIVChat for Windows`. Data: `%LOCALAPPDATA%\XIVChatDesktop`. Do not publicly share these files. Protected relay credentials belong to the current Windows user.

Close the client normally and back up configuration/data before upgrading. Extract the new package into a separate directory; never mix runtime DLLs. Update the plugin in Dalamud and retain the old client package for rollback.

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

### 数据与升级

历史、收藏、备注、布局在本机，中继不提供云同步。配置在 `%APPDATA%\XIVChat for Windows`，数据在 `%LOCALAPPDATA%\XIVChatDesktop`，不要公开；受保护凭据绑定当前 Windows 用户。

升级前正常退出并备份，新包解压到独立目录，不混用旧 DLL。通过 Dalamud 更新插件，并保留旧客户端包回退。

实时发送、好友、截图需要游戏运行、角色在线、插件加载。本应用不保持角色在线，也不能在游戏关闭后发送。游戏/Dalamud 更新可能需要新版插件。
