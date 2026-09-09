# 好友列表旧实现审查与更新后的参考源码

用户说明旧好友读取曾有问题并已移除。本次以此为前提，检查残留代码和当前版本可用接口，不把旧实现视为可直接恢复的功能。

## 源码同步与运行版本

两份独立参考仓库同步前均无本地修改，通过 fetch 和 fast-forward 更新；Dalamud 的 FFCS 子模块随后切换到父仓库固定的提交。

| 对象 | 更新后／运行版本 |
| --- | --- |
| 本地 FFXIVClientStructs main | `5af8ee8e7a184864fe308999c626243f649daa06` |
| 本地 Dalamud master | `86acaf54fd5b89f5351a28f622eb8c10df2a5ef9` |
| Dalamud master 固定的 FFCS 子模块 | `6fa5ad1b8f5ffa9ff6a8aac2dbf83cec56ce8a88` |
| 当前安装的 Dalamud.dll | `15.0.3.3+c4c825465ab432d6d80539df8179bd7cdaf6ff00` |
| 当前安装的 FFXIVClientStructs.dll | FileVersion `7.55.1.9032`；ProductVersion `1.0.0+21898bf815f0e56e02b7dc08f0a3e24822c759d0` |

安装版本来自 DLL 的文件版本资源，与 Dalamud `15.0.3.3` 标签固定的 FFCS 提交一致。从该 FFCS 提交到最新 main，`InfoProxyFriendList`、`InfoProxyCommonList`、`InfoProxyInterface`、`InfoProxyPageInterface`、`AgentFriendlist` 五份文件没有差异。下述旧实现问题不能直接归因为最近更新改变了这些布局；也不代表其他签名和游戏调用约定无需重新验证。

## 确认的问题

1. **已停用的功能仍残留主动 Hook。** 当前分支的 `GameFunctions` 构造函数仍初始化并启用好友请求、名字格式化和接收回调三个 Hook（[GameFunctions.cs](../XIVChatPlugin/GameFunctions.cs)，73–80、149–153 行）。桌面端没有请求入口且接收分支为空，不会让这些 Hook 自动停止。原历史提交 `8bbb452` 的说明明确包含移除好友扫描，因此只能复用协议设计，不能据此认为旧读取代码经过验证。

2. **游戏请求从后台网络线程直接发起。** `SpawnClientTask` 的 Task.Run 接收循环进入 `ProcessMessage`，Friend 请求分支直接调用 `RequestFriendList`，后者直接调用游戏函数（[Server.cs](../XIVChatPlugin/Server.cs)，310–315、420–429 行；[GameFunctions.cs](../XIVChatPlugin/GameFunctions.cs)，241–248 行）。缺少框架线程调度和执行时登录状态校验，可能与游戏主线程更新列表并发。

3. **完整列表的完成条件缺失。** `OnReceiveFriendList` 在一次回调中清空受管列表、复制当前条目、立即发布，然后清除 RequestingFriendList（[GameFunctions.cs](../XIVChatPlugin/GameFunctions.cs)，291–368 行）。没有校验该回调属于哪个 proxy／请求，也未等待分页结束。上游 `InfoProxyPageInterface.AddPage` 明确负责分页，并在全部数据加载后调用 EndRequest；旧逻辑可能发布半份列表并忽略后续页面。是否该旧签名实际指向最终回调尚未证实，不能依赖方法名字判断。

4. **等待者存在并发与生命周期问题。** `_waitingForFriendList` 是普通 HashSet，各客户端网络任务写入，而 async void 回调遍历、await 后清空（[Server.cs](../XIVChatPlugin/Server.cs)，54、94–105、424 行）。并发请求可能造成枚举异常或新请求被清掉；请求失败、客户端断开时没有清理等待者或返回失败结果。RequestingFriendList 无超时，若收不到回调，后续请求会一直被挡住；空／无效数据路径也不发布结果。

5. **手写布局与边界检查不准确。** 本地 `fc[14]` 和 HandleString 长度 14（[GameFunctions.cs](../XIVChatPlugin/GameFunctions.cs)，390、399 行）与当前 FFCS 的 6 字节 FCTag 不符，允许读取后面的保留字节。条目数量只排除大于 2000 的情况，而当前好友结构是 200 项；这无法阻止异常数量导致遍历超出好友数组有效范围。`data` 被直接当作 InfoProxyCommonList 解引用的假设也缺少可核对的 ABI 依据。

6. **现有协议尚未完成好友身份传输。** 读取到的 ContentId 只用于过滤零值，未写入协议 Player（其字段为 Key 0–14）。快照也没有 Owner、请求标识、完整性或更新时间，无法可靠拒绝旧角色／旧请求的迟到结果。客户端 `ServerOperation.PlayerList` 仍为直接 break（[Connection.cs](../XIVChat%20Desktop/Connection.cs)，339–340 行）。

旧接收委托在历史提交 `9316b85` 中从 `(nint, nint) -> nint` 改为 `(uint, nint) -> void`，但本次未解析游戏二进制来确认当前真实签名。此项属于必须验证的调用约定疑点，不作为已证实的崩溃根因。

## 可行的替换路径

- 使用实际运行版本的 `InfoProxyFriendList.Instance()` 获取 proxy，在框架线程上检查登录状态、指针及数量，复制 `CharDataSpan` 为受管快照；不再依赖旧的管理器捕获 Hook 或自定义结构。
- 先验证已加载缓存的只读快照，再验证 `RequestData()` 及完整请求结束。缓存为空不能直接认定没有好友，必须区分尚未加载与已完成的空列表。`AgentFriendlist.RequestFriendInfo` 是单个好友在线信息请求，不能用它替代完整列表刷新。
- 快照同时带登录角色、连接／请求代次和更新时间；主线程统一管理请求状态、节流、超时与注销清理。完成复制后再异步序列化，网络线程不持有游戏指针。
- 删除或隔离旧好友读取 Hook 后实现新读取服务；保留旧操作码／字段的兼容性，追加 ContentId 和快照元数据。好友身份补全与 Lodestone 映射保持独立，不传输无关的 AccountId。
- 最后接入桌面端请求／接收、角色快照和列表界面，按 plan 的在线、离线、空列表、跨服、分页、掉线和切角色场景实测。

### 本次验证的范围

隔离的编译探针直接引用当前安装的 FFCS 和 InteropGenerator.Runtime，成功编译 `Instance()`、`EntryCount`、`CharDataSpan`、`NameString`、`ContentId`、世界与状态字段、`RequestData()`、`GetEntryByName()`，0 错误、0 警告。这证明 API 在已安装程序集里存在且可以编译；探针未执行，没有读游戏内存或发出好友请求，不能据此宣称刷新及分页已经实测成功。

参考源码：[好友 proxy](https://github.com/aers/FFXIVClientStructs/blob/21898bf815f0e56e02b7dc08f0a3e24822c759d0/FFXIVClientStructs/FFXIV/Client/UI/Info/InfoProxyFriendList.cs)、[列表结构](https://github.com/aers/FFXIVClientStructs/blob/21898bf815f0e56e02b7dc08f0a3e24822c759d0/FFXIVClientStructs/FFXIV/Client/UI/Info/InfoProxyCommonList.cs)、[分页接口](https://github.com/aers/FFXIVClientStructs/blob/21898bf815f0e56e02b7dc08f0a3e24822c759d0/FFXIVClientStructs/FFXIV/Client/UI/Info/InfoProxyPageInterface.cs)、[Dalamud 框架线程接口](https://github.com/goatcorp/Dalamud/blob/c4c825465ab432d6d80539df8179bd7cdaf6ff00/Dalamud/Plugin/Services/IFramework.cs)。
