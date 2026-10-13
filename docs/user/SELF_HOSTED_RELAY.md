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

如果你在外面，客户端无法直接连接家里的游戏电脑，可以在自己的服务器上部署中继。客户端和游戏插件都连接它，再通过它传递消息。

中继需要自己部署，不是公共服务。游戏仍然要保持运行，角色仍然要在线。中继不能帮你登录游戏，也不会保存离线聊天或截图。

### 选择一种安装方式

**使用 1Panel：**按 [1Panel 离线部署教程](RELAY_VPS_OFFLINE_IMAGE.md#简体中文) 导入发布包里的 Linux amd64 镜像，并设置域名和 HTTPS。

**普通 Docker 服务器：**如果没有现成的反向代理，可以使用项目里的 `deploy/relay/compose.yaml`，它包含 Caddy。把 `.env.example` 复制为 `.env`，填写你的 `RELAY_DOMAIN` 和至少 16 个字符的独立管理密码 `RELAY_ADMIN_PASSWORD`，再运行 `docker compose up -d --build`。具体步骤见 [HTTPS 部署教程](RELAY_VPS_HTTPS.md#简体中文)。

Caddy 需要使用服务器的 80、443 端口，不需要把 14777、8080 开放到公网。已有 1Panel 反向代理时，请使用对应教程，不要再叠加这一套 Caddy 配置。

部署完成后，打开 `https://你的域名/admin/`，用管理密码登录。客户端和插件里填写的服务地址是 `https://你的域名`，不要带 `/admin/`、其他路径或用户名密码。只有在本机开发时才能使用回环地址的 HTTP。

### 把游戏电脑和客户端连上

1. 在后台的“游戏设备”里添加设备，保存页面显示的注册凭据。它只显示一次。
2. 在游戏插件里填写中继地址和注册凭据，保存并启用中继，等后台显示设备在线。
3. 在后台或插件中生成邀请。生成新邀请后，上一份尚未使用的邀请会失效。
4. 在客户端选择“自建中继”，粘贴邀请。直接对照游戏插件显示的完整指纹，确认一致后保存；不在游戏电脑旁边时，通过你信任的渠道核对。
5. 按客户端和插件的提示，完成第一次聊天连接的信任确认。

管理密码用来登录后台；注册凭据交给游戏插件；邀请交给新客户端。这几种内容不能互相替代。每个客户端有自己的凭据，只能由保存它的 Windows 用户使用；换电脑、换用户或丢失配置后需要重新配对。

**正常重启不需要重新邀请。** 邀请的十分钟有效期只限制第一次使用。配对信息保存成功后，邀请过期不会让已有连接失效。清空配置、撤销授权或更换设备身份后，才可能需要重新配对。

点击客户端左上角 Logo 可以连接上次成功使用的地址。中继连接意外断开后会自动重试；主动断开后停止重试。

### 后台能做什么

后台支持中英文，可以查看在线设备和连接、重命名设备、生成邀请，也可以撤销某个客户端或整个游戏设备的授权。撤销通常在两秒内生效，被撤销的凭据不能恢复。之前的原始凭据不会再次显示。

后台登录最多保持八小时，退出登录或重启服务后需要重新登录，但设备配对不会因此丢失。修改管理密码后，需要用更新后的环境变量重新创建容器；使用 Compose 时，重建时要再次提供域名和你保存的密码。

默认配置适合个人或小规模使用：最多 64 个在线游戏设备，每个设备最多 32 个客户端会话，整个服务最多 256 个会话。每个会话每个方向限速 4 MiB/s，允许 8 MiB 突发流量。截图和聊天共用连接，部分请求还受整个服务的频率限制。

### 备份和升级

升级前保存配置、数据卷和旧镜像。备份数据库时，先停止中继再复制数据；不要只复制运行中的 SQLite 主文件，否则可能漏掉还在 WAL 文件里的内容。使用 Compose 时可以这样备份：

```sh
mkdir -p backups
docker compose stop relay
docker compose cp relay:/data/relay.sqlite3 ./backups/relay.sqlite3
docker compose start relay
```

正常升级应保留数据卷，不要删除它。数据库结构升级后，如果要回退旧版中继，也要恢复升级前的数据备份。

### 连不上时先查什么

- **后台地址打不开：**检查 HTTPS 域名配置，以及反向代理是否转发了原始 Host。
- **后台登录失败或提示 429：**检查管理密码，连续失败后等一分钟再试。
- **游戏设备离线或提示 409：**检查插件是否启用中继，服务地址、注册凭据和 HTTPS 证书是否正确。
- **提示 401：**凭据可能无效或已被撤销，需要检查授权。
- **邀请不能用：**检查是否过期、已使用、生成了新的邀请，或客户端数量已达上限。
- **连接成功但发不出消息：**检查角色是否在线，两端是否都完成了信任确认。
- **指纹不一致：**先停止连接，重新核对游戏插件的指纹，不要跳过检查。

中继能看到连接地址、时间、设备标识和流量大小。聊天等应用数据在两端之间另有 TLS 和原有聊天加密，中继不持有设备私钥，也不把这些流量保存为聊天记录。
