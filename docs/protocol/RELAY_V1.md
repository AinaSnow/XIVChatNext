# Relay v1

## English

The control protocol uses bounded JSON in binary WebSocket frames. Application traffic retains the MessagePack/XIVChat protocol. The service references the minimal control contracts; Windows endpoints use the separate transport library.

### Authentication and routes

| Endpoint | Identity / purpose |
| --- | --- |
| `GET /healthz` | Anonymous liveness check returning status and protocol version |
| `WS /v1/host` | Game registration credential; submits protocol version and endpoint certificate fingerprint, then waits for `registered` |
| `POST /v1/invitations` | Online game credential; issues a ten-minute, single-use invitation |
| `POST /v1/pair` | Redeems an invitation for a separate client credential |
| `WS /v1/client` | Client credential; its authentication record determines the target game device |
| `WS /v1/host-data/{id}` | Game credential plus single-use `X-Relay-Ticket`; joins a separate data session |

Endpoint credentials use Authorization Bearer headers; invitations use request bodies. Service addresses reject embedded credentials, queries and fragments. Devices are registered through password-protected web administration or the local CLI.

Web administration uses `/admin/` and `/admin/api/`. Its sessions are separate from endpoint credentials. The deployment password is checked using a randomly salted PBKDF2-SHA256 verifier (100,000 iterations), held only in memory. Session and CSRF tokens have 256 random bits. Only session-token hashes are retained, for at most eight hours; logout or restart invalidates sessions. Cookies use HttpOnly, SameSite=Strict and Path=/admin, plus Secure for HTTPS. Mutations require the configured Origin, JSON and the session CSRF header; all administration API requests validate Host. Login is limited to five attempts per instance per minute and 64 administration sessions. Responses disable caching; CSP blocks third-party scripts and frames.

The dashboard shows devices, clients, connection status and session ownership, without credential hashes, routing tickets or chat content. New device credentials appear only in the creation response. Dashboard and plugin invitations share the same storage and `xivchat-relay:` encoding. Clients must still independently verify the plugin fingerprint.

Each client connection receives a random session ID and a 256-bit ticket. Data sessions bind the game identity, control connection instance and client identity. A different game cannot join with a leaked ticket. Each client credential allows one active session. Endpoints start their TLS handshake after receiving `ready`; stale session completion cannot replace a new session.

Invitations, registration credentials and client credentials use 256-bit random values; SQLite stores SHA-256 hashes. Immediate transactions prevent duplicate invitation redemption and enforce version, expiry, revocation and capacity checks. Each game device keeps one valid invitation.

### Encryption

Production transport uses HTTPS/WSS with normal server-certificate verification. Inside the transparent stream, the Windows plugin and client establish SslStream TLS 1.2/1.3, pinning the game endpoint certificate's SHA-256 fingerprint and checking its validity. The endpoint creates an ECDSA P-256 self-signed certificate locally; its private key never goes to the relay.

Platform TLS provides freshness, authenticated encryption and replay protection. Existing Sodium key exchange and device trust still run inside TLS. Relay registration or routing permission does not replace endpoint identity verification. Compare the complete pairing fingerprint with the plugin through an independent trusted channel.

Windows stores credentials and the endpoint certificate using current-user DPAPI. The relay observes connection metadata and traffic volume but cannot obtain chat plaintext from these sessions. It does not persist application traffic or queue offline messages.

### Resource limits and shutdown

Each game has one control connection; each client has its own pair of data WebSockets. Forwarding awaits writes without an unbounded background queue. Control frames are limited to 16 KiB and data frames to 64 KiB, with fragmentation and assembly-time limits. Endpoints split large writes into immediately sent 16 KiB frames.

Control setup, routing readiness, TLS handshakes, HTTP response bodies, partial frames and writes have time limits. WebSocket heartbeats detect loss. The plugin reconnects with exponential backoff and jitter; clients retry after an established session ends unexpectedly. Explicit disconnect cancels the connection and pending retries.

The service checks active credential revocation every two seconds. Shutdown, request cancellation and control disconnection propagate to associated streams. Revoked clients and malformed data close only the affected session.

### Compatibility

Relay v1 is separate from the retired public service and its authorization-code/shared-queue protocol. Direct connections remain available, with application capability negotiation. Older clients or plugins without relay support cannot use this relay; direct-mode compatibility does not imply relay compatibility.

## 简体中文


控制协议为有界 JSON，WebSocket 控制帧使用 Binary 类型。应用层继续使用原 MessagePack/XIVChat 协议。服务仅引用最小控制契约；Windows 端点引用独立 Transport 库。

## 认证与路由

| 入口 | 身份/用途 |
| --- | --- |
| `GET /healthz` | 匿名存活检查，仅返回状态及协议版本 |
| `WS /v1/host` | 游戏注册凭据；发送版本与端点证书指纹，等待 `registered` |
| `POST /v1/invitations` | 在线游戏注册凭据；生成十分钟一次性邀请 |
| `POST /v1/pair` | 兑换邀请，生成独立客户端凭据 |
| `WS /v1/client` | 客户端凭据；服务从认证记录确定目标游戏设备 |
| `WS /v1/host-data/{id}` | 游戏注册凭据及 `X-Relay-Ticket` 单次票据；接入独立数据会话 |

端点凭据放在 Authorization Bearer 请求头，邀请放在请求体。地址不允许附带用户名密码、查询或片段。设备注册通过受密码保护的网页后台或本地 CLI；不存在匿名设备列表或将管理密码下放给客户端的流程。

网页管理入口为 `/admin/`，管理接口位于 `/admin/api/`。管理会话与端点 Bearer 凭据完全分开。管理密码通过部署环境设置，内存验证使用随机盐 PBKDF2-SHA256（100,000 次），不持久保存管理密码摘要。会话令牌和 CSRF 令牌均为 256 位随机值；服务器只在内存保存会话令牌摘要，最长 8 小时，退出或重启立即失效。Cookie 设置 HttpOnly、SameSite=Strict、Path=/admin；配置为 HTTPS 时设置 Secure。状态变更要求匹配配置的 Origin、JSON 请求体和会话 CSRF 头，所有管理 API 同时核对 Host。登录每实例每分钟最多 5 次，最多 64 个管理会话。页面与管理响应不缓存，页面 CSP 禁止第三方脚本及嵌入框架。相关设计参考 [ASP.NET Core CSRF 文档](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)。

管理后台可查看设备、客户端、在线状态和会话归属，不返回凭据摘要、路由票据或聊天内容。创建设备时只在当次响应返回原始凭据。后台生成邀请与插件入口共用存储逻辑，使用同一种 `xivchat-relay:` 编码，客户端仍须独立核对插件端点指纹。

每次客户端接入创建随机会话 ID 和 256 位随机票据。数据会话绑定游戏身份、控制连接实例和客户端身份；其他设备即使知道票据也不能接入。单个客户端凭据同时只允许一个活动会话。游戏数据端与客户端收到 `ready` 后才开始交换端点 TLS 握手；旧会话的取消/迟到结果不能接管新会话。

邀请、注册及客户端凭据使用 256 位随机值；SQLite 只保存 SHA-256 摘要。邀请兑换使用立即事务，防止并发重复兑换，并校验版本、有效期、撤销和数量限制。每台设备只保留一个有效邀请。

## 加密

生产传输使用外层 HTTPS/WSS，验证服务端的正常 TLS 证书。在透明数据流内部，再由 Windows 插件与客户端直接建立 `SslStream` TLS 1.2/1.3，固定核对插件端点证书的 SHA-256 指纹及有效期。端点证书为本地生成的 ECDSA P-256 自签证书，其私钥不会发送给中继。

会话新鲜性、密文认证及重复/旧帧防护由平台 TLS 实现承担，不在原聊天帧上自制密码协议。内层 TLS 建立后仍执行现有 Sodium 密钥交换和设备信任。只持有中继注册/路由权限不能替代游戏端点身份确认。配对时的完整指纹须与插件通过独立可信方式核对。

Windows 端点凭据和插件证书通过当前用户 DPAPI 保存。服务能观察连接元数据和流量，不能从上述会话取得聊天明文。服务不持久保存应用流量，也没有离线消息队列。

## 有界资源与关闭

每个游戏端一条控制连接，每个客户端单独一对数据 WebSocket；转发直接等待写入，不建立无界后台队列。控制帧上限 16 KiB、数据帧上限 64 KiB，限制分片数与组装时间；端点把大写入切成 16 KiB 帧且立即发送，避免握手等候 Flush。

控制握手、路由就绪、TLS 握手、HTTP 响应体、部分帧和写入均有限时。WebSocket 心跳检测失联；插件端指数退避并增加随机抖动，客户端在成功会话异常结束后退避重连。显式断开会取消已有连接及待执行重试。

服务每两秒检查活动凭据撤销状态；退出、请求中止和控制连接断开会传播到相关数据流。被撤销客户端与异常数据帧只关闭对应会话，其他设备继续运行。

## 兼容

这是新的中继 v1，不连接旧公共服务，也不兼容其旧认证码/共享队列协议。直连路径继续保留，应用协议能力协商仍适用。未安装中继支持的旧客户端或旧插件不能加入新中继；不能把直连的旧版本兼容结论当成中继兼容结论。