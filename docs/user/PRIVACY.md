# Privacy and security / 隐私与安全

## English

XIVChat Next has no account, no analytics and no cloud service run by the project. Your chats travel between the game plugin and your own desktop, directly or through a relay you host yourself.

### What stays on your computer

- **Desktop client:** settings, saved connections and trusted devices in `%APPDATA%\XIVChat for Windows`. From Desktop 1.5.0 the device private key is encrypted for your Windows account. History, favorites, notes, nicknames, window layouts and cached images are in `%LOCALAPPDATA%\XIVChatDesktop`. None of this is uploaded.
- **Game plugin:** settings, trusted devices and its key are stored with your other Dalamud plugin settings; relay credentials are protected for your Windows account. Recent messages are kept in memory so a reconnecting client can catch up; the amount is set in the plugin's Messages tab and is cleared when the plugin stops.

These folders contain chat history and connection secrets. Do not share them publicly.

### Connections

- **First connection:** the client and the plugin each show a fingerprint. Compare them and approve on both ends. Only approve devices you recognise; remove old ones in the plugin's Devices tab.
- **Encryption:** chat traffic between the client and the plugin is encrypted, whether you connect directly or through a relay.
- **Direct connections:** from plugin 1.7.17, new plugin configurations accept connections from this computer only. Turn on **Allow LAN connections** only if another computer on your network needs to connect. The plugin limits repeated connection attempts and trust prompts from unknown devices.
- **Relay:** a relay you host sees connection addresses, timing, device identifiers and traffic sizes. It does not hold device private keys and does not store chats. See [Self-hosted relay](SELF_HOSTED_RELAY.md).

### What the client fetches from the internet

| Feature | Service | What is sent | Control |
| --- | --- | --- | --- |
| Update checks | GitHub (`api.github.com`) | An ordinary request for the public release list. No chat or character information. | Settings → Updates → turn off the startup check |
| Online avatars | Lodestone (`na.finalfantasyxiv.com`) | Names and home worlds of characters shown in the client, to find their portraits | Settings → History → **Allow online Lodestone avatar lookups** |
| Item and map cards | `xivapi.com`, `cafemaker.wakingsands.com`, `xivapi-v2.xivcdn.com` | Item, icon and map identifiers | Images are cached locally after the first download |
| Map links | `map.wakingsands.com` | Map identifier and coordinates, when you open a map card's web view | Only when you open it |

Like any web request, these services also see your IP address.

### Screenshots and streamer mode

The plugin captures the game window when a trusted client asks for a screenshot. From plugin 1.8.0 you can turn this off with **Allow clients to request game screenshots** in the plugin's Messages tab; earlier versions have no switch, so only trust devices you control. **Streamer mode changes what the client displays, copies and exports.** It does not redact game screenshots, images, unknown nicknames or text you type, and local history keeps the original messages. Check the actual screen before sharing.

### Reporting a security problem

Please do not post details publicly. [Open an issue](https://github.com/AinaSnow/XIVChatNext/issues/new) that only says you have a security report, and a private channel will be arranged.

## 简体中文

XIVChat Next 没有账号，不做统计，也没有项目方运营的云服务。聊天只在游戏插件和你自己的电脑之间传递，可以直连，也可以经过你自己部署的中继。

### 哪些内容保存在本机

- **桌面客户端：**设置、保存的连接和已信任设备在 `%APPDATA%\XIVChat for Windows`。客户端 1.5.0 起，本设备私钥按你的 Windows 账户加密保存。聊天记录、收藏、备注、备注名、窗口布局和图片缓存在 `%LOCALAPPDATA%\XIVChatDesktop`。这些内容都不会上传。
- **游戏插件：**设置、已信任设备和插件密钥与其他 Dalamud 插件设置保存在一起，中继凭据按你的 Windows 账户加密。插件会在内存中保留最近的消息，方便客户端重连后补齐；保留多少可以在插件的“消息”页设置，插件停止后清空。

这些目录含有聊天记录和连接凭据，请不要公开上传。

### 连接安全

- **第一次连接：**客户端和插件都会显示设备指纹。核对一致后，在两边都确认允许。只允许你认识的设备；不再使用的设备可以在插件“设备”页删除。
- **加密：**无论直连还是经过中继，客户端和插件之间的聊天数据都是加密传输的。
- **直连：**插件 1.7.17 起，新配置只允许本机直接连接。只有需要从局域网里另一台电脑连接时，才开启“允许局域网设备连接”。插件会限制陌生设备反复连接和弹出确认窗口。
- **中继：**你自己部署的中继能看到连接地址、时间、设备标识和流量大小，但不持有设备私钥，也不保存聊天记录。详见 [自建中继](SELF_HOSTED_RELAY.md#简体中文)。

### 客户端会访问哪些网站

| 功能 | 服务 | 发送的内容 | 怎么控制 |
| --- | --- | --- | --- |
| 检查更新 | GitHub（`api.github.com`） | 普通的公开版本列表请求，不含聊天或角色信息 | 设置 → 更新 → 关闭启动时检查 |
| 联网头像 | Lodestone（`na.finalfantasyxiv.com`） | 客户端里显示的角色名和所属服务器，用来查找头像 | 设置 → 历史 → “允许联网查询 Lodestone 头像” |
| 物品和地图卡片 | `xivapi.com`、`cafemaker.wakingsands.com`、`xivapi-v2.xivcdn.com` | 物品、图标和地图编号 | 第一次下载后图片会缓存在本机 |
| 地图链接 | `map.wakingsands.com` | 打开地图卡片网页时的地图编号和坐标 | 只在你打开时访问 |

和所有网络请求一样，这些服务也能看到你的 IP 地址。

### 截图和主播模式

已信任的客户端请求截图时，插件会截取游戏画面。插件 1.8.0 起，可以在插件“消息”页关闭“允许客户端请求游戏截图”；更早的版本没有这个开关，所以只信任你自己控制的设备。**主播模式只改变客户端里显示、复制和导出的内容**，不会给游戏截图、图片、未知昵称或你正在输入的文字打码，本地记录也保留原始消息。分享前请检查实际画面。

### 报告安全问题

请不要公开发布细节。可以 [新建一个 issue](https://github.com/AinaSnow/XIVChatNext/issues/new)，只说明你有安全问题要报告，之后会安排私下沟通的方式。
