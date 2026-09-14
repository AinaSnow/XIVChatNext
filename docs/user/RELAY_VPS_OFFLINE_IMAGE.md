# 直接上传 Docker 镜像部署（Ubuntu x86_64 + 1Panel）

这是已经构建好的 Linux x86_64 / amd64 中继镜像，包含网页管理后台。VPS 不需要拉取 .NET 基础镜像、下载 NuGet 或编译源码；已有 Docker 和 Compose 即可导入启动。HTTPS 和证书继续使用现有 1Panel。

## 包含什么

上传包：`xivchat-relay-1panel-offline-20260914.zip`，解压后为一个独立目录，包含：

- `xivchat-relay-20260914-linux-amd64.tar.gz`：Docker 镜像，约 117 MiB，直接交给 `docker load`，不必先解压它。
- `compose.yaml`：只运行中继的配置；绑定 `127.0.0.1:18080`，不启动 Caddy、不占用 80/443、不在线拉取镜像。
- `SHA256SUMS`：镜像校验值。
- 本说明，以及 1Panel 反代/HTTPS 详细说明。

镜像标签：`xivchat-relay:20260914-web-admin`。仅用于 Intel/AMD x86_64 VPS，不适用于 ARM。镜像中没有默认管理密码或测试授权数据。

## 1. 上传并解压外层 ZIP

在 1Panel 文件管理中，将外层 ZIP 上传到 `/opt/` 并解压，得到：

```text
/opt/xivchat-relay-1panel-offline-20260914/
```

下文命令在 1Panel 终端或 SSH 的 root 会话执行。进入解压出的目录：

```sh
cd /opt/xivchat-relay-1panel-offline-20260914
uname -m
docker compose version
sha256sum -c SHA256SUMS
docker load -i xivchat-relay-20260914-linux-amd64.tar.gz
```

架构应显示 `x86_64`；校验应为 `OK`；导入应显示 `Loaded image: xivchat-relay:20260914-web-admin`。`docker load` 可以直接加载 gzip 压缩镜像并恢复标签。[Docker 官方说明](https://docs.docker.com/reference/cli/docker/image/load/)

注意：外层 ZIP 是交付包装；Docker 导入的是里面的 `.tar.gz`。若使用 1Panel 的镜像导入界面，也应选择该镜像文件；界面不接受压缩格式时用上面的终端命令。

## 2. 用环境变量设置域名与密码

不需要创建 `.env`。使用附带的 Compose 配置时，在同一个 Bash/root 终端会话设置以下变量，然后继续第 3 节：

```sh
export RELAY_DOMAIN='relay.你的域名.com'
read -r -s -p '管理密码（至少16字符）: ' RELAY_ADMIN_PASSWORD
echo
export RELAY_ADMIN_PASSWORD
```

输入密码时终端不会回显。Compose 将它们转换为中继容器实际读取的 `Relay__PublicUrl` 和 `Relay__AdminPassword`。

如果 1Panel 编排界面提供环境变量输入区域，也可在那里保存 `RELAY_DOMAIN` 和 `RELAY_ADMIN_PASSWORD`，由该编排使用这些变量。

`RELAY_DOMAIN` 不带 `https://`、端口或路径。密码至少 16 字符，可用密码管理器生成 32 位以上随机字母数字密码。请自行保存密码，重建容器时仍需要提供。普通容器重启继续使用创建时的环境变量。

### 如果直接在 1Panel 创建容器

导入镜像后，选择 `xivchat-relay:20260914-web-admin` 创建容器，在容器环境变量区域填写下面的实际变量名；这种方式不使用 Compose 的 `RELAY_DOMAIN` 名称：

| 容器环境变量 | 值 |
| --- | --- |
| `Relay__PublicUrl` | `https://你的真实中继域名` |
| `Relay__AdminPassword` | 你的独立长密码 |
| `Logging__LogLevel__Default` | `Warning` |

变量名中间是 **两个下划线**。同时将容器 TCP 8080 映射至宿主机 `127.0.0.1:18080`，将命名卷 `xivchat-relay_relay-data` 挂载至 `/data`，选择 `unless-stopped` 重启策略。保持镜像默认普通用户，不启用特权模式。若界面无法限制端口只绑定回环地址，使用附带的 Compose 方式。直接创建容器与 Compose 方式二选一，避免创建两套服务；创建后跳到第 4 节。

## 3. 启动

```sh
docker compose config --quiet
docker compose up -d --no-build
docker compose ps
curl -fsS http://127.0.0.1:18080/healthz
```

服务应显示 `healthy`，健康接口包含 `"status":"ok"`。如 18080 已被占用，将 `compose.yaml` 中宿主机端口 18080 改为空闲端口，再同步调整反代。保留 `127.0.0.1` 绑定。

本目录的 `compose.yaml` 专门用于已导入镜像，可以直接运行上述命令。不要与源码包 `deploy/relay/compose.yaml` 混用；源码包的默认配置包含 Caddy。

## 4. 在 1Panel 配置反代与 HTTPS

为中继域名添加 A 记录指向 VPS，在 1Panel 创建反向代理网站：

| 设置 | 内容 |
| --- | --- |
| 主域名 | 环境变量中配置的真实域名 |
| 代理地址 | `http://127.0.0.1:18080` |
| 代理路径 | `/`，转发整个站点 |
| Host / 发送域名 | `$http_host` 或你的真实中继域名 |
| WebSocket | 开启 |
| 缓存 | 关闭 |
| HTTPS | 选择对应域名的有效证书，启用 HTTP 跳转 HTTPS |

上游回环地址要求 OpenResty 使用 host 网络模式；如果你修改过其容器网络，请先核对，bridge 模式不能直接用这个地址访问宿主机。Host 不要填 `127.0.0.1` 或 `$proxy_host`。不要为整个站点增加密码访问或浏览器验证码，这会挡住游戏与桌面客户端。

证书申请、自动续签、WebSocket 配置片段和网络模式核对方法，见附带的 [1Panel HTTPS 说明](RELAY_VPS_HTTPS.md) 第 4–6 节及网络准备说明。该文件中的源码上传/构建步骤不适用于本镜像包，请用本说明第 1–3 节替代。

## 5. 登录和配对

打开 `https://你的中继域名/admin/`，确认浏览器证书正常，再用环境变量中设置的密码登录。

在后台添加游戏设备，复制服务地址与一次显示的注册凭据到游戏插件；设备上线后生成邀请，在桌面端粘贴邀请并与插件核对完整端点指纹，再完成首次设备信任。插件里不填写后台管理密码。

完成后实际发送消息并测试截图，确认公网 HTTPS 与持续 WebSocket 都正常。

## 维护

使用 Compose 时，在本目录执行 `docker compose logs --tail=100 relay` 查看日志。换密码时重新设置 `RELAY_DOMAIN`、`RELAY_ADMIN_PASSWORD` 环境变量，再执行 `docker compose up -d --force-recreate relay`；设备授权保留，后台会话失效，活动连接暂时中断。新终端会话不会继承上次 `export` 的变量，执行 Compose 维护或重建命令前需要再次提供。启动成功后，可执行 `unset RELAY_ADMIN_PASSWORD` 清除当前终端中的变量。

直接在 1Panel 创建容器时，从面板查看日志；修改容器环境变量并按面板流程重新创建容器，保持同一个数据卷。管理密码不支持只改终端变量后直接让运行中的服务生效。

设备授权存于 `xivchat-relay_relay-data` 命名卷。保留数据卷，并自行保存域名、密码和部署配置；HTTPS 证书与站点配置由 1Panel 备份。不要执行 `docker compose down -v` 删除数据。

备份或升级前，参照完整使用指南停止中继写入后备份数据库，并保留旧镜像；以后用新版镜像文件重新 `docker load`，同步修改 `compose.yaml` 中的镜像标签，再执行 `docker compose up -d --no-build`。

## 已完成的验证

镜像来自已通过后台 Windows/Linux 各 47 项测试、原协议各 26/25 项回归及 Linux 容器启动验证的中继源码（提交 `82dda03`）。导出后已重新 `docker load` 成功，下载前后 SHA256 一致，归档中的架构、镜像标签及普通运行用户已检查。

你的 VPS 与 1Panel 尚未现场部署；公网证书和真实游戏连接仍需按上述步骤验收。
