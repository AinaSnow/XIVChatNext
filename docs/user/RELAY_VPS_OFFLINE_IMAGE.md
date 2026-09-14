# Ubuntu + 1Panel: offline image / 离线镜像部署

## English

Download **XIVChatNext-Relay-1Panel-linux-amd64.zip** from [release 1.7.15](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15). This is for an **x86_64/amd64 Ubuntu VPS**, with Docker and Compose already available. It includes a prebuilt relay image with English/Chinese web administration. No .NET/NuGet download or server-side build is required.

The archive contains the image `xivchat-relay-20260914-linux-amd64.tar.gz`, standalone `compose.yaml`, `SHA256SUMS` and bilingual deployment guides. Image tag: `xivchat-relay:20260914-web-admin`. There is no default password or test authorization data.

### Import and start

Upload/extract the outer ZIP into its own directory. In that directory, using the server terminal:

```sh
uname -m
docker compose version
sha256sum -c SHA256SUMS
docker load -i xivchat-relay-20260914-linux-amd64.tar.gz
export RELAY_DOMAIN='relay.example.com'
read -r -s -p 'Admin password (at least 16 characters): ' RELAY_ADMIN_PASSWORD
echo
export RELAY_ADMIN_PASSWORD
docker compose config --quiet
docker compose up -d --no-build
docker compose ps
curl -fsS http://127.0.0.1:18080/healthz
```

Replace the example domain with yours. Keep the password in your password manager; enter it without echo in the terminal. Use the same terminal session for the environment variables and Compose. The health response should contain `"status":"ok"`. Docker can load the compressed image directly.

The included Compose binds only `127.0.0.1:18080`, persists `/data`, and does not start Caddy or use 80/443. Do not mix it with the source repository's default Compose file.

### Direct 1Panel container creation (alternative)

After importing the image, create one container from `xivchat-relay:20260914-web-admin`. This is an alternative to Compose, not a second service.

| Container environment variable | Value |
| --- | --- |
| `Relay__PublicUrl` | `https://your-real-domain` |
| `Relay__AdminPassword` | Your unique password, at least 16 characters |
| `Logging__LogLevel__Default` | `Warning` |

Use two underscores. Map container TCP 8080 to host **127.0.0.1:18080**, attach a persistent named volume to `/data`, and use `unless-stopped`. Keep the default non-root user and no privileged mode. If the panel cannot bind to loopback, use the provided Compose method.

### Domain and HTTPS

Create an A record for a dedicated subdomain and configure a 1Panel reverse-proxy website. Target `http://127.0.0.1:18080`, proxy the whole `/`, preserve `Host: $http_host` (or the real domain), enable WebSocket and disable caching. Issue/enable a valid certificate and use HTTPS. This loopback target assumes OpenResty runs with host networking; a customized bridge network needs its corresponding shared-network route.

Do not expose 18080/8080/14777 to the Internet. Do not proxy only `/admin/`: clients use `/v1/`. Full settings are in [HTTPS deployment](RELAY_VPS_HTTPS.md).

Open `https://your-domain/admin/`, sign in, add the game device and pair the client using the [relay guide](SELF_HOSTED_RELAY.md).

### Maintenance

Normal restarts retain the container's environment and saved authorization. Recreating a container requires supplying the domain/password again. Back up the data volume with the service stopped before upgrades; retain the old image. To inspect problems, use `docker compose logs --tail=100 relay`. Never delete the data volume during a normal update.

## 简体中文

从 [1.7.15 发布](https://github.com/AinaSnow/XIVChatNext/releases/tag/1.7.15) 下载 **XIVChatNext-Relay-1Panel-linux-amd64.zip**，适用于已有 Docker/Compose 的 **Ubuntu x86_64/amd64 VPS**。包含中英文网页后台的预编译镜像，不需要现场下载 .NET/NuGet 或构建。

包内有 `xivchat-relay-20260914-linux-amd64.tar.gz`、独立 `compose.yaml`、`SHA256SUMS` 和双语指南。镜像标签 `xivchat-relay:20260914-web-admin`，无默认密码或测试授权。

### 导入与启动

上传并解压外层 ZIP，在解压目录中执行上方命令，先确认架构、校验和、导入镜像，再配置域名与管理密码并启动。域名替换为真实子域名；密码至少 16 字符并自行保存，终端输入不回显。环境变量与 Compose 在同一终端会话执行。健康检查应含 `"status":"ok"`。

Docker 可直接导入压缩镜像。附带 Compose 仅绑定 `127.0.0.1:18080`，持久保存 `/data`，不启动 Caddy、不占用 80/443，不要混用源码包的默认配置。

### 1Panel 直接创建容器（替代方式）

导入后选择 `xivchat-relay:20260914-web-admin` 创建容器，与 Compose 二选一。实际容器变量为 `Relay__PublicUrl=https://真实域名`、`Relay__AdminPassword=独立长密码`、`Logging__LogLevel__Default=Warning`，中间均为两个下划线。

容器 8080 映射到宿主机 **127.0.0.1:18080**，持久卷挂载 `/data`，重启策略 `unless-stopped`，保持默认普通用户、不启用特权。若界面不能绑定回环地址，使用附带 Compose。

### 域名与 HTTPS

将独立子域名 A 记录指向 VPS，1Panel 创建反向代理网站，目标 `http://127.0.0.1:18080`、路径 `/`、Host 保留 `$http_host` 或真实域名，开启 WebSocket，关闭缓存，申请启用有效证书并通过 HTTPS 访问。

回环目标适用于 OpenResty host 网络，自定义 bridge 网络需相应共享网络路由。不要公网开放 18080/8080/14777，也不要只代理 `/admin/`，客户端还访问 `/v1/`。详见 [HTTPS 指南](RELAY_VPS_HTTPS.md#简体中文)。

打开 `https://你的域名/admin/`，登录后按 [中继指南](SELF_HOSTED_RELAY.md#简体中文) 添加游戏设备并配对客户端。

### 维护

普通重启保留容器环境与授权；重新创建需再提供域名和密码。升级前停服备份数据卷并保留旧镜像，用 `docker compose logs --tail=100 relay` 排查。常规升级不要删除数据卷。
