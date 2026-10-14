# Chat encryption v2 / 聊天加密 v2

## English

Protocol 2 replaces the original XIVChat key exchange between the desktop client and the game plugin, directly or inside a relay data session. Plugin 1.8.0 and Desktop 1.6.0 implement it in `XIVChatCommon/KeyExchangeV2.cs` and `XIVChatCommon/SecureChannel.cs`. Application packets (MessagePack operations) are unchanged.

### Why

Protocol 1 derives session keys only from the two long-term keys, so every connection between the same devices uses the same keys, and packets carry random nonces with no sequence. Someone who records encrypted client packets can open a new connection presenting the client's public key and replay them; the plugin decrypts and executes them. Protocol 1 also has no forward secrecy.

### Handshake

Each device has a long-term X25519 key pair (client `c`, plugin `s`). Each connection adds fresh ephemeral key pairs (`ce`, `se`).

1. Client → plugin: magic `0E 14 44`, `ce.pub` (32 bytes), `c.pub` (32 bytes). Protocol 1 uses magic `0E 14 43`.
2. Plugin → client: `se.pub`, `s.pub`, server tag (32 bytes).
3. Client → plugin: client tag (32 bytes).

Both sides compute three X25519 results and reject an all-zero (low-order) result:

| Name | Client computes | Plugin computes | Purpose |
| --- | --- | --- | --- |
| `ee` | `ce × se` | `se × ce` | Fresh secret for this connection |
| `es` | `ce × s` | `s × ce` | Only the holder of `s` can compute it |
| `se` | `c × se` | `se × c` | Only the holder of `c` can compute it |

With BLAKE2b (`H(message, key, length)`):

- `T = H("XIVChat Next protocol 2" ‖ ce.pub ‖ c.pub ‖ se.pub ‖ s.pub, —, 32)`
- `K(label) = H(label ‖ ee ‖ es ‖ se, key = T, 32)` for `client to server`, `server to client`, `client confirm`, `server confirm`
- server tag `= H(T, key = K(server confirm), 32)`, client tag `= H(T, key = K(client confirm), 32)`

The client checks the server tag before sending its own; the plugin checks the client tag in constant time. Trust is then decided by the long-term public key, as before: the fingerprint shown on both ends is unchanged.

### Packets

Each direction has its own key. A frame is a 4-byte little-endian ciphertext length (16–128,000) followed by XSalsa20-Poly1305 ciphertext. The 24-byte nonce is never sent: it is the frame's counter in that direction (64-bit little-endian, starting at 0, zero-padded). A replayed, reordered, dropped or modified frame fails authentication and closes the connection.

### Properties

- Recorded packets cannot be replayed into another connection, because `ee` differs every time.
- Past sessions stay private if a long-term key later leaks, because the ephemeral private keys are discarded.
- Knowing a device's public key is not enough to impersonate it: `es` and `se` require the matching private key.
- Not provided: hiding which public keys connect (both are sent in the clear, as in protocol 1) and protection against a compromised endpoint.

### Compatibility and downgrade protection

- A protocol 1 plugin closes the connection when it reads the new magic. The client then reconnects with protocol 1 and warns that the plugin should be updated.
- The client records the highest protocol completed with each trusted plugin. After protocol 2 has succeeded, it refuses a protocol 1 fallback to that plugin and reports a possible interception or downgrade.
- The plugin records each client key that completed protocol 2 and refuses protocol 1 from that key, which also blocks protocol 1 replays that impersonate an updated client. Removing trust for a device clears this record.
- **Allow clients using old encryption (v1)** (on by default) controls whether the plugin accepts protocol 1 at all. Turn it off once every client is on Desktop 1.6.0 or later.

`tests/ProtocolRegression` covers both directions, cross-connection and in-connection replay, reordering, tampering, oversized frames, client and plugin impersonation, low-order keys and detection of a protocol 1 plugin.

## 简体中文

加密 v2 替换桌面客户端与游戏插件之间原有的 XIVChat 密钥交换，直连和中继数据会话都使用它。插件 1.8.0 和客户端 1.6.0 起支持，代码在 `XIVChatCommon/KeyExchangeV2.cs` 与 `XIVChatCommon/SecureChannel.cs`，应用层消息格式不变。

**为什么要换：**v1 的会话密钥只由双方长期密钥决定，同一对设备每次连接的密钥都相同，消息使用随机 nonce、没有序号。录下加密数据包的人可以用客户端的公钥发起新连接并重放这些数据包，插件会照常解密并执行。v1 也没有前向保密。

**握手：**每次连接双方各生成一次性密钥，客户端发送魔数 `0E 14 44`、临时公钥和长期公钥；插件回复临时公钥、长期公钥和确认码；客户端再回复确认码。三次 X25519 结果分别保证“本次连接独有”“只有真正的插件能算出”“只有真正的客户端能算出”，全零结果会被拒绝。会话密钥和确认码用 BLAKE2b 从三个结果和全部公钥派生，具体见上方英文表格。信任仍然按长期公钥判断，两端显示的指纹不变。

**数据包：**每个方向使用独立密钥。帧格式为 4 字节小端长度加 XSalsa20-Poly1305 密文，nonce 不随包发送，而是该方向的递增计数器。重放、乱序、丢失或被修改的数据包都会验证失败并断开连接。

**效果：**录下的数据包不能用于其他连接；长期密钥以后泄露，过去的会话仍然安全；只知道公钥无法冒充设备。不提供隐藏连接双方公钥的能力，也不能防护已被入侵的设备。

**兼容与防降级：**

- 旧插件读到新魔数会断开，客户端随后改用 v1 重连，并提示更新插件。
- 客户端会记住每个已信任插件用过的最高版本。用过 v2 之后，再遇到只接受 v1 的情况会停止连接，并提示可能被拦截或降级。
- 插件会记住用过 v2 的客户端，之后拒绝它以 v1 连接，这也能挡住冒充已更新客户端的 v1 重放。取消信任时会一并清除记录。
- 插件“连接”页的“允许旧版加密 (v1) 的客户端”默认开启。所有客户端都更新到 1.6.0 以上后建议关闭。

`tests/ProtocolRegression` 覆盖双向收发、跨连接和连接内重放、乱序、篡改、超长帧、冒充客户端或插件、低阶公钥以及识别旧插件。
