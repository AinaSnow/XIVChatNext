# 组件与构建

## 当前目录

```text
XIVChat Desktop/                  Windows WinUI 桌面端
XIVChatPlugin/                    Dalamud 插件
XIVChatCommon/                    现有应用协议与加密实现
XIVChatStorage/                   桌面历史存储
src/XIVChat.Relay/                .NET 10 独立转发服务与网页管理后台
src/XIVChat.Relay.Protocol/       .NET 9 最小中继控制契约
src/XIVChat.Relay.Transport/      .NET 9 端点传输、TLS 与系统凭据保护
tests/                           回归及隔离界面测试
deploy/relay/                    Dockerfile、Compose、Caddy 与环境示例
docs/                            使用、开发、协议与验收
assets/branding/                 当前品牌资源及 legacy SVG
artifacts/                       忽略的本地产物
```

本轮先建立组件边界与独立入口。旧插件、桌面、Common 和 Storage 目录保留，避免业务接入时同时改动所有资源和项目引用；后续小批量迁移时必须更新路径并单独验证。不拆成三个仓库，也不重写 Git 历史。

Relay 仅依赖 `XIVChat.Relay.Protocol`，不引用 WinUI、Dalamud、Storage、Sodium 或游戏文件。TLS 和现有聊天加密在两端运行。三端沿用独立版本号；中继控制协议版本当前为 `1`，不与旧公共中继协议兼容。

## 工具链与命令

服务端使用 .NET 10 SDK。桌面端需要 Windows SDK、.NET 9 目标框架和 Windows App SDK；插件使用项目中指定的 Dalamud SDK。`global.json` 允许已安装的新主版本 SDK 选择，应用目标框架并未一起升级。

```powershell
./build.ps1 -Component relay -Configuration Release
./build.ps1 -Component desktop -Configuration Debug
./build.ps1 -Component plugin -Configuration Debug
./build.ps1 -Component relay-tests
./build.ps1 -Component relay-admin-tests
./build.ps1 -Component regression
```

Linux 独立构建无需 PowerShell：

```sh
dotnet build XIVChat.Relay.slnx -c Release
dotnet run --project tests/Relay/RelayTests.csproj -c Debug --no-launch-profile
dotnet run --project tests/RelayAdmin/RelayAdminTests.csproj -c Debug --no-launch-profile
docker build -f deploy/relay/Dockerfile -t xivchat-relay:0.1.0 .
```

中继回归会启动真实服务子进程，并在 `artifacts/relay-tests-*` 创建独立数据库。固定使用 Debug，因为该测试子进程读取 Debug 服务输出。测试将自行停止服务，不连接用户游戏。

网页后台测试使用 `artifacts/relay-admin-tests-*` 隔离数据库，覆盖页面资源、密码登录、会话退出/重启失效、Host/Origin/CSRF 限制、网页设备注册、旧桌面邀请解码兼容、真实端点 TLS、撤销和持久化。Windows 上端点证书需要正常用户密钥存储访问权限。后台资源位于 `src/XIVChat.Relay/wwwroot/admin/`，直接随 .NET 输出与镜像发布，不需要 Node.js 或前端构建服务。

`.github/workflows/relay.yml` 提供 Linux 回归、镜像构建和只读容器启动检查。工作流文件已提供；本地/远程命令验证不等于 GitHub Actions 已运行。

## Windows 界面回归

使用 `tests/DesktopSetupSmoke.targets` 可构建隔离的引导测试程序，使用 `tests/DesktopSmoke.targets` 运行原桌面/通知回归。配置写入测试输出目录，不写入正常用户配置。测试入口需要桌面会话。

```powershell
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild '-p:CustomAfterMicrosoftCommonTargets=完整路径/tests/DesktopSetupSmoke.targets'
```

运行生成的 EXE 后，检查输出目录的 `setup-smoke-results.txt`。若显式提供 `XIVCHAT_SETUP_RELAY_TEST` 环境变量，值应为隔离测试 JSON 的路径，包含 `Server`、`DeviceId`、`Credential`；才会追加跨主机中继配对、重连和取消测试。该文件含临时凭据，不提交或打包，测试完成后撤销对应设备。

测试构建更换了程序入口。交付前必须不带 `CustomAfterMicrosoftCommonTargets` 重新构建正常桌面程序：

```powershell
dotnet build 'XIVChat Desktop/XIVChat Desktop.csproj' -c Debug -t:Rebuild
```

## 构建产物与发布

插件正常构建会通过 Dalamud 打包器生成插件包；桌面端和中继分别使用各自项目的 `dotnet publish`。本地 ZIP、构建日志及临时测试数据放入 `artifacts/`。

统一生成桌面端与插件分发包：

```powershell
./pack.ps1
# 或只生成其中一端；同名目录已存在时拒绝覆盖
./pack.ps1 -Component desktop -Label preview-desktop
./pack.ps1 -Component plugin -Label preview-plugin
```

桌面包包含 .NET 和 Windows App SDK 运行库，保留英、日、德、中、法的语言资源；应用自身仍提供中英文界面。脚本同时清理不受 `SatelliteResourceLanguages` 控制的 WinUI 原生 MUI 目录，保留中性回退资源。Windows App SDK 的引导初始化使用 SDK 默认选择：依赖框架的开发构建启用，携带运行库的分发构建不强制启用，避免两套运行库初始化冲突。

插件直接分发 Dalamud SDK 生成的 `XIVChatNext/latest.zip`，校验主 DLL 和清单位于压缩包根目录且无嵌套 ZIP；不要再压缩整个 `bin/Release`。输出还包含 `SHA256SUMS.txt`。

正式发布前单独更新对应组件版本与更新说明，验证干净机器依赖、升级与回滚，再发布对应包或镜像。本轮没有推送正式插件源、公共镜像或 GitHub Release。不要以旧桌面包 `1.3.5` 或旧插件源作为本分支新功能的验证对象。
