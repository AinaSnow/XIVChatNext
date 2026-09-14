# XIVChat Next

<p align="center">
  <img src="assets/branding/logo-c1.png" width="100" alt="XIVChat Next Logo" />
</p>

基于原版 [XIVChat](https://xiv.chat/) 的桌面聊天工作台，包含 Dalamud 游戏插件、Windows 桌面客户端和可自部署的中继服务。

## 当前开发版

工作台分支已实现本地历史与搜索、好友和固定悄悄话、事件与通知、物品／地图卡片、截图、多窗口，以及首次启动引导和 Logo 快捷连接。插件和客户端均可选择直连或自建中继。

本仓库当前是开发验证版本。下面的旧版安装源及此前桌面正式包，不代表已经包含这些工作台改动；本轮没有发布公共镜像或更新正式插件源。各项实测范围见 [开发与验收文档](docs/README.md)。

## 开始使用开发版

1. 在游戏中加载本分支对应的插件。
2. 启动桌面客户端，按四步引导选择语言、连接位置与端口、通知偏好，最后保存。旧用户保持原设置，可从设置页重新运行引导。
3. 同机直连使用 `127.0.0.1`；默认端口为 `14777`，两端端口必须一致。另一台电脑填写游戏电脑的 IP 或主机名。
4. 首次连接核对两端显示的设备指纹并确认信任。连接成功后，点击首页 Logo 即可连接最近一次成功使用的目标；失败尝试不会覆盖它。

不能直接访问游戏电脑时，可按 [自部署中继指南](docs/user/SELF_HOSTED_RELAY.md) 部署服务，在插件中生成邀请，再在客户端配对。游戏和插件仍需运行。

## 分别构建各组件

在仓库根目录运行：

```powershell
./build.ps1 -Component relay -Configuration Release
./build.ps1 -Component desktop
./build.ps1 -Component plugin
./build.ps1 -Component relay-tests
./build.ps1 -Component regression
```

Relay 可在 Linux 独立构建或通过 Docker 构建，不需要游戏文件、Dalamud 或 WinUI。Windows 两端继续使用各自现有工具链。目录、依赖和测试入口见 [开发指南](docs/development/BUILD.md)。

## 旧正式版安装方式

### 1. 安装游戏端插件
在《最终幻想14》卫月框架 (Dalamud / XIVLauncher) 中，打开 **设置 -> 实验性功能 -> 自定义插件仓库**，添加以下链接：
```text
https://raw.githubusercontent.com/AinaSnow/XIVChatNext/refs/heads/main/repo.json
```
保存后，在插件安装器中搜索 **`XIVChatNext Server`** 并安装即可。

### 2. 连接桌面客户端
旧桌面包名为 `XIVChat-Desktop-v1.3.5-win-x64.zip`；已有本地副本保留在忽略的 `artifacts/legacy-releases/`，不再作为根目录源码跟踪。解压后运行 **`XIVChat Desktop.exe`**：
- 新增服务器，填入游戏内提示的连接地址与端口。
- 首次连接时核对两端显示的指纹，再确认设备信任。
