# Self-hosted relay / 自建中继

## English

The plugin and desktop both connect to your HTTPS server when the game PC is not directly reachable. The relay forwards application traffic; it does not proxy the game, keep a character logged in, or store offline chats/screenshots.

### Deploy

Use the [Ubuntu + 1Panel offline image guide](RELAY_VPS_OFFLINE_IMAGE.md) for the release's prebuilt Linux amd64 image. For source builds and 1Panel HTTPS setup, see [VPS HTTPS](RELAY_VPS_HTTPS.md).

On a server without an existing reverse proxy, the repository's default `deploy/relay/compose.yaml` includes Caddy. In that directory, copy `.env.example` to `.env`, set a real `RELAY_DOMAIN` and independent `RELAY_ADMIN_PASSWORD` (at least 16 characters), then run `docker compose up -d --build`. Caddy needs inbound 80/443; do not publish game port 14777 or backend 8080. Do not combine this default configuration with the 1Panel-only configuration.

Open `https://your-domain/admin/` and sign in with the administrator password. Use the HTTPS root address as the service URL, without a subpath, query, username or password. Plain HTTP is allowed only for local development on loopback.

### Register the game and pair the desktop

1. In **Game devices**, add a named device and save the registration credential shown only once.
2. Enter the service URL/credential in the plugin, save and enable Relay. Wait for the device to show online.
3. Generate an invitation in the web admin or plugin. A new invitation invalidates the previous unused invitation.
4. In the desktop, choose Self-hosted relay and paste the invitation. Compare the complete endpoint fingerprint with the plugin through an independent trusted channel, then save.
5. Complete the existing chat-device trust confirmation on both ends.

Registration, invitations and desktop credentials are separate from the admin password. Each desktop has its own credential. Credentials are protected for the current Windows user; moving PCs/users or losing configuration may require new pairing.

**A normal restart does not require another invitation.** The ten-minute invitation lifetime applies only to its first, one-time exchange. Saved credentials and trust remain valid until revoked. Configuration save failure, revoked/lost credentials or a replaced endpoint certificate can require re-pairing.

After a successful session, the Logo can connect to the last successful target. Interrupted successful relay sessions retry with backoff; explicit Disconnect cancels retries.

### Administration and operation

The English/Chinese web admin lists devices, clients and live sessions. Rename devices, create invitations, revoke a client or revoke a whole game device. Original credentials cannot be displayed again. Revocation normally closes affected sessions within the two-second checking interval; it cannot be undone.

Admin sessions last at most eight hours and end on logout or server restart. Restarting the server does not erase device/client authorization. To change the password, update the deployment environment and recreate the relay container. With Compose, provide the original domain and chosen password again when recreating it.

Defaults are intended for small private deployments: 64 online game hosts, 32 clients/sessions per host, 256 total sessions, and 4 MiB/s per session/direction with an 8 MiB burst. Large screenshots share their session with chat. Request/rate limits can apply per instance behind a shared proxy.

### Backup, upgrade and troubleshooting

Retain the data volume and the existing image. Stop relay writes before copying SQLite; do not copy only a live main database while omitting its WAL. A sample Compose backup is:

```sh
mkdir -p backups
docker compose stop relay
docker compose cp relay:/data/relay.sqlite3 ./backups/relay.sqlite3
docker compose start relay
```

Keep configuration/volumes for upgrades and restore a matching pre-upgrade data snapshot for rollback when schemas change. Never remove the volume as part of routine upgrades.

| Symptom | Check |
| --- | --- |
| Wrong admin address | Configured HTTPS domain and forwarded original Host |
| Login failure / 429 | Password and login rate limit; wait a minute before retrying |
| Plugin offline / 409 | HTTPS reachability, registration credential and endpoint certificate |
| 401 | Lost, incorrect or revoked credential |
| Invitation rejected | Expired, used, replaced, incompatible or client limit reached |
| Connected but cannot send | Character login and trust confirmation on both ends |
| Fingerprint mismatch | Stop and independently compare with the plugin; do not bypass it |

The relay sees addresses, timing, device identifiers and traffic sizes. Application data uses an additional endpoint TLS session and the existing chat encryption; the relay does not hold endpoint private keys or persist application traffic.

## 简体中文

游戏插件和桌面端都主动连接你的 HTTPS 中继，适合无法直连游戏电脑的环境。中继只转发应用数据，不代理游戏、不保持角色在线、不保存离线聊天或截图。

### 部署

发布包中的预编译 Linux amd64 镜像见 [1Panel 离线部署](RELAY_VPS_OFFLINE_IMAGE.md#简体中文)，源码与反代配置见 [VPS HTTPS](RELAY_VPS_HTTPS.md#简体中文)。

没有现有反代的服务器可用 `deploy/relay/compose.yaml`，自带 Caddy。复制 `.env.example` 为 `.env`，填写真实 `RELAY_DOMAIN` 和至少 16 字符的独立 `RELAY_ADMIN_PASSWORD`，运行 `docker compose up -d --build`。Caddy 使用 80/443，无需公网开放 14777、8080；不要与 1Panel 配置合并。

打开 `https://你的域名/admin/` 登录。两端服务地址填写 HTTPS 根地址，不带子路径、查询或用户名密码；仅本机开发允许回环 HTTP。

### 注册与配对

1. 后台“游戏设备”添加设备，保存只显示一次的注册凭据。
2. 插件填写服务地址与凭据，保存并启用中继，等待在线。
3. 后台或插件生成邀请，新邀请会使上一份未使用邀请失效。
4. 客户端选择自建中继、粘贴邀请，与插件通过独立可信方式核对完整指纹并保存。
5. 在两端完成首次聊天设备信任。

管理密码、游戏注册凭据、邀请和客户端凭据各有用途，不能混用。每个客户端有独立凭据，由当前 Windows 用户保护；换电脑/用户或丢失配置可能需要重新配对。

**正常重启不需要重新邀请。** 十分钟只限制邀请的首次兑换，已保存凭据和信任持续有效，直到被撤销。保存失败、凭据丢失/撤销或端点证书更换等情况才需要重新配对。Logo 连接最近成功目标；成功中继会话意外中断后退避重试，主动断开可取消。

### 后台与运行

中英文后台可查看设备、客户端和会话，重命名、生成邀请、撤销单客户端或整个游戏设备，不能再次显示原始凭据。撤销通常在两秒检查周期内关闭受影响连接，且不可恢复。

后台会话最多八小时，退出或服务重启失效，但设备授权保留。改密码需更新环境并重新创建容器；Compose 重建时需再次提供域名和自行保存的密码。

默认面向小规模私有部署：64 个在线游戏端、每端 32 个客户端/会话、全实例 256 会话，每会话每方向 4 MiB/s、突发 8 MiB。截图与聊天共用会话；共享反代下部分请求限制按实例共享。

### 备份、升级与排查

保留数据卷和旧镜像，停止中继写入后再复制 SQLite；不要只复制正在写入的主库而漏掉 WAL。上方命令给出了 Compose 备份示例。升级保留配置与数据卷，数据库结构变化时回退需匹配升级前快照，常规升级不要删除卷。

地址错误检查 HTTPS 域名与原始 Host；登录失败检查密码和频率；设备离线检查 HTTPS、凭据与证书；401 表示凭据无效或撤销；邀请失败检查过期、使用、替换或数量限制；已连接不能发送检查角色和两端信任。指纹不符时停止并独立核对，不要绕过。

中继可见地址、时间、设备标识和流量大小。应用数据另经端点 TLS 与原聊天加密，中继不持有端点私钥、不持久保存应用流量。
