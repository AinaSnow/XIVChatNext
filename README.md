# XIVChat Next

<p align="center"><img src="assets/branding/logo-c1.png" width="100" alt="XIVChat Next messenger bird" /></p>

**English** · [简体中文](#简体中文)

A Windows chat workspace for **FINAL FANTASY XIV**, built on [XIVChat](https://xiv.chat/). Keep conversations, friends, searchable history and game screenshots close at hand while your game stays running.

**Desktop 1.4.0 · Plugin 1.7.16** adds contact nicknames, streamer mode and a game-symbol picker, and fixes CN item-link messages being lost before forwarding.

[Website & getting started](https://ainasnow.github.io/XIVChatNext-site/) · [Downloads](https://github.com/AinaSnow/XIVChatNext/releases/latest) · [Client guide](docs/user/CLIENT_GUIDE.md) · [Self-hosted relay](docs/user/SELF_HOSTED_RELAY.md)

## Download

| Component | Version | Download |
| --- | --- | --- |
| Windows x64 desktop client | **1.4.0** | [Desktop ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.16/XIVChatNext-Desktop-v1.4.0-win-x64.zip) |
| Dalamud plugin | **1.7.16** | Install through the repository below, or [latest.zip](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.16/latest.zip) |
| Optional self-hosted relay | Unchanged | [Existing relay package](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15) |

The desktop ZIP includes .NET and Windows App SDK dependencies. Runtime language resources are limited to English, Japanese, German, Chinese and French; the application UI supports **English and Simplified Chinese**. Web-based cards use Microsoft Edge WebView2 Runtime.

## Start chatting

1. In Dalamud settings, add this custom plugin repository:

   ```text
   https://raw.githubusercontent.com/AinaSnow/FFXIV-Dalamud-Plugins/main/pluginmaster.json
   ```

2. Install **XIVChatNext Server**, log in to a character and enable your connection in the plugin settings. New plugin configurations default to English; Chinese and System are available.
3. Extract the desktop ZIP and run **XIVChat Desktop.exe**. Follow the first-run guide to choose language, connection and notifications.
4. On the same computer, connect to **127.0.0.1:14777**, unless the plugin port was changed. On a reachable network, use the game PC's address.
5. Compare device fingerprints and confirm trust on both ends. Afterwards, clicking the Logo connects to the most recently successful target.

Without direct access to the game PC, use your own HTTPS relay. It includes an English/Chinese web administration page for devices, invitations, sessions and revocation. **Normal restarts do not need new invitations** once credentials and trust have been saved.

## Features

- Channels, fixed-target tells, pinned conversations and unread counts.
- Game-synced friends, available presence and optional Lodestone avatars.
- Character-scoped local history, search, favorites, notes and export.
- Item/map cards and available equipment comparisons.
- Game screenshots with preview, save and copy.
- Chat popouts, saved layouts and event notifications.
- Direct connections or an optional self-hosted relay.

Desktop 1.4.0 adds local contact nicknames and a streamer-mode switch for names, worlds and avatars, including copied chat and text exports. See the [scope and limitations](docs/user/CLIENT_GUIDE.md#contact-nicknames-and-streamer-mode-desktop-140) before sharing your screen.

**Keep the game running and your character logged in for live chat, friends and screenshots.** Local history remains accessible offline. Presence/game data depend on the installed game and Dalamud version. Upgrade the client and plugin together. New self-hosted relay credentials are not compatible with legacy public-relay codes.

## Build and documentation

```powershell
./build.ps1 -Component desktop -Configuration Release
./build.ps1 -Component plugin -Configuration Release
./build.ps1 -Component relay -Configuration Release
./pack.ps1 -Label release-1.7.16
```

[Build guide](docs/development/BUILD.md) · [Documentation](docs/README.md) · [Changelog](CHANGELOG.md)

Product source, deployment files, documentation and [privacy regression checks](tests/README.md) live here. Retired tests, process notes and artwork remain archived outside the product tree.

An independent community project based on XIVChat, originally created by Anna. Not affiliated with Square Enix. FINAL FANTASY XIV is a trademark of Square Enix.

---

## 简体中文

不用切回游戏，也能收发 FF14 消息。XIVChat Next 基于 [XIVChat](https://xiv.chat/)，把聊天放到一个单独的 Windows 窗口里。你可以给好友发悄悄话、查看聊天记录，也可以把常聊的人放进独立小窗。

使用时，**游戏需要保持运行，角色也需要在线**。

**客户端 1.4.0 · 插件 1.7.16**：新增好友备注名、主播模式和游戏符号面板，并修复国服物品链接消息丢失。

[介绍与入门](https://ainasnow.github.io/XIVChatNext-site/zh/) · [下载](https://github.com/AinaSnow/XIVChatNext/releases/latest) · [客户端指南](docs/user/CLIENT_GUIDE.md#简体中文) · [自建中继](docs/user/SELF_HOSTED_RELAY.md#简体中文)

### 下载与安装

- **客户端 1.4.0**：[Windows x64 ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.16/XIVChatNext-Desktop-v1.4.0-win-x64.zip)，解压后运行 `XIVChat Desktop.exe`。
- **插件 1.7.16**：通过上面的 Dalamud 自定义插件仓库安装 **XIVChatNext Server**，或使用 [插件 ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.16/latest.zip)。
- **可选中继**：继续使用 [已有部署包](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15)，本次无需更新中继。

客户端包含 .NET 和 Windows App SDK，保留英、日、德、中、法运行库资源，**应用界面为中英文**。网页卡片需要 Microsoft Edge WebView2 Runtime。插件新配置默认英文，可切换中文或跟随系统，已有选择保留。

### 首次连接

1. 在 Dalamud 中添加上面的插件仓库，安装 **XIVChatNext Server**，登录游戏并在插件里启用连接。
2. 下载并解压客户端，运行 `XIVChat Desktop.exe`，按提示设置语言、连接和通知。
3. 客户端和游戏在同一台电脑时，地址填 `127.0.0.1`，默认端口是 `14777`。如果改过插件端口，两边要填一致。局域网连接则填游戏电脑的地址。
4. 核对两端显示的指纹，确认是自己的设备后允许连接。以后点击 Logo 就能连接上次成功使用的目标。
5. 如果无法直接连接，可以在自己的服务器上部署 HTTPS 中继。后台可以添加设备、生成邀请和撤销授权；正常重启不需要重新邀请。中继需要自己部署，不是公共服务。

### 功能与说明

- **频道和私聊分开看**：按频道查看消息，把常聊的人置顶，查看未读提醒。
- **聊天可以单独开个小窗**：把窗口放在屏幕一角，边做别的事边看消息。
- **以前聊过的内容可以搜索**：记录保存在这台电脑上，也能收藏消息、添加备注和导出。
- **从好友列表直接开始聊天**：查看游戏里的好友和能获取到的在线状态，选中好友就能打开私聊。
- **消息里的物品和坐标可以点开看**：查看物品资料、地图位置和支持的装备对比。
- **在客户端查看游戏截图**：让游戏电脑截图，再预览、保存或复制。需要先在插件里允许截图。

1.4.0 可以给好友设置独立的备注名，也能打开主播模式，把需要隐藏的角色改成化名，并隐藏服务器和头像。复制聊天、导出 TXT/RTF 时也按同样规则处理。**游戏截图不会自动打码**，正文里的未知昵称和输入框内容也不会自动隐藏。详细范围见[客户端指南](docs/user/CLIENT_GUIDE.md#备注名和主播模式客户端-140)。

实时功能需要**游戏运行、角色在线、插件加载**，已保存历史可离线查看。游戏与 Dalamud 更新可能影响好友状态和数据，请配套升级两端；新中继不兼容旧公共中继认证码。

使用上方命令构建，详见 [构建说明](docs/development/BUILD.md#简体中文)、[文档](docs/README.md#简体中文) 和 [更新说明](CHANGELOG.md)。本次新增的回归检查放在 [tests](tests/README.md)，可直接运行；旧测试、过程记录和旧素材仍在仓库外归档。

本项目基于 Anna 创建的 XIVChat，与 Square Enix 无关联。FINAL FANTASY XIV 是 Square Enix 的商标。
