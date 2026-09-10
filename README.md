# XIVChat Next

<p align="center">
  <img src="assets/branding/logo-c1.png" width="100" alt="XIVChat Next Logo" />
</p>

基于原版 [XIVChat](https://xiv.chat/) 改进

---

## ✨ 新增与优化功能

相比于原版，**XIVChat Next** 新增物品地图超链接展示部分功能优化

工作台开发分支现已接入事件页、排本／私聊／关键词／断线通知、分类开关及定时免打扰，并修复登出时失效世界引用引发的每帧报错。功能与实测范围见 [第四阶段记录](docs/PHASE4_NOTIFICATIONS_2026-09-11.md)；这些改动尚未发布到下述正式安装包。
---

## 📖 使用方法

### 1. 安装游戏端插件
在《最终幻想14》卫月框架 (Dalamud / XIVLauncher) 中，打开 **设置 -> 实验性功能 -> 自定义插件仓库**，添加以下链接：
```text
https://raw.githubusercontent.com/AinaSnow/XIVChatNext/refs/heads/main/repo.json
```
保存后，在插件安装器中搜索 **`XIVChatNext Server`** 并安装即可。

### 2. 连接桌面客户端
下载并解压客户端正式发布包 (`XIVChat-Desktop-v1.3.5-win-x64.zip`)，运行 **`XIVChat Desktop.exe`**：
- 新增服务器，填入游戏内提示的连接地址与端口。
- 首次连接时在弹出的“密钥验证”窗口中点击 **“是”** 
