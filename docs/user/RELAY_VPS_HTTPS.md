# Ubuntu + 1Panel 部署 XIVChat 中继

适用你的环境：Ubuntu，1Panel 已经通过 OpenResty 使用 80/443 做反向代理。继续由 1Panel 管理域名和 HTTPS 证书，中继只监听本机 `127.0.0.1:18080`。

```text
浏览器 / 游戏插件 / 桌面端
          ↓ HTTPS / WSS :443
1Panel OpenResty（域名与证书）
          ↓ HTTP / WebSocket，保留原始 Host
127.0.0.1:18080 → XIVChat 中继容器 :8080
```

部署包：`xivchat-relay-vps-20260914.tar.gz`，包含中继与协议库源码、网页后台和部署配置，不含密码、测试数据或客户端。中继代码基于提交 `82dda03`；本次另外提供 `compose.1panel.yaml`，现场构建镜像，不必在 VPS 安装 .NET。

**全文使用 `docker compose -f compose.1panel.yaml`。** 这个文件是独立配置；不要和默认 `compose.yaml` 合并，也不要省略 `-f`，默认配置会启动另一套 Caddy 并占用 80/443。

## 1. 域名与准备

在 DNS 管理中，将一个独立子域名（例如 `relay.example.com`）的 A 记录指向 VPS 公网 IPv4。没有可用 IPv6 时，不要为它添加 AAAA 记录。首次联调先直接解析到 VPS。

本文中的 `relay.example.com` 均为示例，必须替换为你的真实域名。不使用 `example.com/relay` 这样的子路径部署。

保留现有 80/443 和 SSH 的防火墙设置；无需开放 18080、8080 或游戏端口 14777。中继不会接管你已有网站。

在 1Panel 的终端或 SSH 中使用 root 会话执行后续服务器命令：

```sh
sudo -i
docker version
docker compose version
ss -ltnp '( sport = :18080 )'
```

18080 应空闲。如果已占用，将 `compose.1panel.yaml` 中的 **18080** 换成另一个空闲端口，并同步修改后文反代地址；保留 `127.0.0.1` 绑定和容器端口 8080。1Panel 已有 Docker 时不必重装。

在 1Panel 容器列表找到 OpenResty，查看其网络模式。本文的回环地址反代适用于 **host 模式**。也可用下面命令核对，先用第一条找到真实容器名称：

```sh
docker ps --format '{{.Names}}\t{{.Image}}'
docker inspect --format '{{.HostConfig.NetworkMode}}' 你的OpenResty容器名
```

输出应为 `host`。如果你自定义成 bridge 模式，容器内的 `127.0.0.1` 不指向宿主机，需要另配共享 Docker 网络；不要直接改成公网监听来解决，也不要为本服务随意改变承载现有网站的 OpenResty 网络模式。

## 2. 上传与设置

通过 1Panel 文件管理或 WinSCP，将部署包上传到 VPS 的 `/tmp/`。也可在自己的 Windows PowerShell 中执行，替换账户和地址；非默认 SSH 端口可加 `-P 端口`：

```powershell
scp "E:\SapphireServer\Dalamud Dev\XFW-next-workbench\artifacts\xivchat-relay-vps-20260914.tar.gz" 你的SSH账户@VPS公网IP:/tmp/
```

回到 VPS 的 root 终端。以下用于首次安装，目标目录应为新的独立目录：

```sh
install -d -m 700 /opt/xivchat-relay
tar -xzf /tmp/xivchat-relay-vps-20260914.tar.gz -C /opt/xivchat-relay
cd /opt/xivchat-relay/deploy/relay
umask 077
cp .env.example .env
chmod 600 .env
```

用 1Panel 文件编辑器打开 `/opt/xivchat-relay/deploy/relay/.env`，填写两项：

```dotenv
RELAY_DOMAIN=relay.你的域名.com
RELAY_ADMIN_PASSWORD='填写你自己生成的独立长密码'
```

域名不带 `https://`、端口或路径。管理密码至少 16 字符；可用密码管理器生成 32 位以上的随机字母数字密码。上面的中文不是默认密码。也可执行 `openssl rand -hex 24`，将输出保存到自己的密码管理器，再填入 `.env`。不要把 `.env` 当作 shell 脚本执行。

## 3. 启动中继

在 VPS 终端执行：

```sh
cd /opt/xivchat-relay/deploy/relay
docker compose -f compose.1panel.yaml config --quiet
docker compose -f compose.1panel.yaml up -d --build
docker compose -f compose.1panel.yaml ps
curl -fsS http://127.0.0.1:18080/healthz
```

首次会下载 .NET 镜像与依赖并编译；VPS 需要能访问 Microsoft 容器镜像源与 NuGet。中继应进入 `healthy`，健康请求应返回包含 `"status":"ok"` 的 JSON。

此时直接通过 HTTP 登录后台不会正常工作，因为管理地址配置为外部 HTTPS；接下来先完成 1Panel 反代和证书。

如启动失败：

```sh
docker compose -f compose.1panel.yaml logs --tail=100 relay
```

## 4. 1Panel 创建反向代理网站

进入 **网站 → 创建网站 → 反向代理**，只为新的中继子域名建站。不同 1Panel 版本的字段名称可能略有不同。[1Panel 创建网站说明](https://1panel.cn/docs/v2/user_manual/websites/website_create/)

| 项目 | 填写内容 |
| --- | --- |
| 主域名 | 你的真实中继域名，例如 `relay.example.com` |
| 代理地址 / 目标 URL | `http://127.0.0.1:18080` |
| 代理路径 | `/`，转发整个站点 |
| 发送域名 / Host | `$http_host`，或明确填写你的真实中继域名 |
| WebSocket | 开启 |
| 缓存 | 关闭 |

**Host 不能使用 `127.0.0.1` 或 `$proxy_host`。** 后台会核对请求域名。不要只代理 `/admin/`：游戏和客户端还要访问 `/v1/`，健康检查使用 `/healthz`。

如果界面没有 WebSocket 开关，或想核对生成的配置，可在该网站的反向代理配置中检查下面内容。它是现有 `location /` 的参考；修改对应配置，不要额外粘贴第二个重复的 `location /`，不要覆盖整个站点的证书配置：

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

WebSocket 代理需要将 Upgrade 与 Connection 请求头传给上游；较长的读写超时用于持续连接。[Nginx 官方 WebSocket 说明](https://nginx.org/en/docs/http/websocket.html)

保存时确保 1Panel 配置检查通过。不要对整个中继站点额外开启“密码访问”、验证码或浏览器挑战，它们会拦住插件和桌面端请求；后台已有独立管理密码。

## 5. 用 1Panel 配置 HTTPS

1. 在 1Panel 的证书模块申请覆盖该中继域名的证书；已有覆盖该域名的有效证书可以复用。
2. 若走 DNS 验证，按面板配置 DNS 账户/验证记录；若走 HTTP 验证，确认域名已指向本机且公网 80 可访问。
3. 打开中继网站配置的 **HTTPS**，选择该证书，启用 HTTPS，并选择 HTTP 自动跳转 HTTPS。
4. 在证书模块确认自动续签配置和网站证书关联正常。无需另外安装 Certbot 或启动包里的 Caddy。

对应入口见 [1Panel HTTPS 配置](https://1panel.cn/docs/v2/user_manual/websites/website_config_basic/#9-https)。

## 6. 公网验收与接入游戏

在你的电脑浏览器打开：

```text
https://你的真实中继域名/healthz
https://你的真实中继域名/admin/
```

第一页应显示健康 JSON；第二页应显示登录页且浏览器证书正常。使用 `.env` 的管理密码登录。必须使用同一个域名，不能用 VPS IP 代替。

也可在 Windows PowerShell 验证，替换示例域名：

```powershell
curl.exe -fsS https://relay.example.com/healthz
curl.exe -I https://relay.example.com/admin/
```

不要用 `-k` 绕过证书检查。再用手机移动网络打开一次后台，确认公网可达。

随后按顺序操作：

1. 后台添加游戏设备，复制服务地址及一次性显示的注册凭据。
2. 游戏插件中继设置填写 `https://你的中继域名` 和注册凭据，保存并启用；这里不填后台密码。
3. 设备在线后，在后台生成邀请，粘贴至桌面端的“自建中继”连接。
4. 与游戏插件核对完整端点指纹，确认配对与首次设备信任。
5. 实际发送消息、测试截图，并保持连接一段时间，确认反代支持持续 WebSocket。

邀请有效 10 分钟且只能兑换一次。之后设备、邀请、在线状态和撤销授权都在网页管理。浏览器验证 VPS 域名证书，配对验证游戏端指纹，两者用途不同。更多日常操作见 [使用指南](SELF_HOSTED_RELAY.md)。

## 7. 常见问题

| 现象 | 检查 |
| --- | --- |
| 中继构建下载失败 | VPS 到 Microsoft 镜像源、NuGet 的网络与构建日志 |
| 502 | 先测本机 `/healthz`；本机正常再查 OpenResty 是否 host 模式、上游地址与端口是否一致 |
| 后台提示地址不正确 / 403 | 检查 `.env` 域名、浏览器域名与反代 Host；Host 不应变成上游 IP |
| HTTPS 证书警告 | 检查证书是否覆盖该域名、是否过期及 1Panel 是否绑定正确证书 |
| 后台能打开，游戏却无法上线 | 代理应覆盖 `/v1/`，开启 WebSocket，检查插件凭据和额外站点认证/WAF 拦截 |
| 连接一会就断 | 检查 WebSocket Upgrade/Connection 与代理超时；查看中继、反代日志 |
| 登录 429 | 登录每分钟最多 5 次，等待一分钟重试；另有实例 HTTP 请求限制 |
| 误启动 Caddy，提示端口占用 | 核对是否漏写 `-f compose.1panel.yaml`，不要停掉现有 OpenResty |

## 8. 维护与备份

后续命令仍在 `/opt/xivchat-relay/deploy/relay` 执行。改后台密码时编辑 `.env` 后执行：

```sh
docker compose -f compose.1panel.yaml up -d --force-recreate relay
```

后台会话失效，设备授权保留；容器重建时连接暂时中断。中继配置自动重启，HTTPS 证书由现有 1Panel 维护。

中继授权数据存于 `relay-data` 命名卷；保留它和 `.env`，并通过 1Panel 备份站点与证书配置。不要执行 `down -v` 删除卷。数据库备份可在空闲时执行，会短暂停止中继：

```sh
umask 077
backup_dir="backups/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$backup_dir"
docker compose -f compose.1panel.yaml stop relay
docker compose -f compose.1panel.yaml cp relay:/data/relay.sqlite3 "$backup_dir/relay.sqlite3"
docker compose -f compose.1panel.yaml start relay
```

确认备份文件存在后，将其保存至 VPS 外的安全位置。升级时先备份数据并保留旧镜像，更新源码、保留 `.env`，继续使用同一 Compose 项目与卷，再执行带 `-f compose.1panel.yaml` 的构建启动命令。

## 验证边界

中继源码已通过 Windows/Linux 后台各 47 项测试、原协议各 26/25 项回归及 Linux 容器实际启动验证。1Panel 专用配置只启动中继、绑定回环端口并沿用这些运行约束。尚未连接你的 VPS 执行部署，也未在你的 1Panel 上实测；请按第 6 节完成公网证书和真实游戏验收。
