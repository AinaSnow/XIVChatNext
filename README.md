# XIVChat Next

<p align="center"><img src="assets/branding/logo-c1.png" width="100" alt="XIVChat Next messenger bird" /></p>

**English** · [简体中文](#简体中文)

A Windows chat workspace for **FINAL FANTASY XIV**, built on [XIVChat](https://xiv.chat/). Keep conversations, friends, searchable history and game screenshots close at hand while your game stays running.

[Website & getting started](https://ainasnow.github.io/XIVChatNext-site/) · [Downloads](https://github.com/AinaSnow/XIVChatNext/releases/latest) · [Client guide](docs/user/CLIENT_GUIDE.md) · [Self-hosted relay](docs/user/SELF_HOSTED_RELAY.md)

## Download

| Component | Version | Download |
| --- | --- | --- |
| Windows x64 desktop client | **1.3.6** | [Desktop ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.15/XIVChatNext-Desktop-v1.3.6-win-x64.zip) |
| Dalamud plugin | **1.7.15** | Install through the repository below, or [latest.zip](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.15/latest.zip) |
| Optional self-hosted relay | Included with this release | [Release assets](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15) |

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

**Keep the game running and your character logged in for live chat, friends and screenshots.** Local history remains accessible offline. Presence/game data depend on the installed game and Dalamud version. Upgrade the client and plugin together. New self-hosted relay credentials are not compatible with legacy public-relay codes.

## Build and documentation

```powershell
./build.ps1 -Component desktop -Configuration Release
./build.ps1 -Component plugin -Configuration Release
./build.ps1 -Component relay -Configuration Release
./pack.ps1 -Label release-1.7.15
```

[Build guide](docs/development/BUILD.md) · [Documentation](docs/README.md) · [Changelog](CHANGELOG.md)

Product source, deployment files and maintained documentation live here. Local tests, process notes and retired artwork are archived outside the product tree and are not included in release packages.

An independent community project based on XIVChat, originally created by Anna. Not affiliated with Square Enix. FINAL FANTASY XIV is a trademark of Square Enix.

---

## 简体中文

XIVChat Next 是基于 [XIVChat](https://xiv.chat/) 的 **FFXIV Windows 桌面聊天工作台**。游戏运行时，将会话、好友、可搜索历史和游戏截图放在手边。

[介绍与入门](https://ainasnow.github.io/XIVChatNext-site/zh/) · [下载](https://github.com/AinaSnow/XIVChatNext/releases/latest) · [客户端指南](docs/user/CLIENT_GUIDE.md#简体中文) · [自建中继](docs/user/SELF_HOSTED_RELAY.md#简体中文)

### 下载与安装

- **客户端 1.3.6**：[Windows x64 ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.15/XIVChatNext-Desktop-v1.3.6-win-x64.zip)，解压后运行 `XIVChat Desktop.exe`。
- **插件 1.7.15**：通过上面的 Dalamud 自定义插件仓库安装 **XIVChatNext Server**，或使用 [插件 ZIP](https://github.com/AinaSnow/XIVChatNext/releases/download/1.7.15/latest.zip)。
- **可选中继**：从 [本次发布](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15) 取得 Ubuntu x86_64 + 1Panel 离线部署包。

客户端包含 .NET 和 Windows App SDK，保留英、日、德、中、法运行库资源，**应用界面为中英文**。网页卡片需要 Microsoft Edge WebView2 Runtime。插件新配置默认英文，可切换中文或跟随系统，已有选择保留。

### 首次连接

1. 登录角色并加载插件，启用需要的连接方式。
2. 打开客户端，完成语言、连接与通知引导。
3. 同机默认 `127.0.0.1:14777`，局域网用游戏电脑地址，两端端口须一致。
4. 核对两端指纹并确认信任。之后点击 Logo 连接最近成功目标。
5. 无法直连时，使用自建 HTTPS 中继，通过中英文网页后台添加设备、生成邀请和管理授权。保存凭据与信任后，正常重启不需重新邀请。

### 功能与说明

支持频道与固定悄悄话、置顶与未读、好友状态及可选头像、按角色隔离的本地历史/搜索/收藏/备注/导出、物品和地图卡片、可用装备对比、截图预览/保存/复制、小窗与布局、事件通知及可选中继。

实时功能需要**游戏运行、角色在线、插件加载**，已保存历史可离线查看。游戏与 Dalamud 更新可能影响好友状态和数据，请配套升级两端；新中继不兼容旧公共中继认证码。

使用上方命令构建，详见 [构建说明](docs/development/BUILD.md#简体中文)、[文档](docs/README.md#简体中文) 和 [更新说明](CHANGELOG.md#简体中文)。仓库保留产品源码、部署文件和维护中的说明，测试、过程记录和旧素材在仓库外本地归档，不进入发布包。

本项目基于 Anna 创建的 XIVChat，与 Square Enix 无关联。FINAL FANTASY XIV 是 Square Enix 的商标。
