# Build guide / 构建说明

## English

| Directory | Component |
| --- | --- |
| `XIVChat Desktop` | Windows client |
| `XIVChatPlugin` | Dalamud plugin |
| `XIVChatCommon`, `XIVChatStorage` | Contracts and local storage |
| `src` | Relay service, protocol and transport |
| `deploy/relay` | Docker deployment |
| `assets/branding` | Selected artwork |

Desktop builds need Windows x64, .NET 9 or a compatible newer SDK, and Windows SDK/WinUI tools. The target is `net9.0-windows10.0.26100.0`. Plugin builds need compatible installed Dalamud development libraries and the toolchain selected by `Dalamud.NET.Sdk/14.0.1`. The manifest targets Dalamud API 15; the SDK package and API level use different version identifiers. Relay builds need .NET 10 or Docker and work on Linux without game/Dalamud/WinUI files.

```powershell
./build.ps1 -Component desktop -Configuration Release
./build.ps1 -Component plugin -Configuration Release
./build.ps1 -Component relay -Configuration Release
./pack.ps1 -Label release-1.7.15
```

Packaging supports `-Component desktop`, `plugin` or `all`. It creates a new ignored `artifacts/<label>` folder and refuses to overwrite existing output. .NET and Windows App SDK are included; framework bootstrap stays at the SDK default. Runtime resources retain English, Japanese, German, Chinese and French; application UI is English/Chinese. Native MUI folders are pruned separately from managed satellites.

The plugin ZIP comes directly from the SDK's `XIVChatNext/latest.zip`, checked for root-level files and no nested ZIP. Do not ZIP the entire build directory. Packages include SHA-256 checksums.

Relay CI builds the Linux solution and checks container startup with a read-only root filesystem and a writable volume. Maintained privacy/storage and Windows UI checks are in [tests](../../tests/README.md). Retired tests and process records remain archived outside the repository; product builds do not reference them.

## 简体中文

目录包含 Windows 客户端、Dalamud 插件、共享协议、本地存储、中继服务、部署和品牌资源，具体见上表。

客户端需要 Windows x64、.NET 9 或兼容更新 SDK、Windows SDK/WinUI 工具，目标为 `net9.0-windows10.0.26100.0`。插件需要匹配的 Dalamud 开发库及 `Dalamud.NET.Sdk/14.0.1` 所选工具链；清单对应 API 15，构建 SDK 与 API 级别编号不同。中继需要 .NET 10 或 Docker，可在 Linux 独立构建。

使用上方命令构建。打包支持 `desktop`、`plugin`、`all`，创建忽略目录 `artifacts/<label>`，拒绝覆盖同名目录。客户端包含 .NET 和 Windows App SDK，由 SDK 选择初始化；运行库保留英日德中法，应用界面为中英文。原生 MUI 与托管语言资源分别精简。

插件直接使用 SDK 的 `XIVChatNext/latest.zip`，检查根目录入口且无嵌套 ZIP，不要压缩整个构建目录。输出附带 SHA-256 校验。

中继 CI 构建 Linux 解决方案并检查只读根文件系统、持久卷下的容器启动。本次维护的隐私、存储和 Windows 窗口检查放在 [tests](../../tests/README.md)，运行方法见该目录。旧测试和过程记录仍在仓库外归档，产品构建不依赖这些路径。
