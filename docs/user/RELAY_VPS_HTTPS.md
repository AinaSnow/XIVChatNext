# Ubuntu + 1Panel HTTPS / HTTPS 部署

## English

Use the [offline image](RELAY_VPS_OFFLINE_IMAGE.md) if available. This guide also covers building from source on Ubuntu. It assumes 1Panel/OpenResty already owns ports 80/443 and uses host networking; the relay listens only at `127.0.0.1:18080`.

### Source deployment

Download the source archive from [Releases](https://github.com/AinaSnow/XIVChatNext/releases/latest), extract it into its own directory and enter `deploy/relay`. The server needs Docker/Compose and access to Microsoft container images and NuGet.

```sh
cp .env.example .env
# Edit .env: real RELAY_DOMAIN and a unique RELAY_ADMIN_PASSWORD (16+ characters).
chmod 600 .env
docker compose -f compose.1panel.yaml config --quiet
docker compose -f compose.1panel.yaml up -d --build
docker compose -f compose.1panel.yaml ps
curl -fsS http://127.0.0.1:18080/healthz
```

Use the **standalone `compose.1panel.yaml`** explicitly. The default `compose.yaml` includes Caddy and would compete for 80/443. Do not merge the two configurations. Keep passwords out of source control and do not execute `.env` as a script.

### DNS and reverse proxy

Create an A record for a dedicated subdomain pointing to the VPS. Avoid an AAAA record if IPv6 is not available. Do not deploy under a path such as `example.com/relay`.

| 1Panel setting | Value |
| --- | --- |
| Domain | Your configured relay subdomain |
| Proxy target | `http://127.0.0.1:18080` |
| Path | `/` |
| Host | `$http_host` or your actual domain |
| WebSocket | Enabled |
| Cache | Disabled |

Do not use `127.0.0.1` or `$proxy_host` as the Host. Route the whole site, including `/admin/`, `/v1/` and `/healthz`. Keep 18080/8080/14777 private. If 18080 is taken, choose a free host port and update the proxy, retaining the loopback binding.

For an existing proxy location, the relevant settings are:

```nginx
location / {
    proxy_pass http://127.0.0.1:18080;
    proxy_http_version 1.1;
    proxy_set_header Host $http_host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
    proxy_buffering off;
    proxy_cache off;
}
```

Modify the matching location; do not append a duplicate or overwrite unrelated certificate settings. In a customized bridge-network OpenResty container, loopback is the container itself, so use a properly configured shared network instead of public backend exposure.

### Enable HTTPS and finish

Request/install a valid certificate in 1Panel, enable HTTPS and verify `https://your-domain/healthz`. Open `https://your-domain/admin/`, sign in and follow [device pairing](SELF_HOSTED_RELAY.md).

The configured `Relay__PublicUrl`, browser address and forwarded Host must agree. Admin sign-in over the bare local HTTP backend is not the production login path. Check `docker compose -f compose.1panel.yaml logs --tail=100 relay` if the service fails.

Back up the relay data volume and keep the old image before upgrades. Keep the source version, deployment configuration and data snapshot together for rollback.

## 简体中文

有离线镜像时优先使用 [离线部署](RELAY_VPS_OFFLINE_IMAGE.md#简体中文)。本指南也支持 Ubuntu 源码构建，假定 1Panel/OpenResty 已使用 80/443 和 host 网络，中继仅监听 `127.0.0.1:18080`。

### 源码部署

从 [Releases](https://github.com/AinaSnow/XIVChatNext/releases/latest) 下载源码归档，独立解压并进入 `deploy/relay`。服务器需 Docker/Compose，可访问 Microsoft 镜像和 NuGet。

执行上方命令，复制并编辑 `.env`：填写真实域名与至少 16 字符的独立密码，限制权限，再用 **`-f compose.1panel.yaml`** 检查、构建和启动。默认 `compose.yaml` 含 Caddy，会占用 80/443，不要省略参数或合并配置。密码不要提交，`.env` 不要当脚本执行。

### DNS 与反代

独立子域名 A 记录指向 VPS，没有 IPv6 时不要添加 AAAA，不使用子路径部署。1Panel 反代目标 `http://127.0.0.1:18080`、路径 `/`、Host 为 `$http_host` 或真实域名、开启 WebSocket、关闭缓存。

Host 不能用 `127.0.0.1` 或 `$proxy_host`。整个站点的 `/admin/`、`/v1/`、`/healthz` 都要转发；不要公网开放 18080/8080/14777。端口占用时改宿主机端口并同步反代，仍保留回环绑定。

上方 Nginx 示例用于修改现有对应 location，不要重复追加 location 或覆盖无关证书配置。OpenResty 若自定义为 bridge 网络，回环地址只指容器自身，应配置共享网络，不要改为公网暴露后端。

### HTTPS 与完成配对

在 1Panel 申请/安装有效证书、启用 HTTPS，验证 `https://你的域名/healthz`，再登录 `https://你的域名/admin/` 并按 [中继说明](SELF_HOSTED_RELAY.md#简体中文) 配对。

`Relay__PublicUrl`、浏览器访问域名与转发 Host 要一致；本机 HTTP 后端不是生产登录地址。故障可查看 `docker compose -f compose.1panel.yaml logs --tail=100 relay`。

升级前备份数据卷并保留旧镜像，回退时保留匹配的源码版本、部署配置与数据快照。
