# Changelog / 更新说明

## Desktop 1.3.7 — 2026-09-14

### English

- Fixed chat and main windows reopening outside the screen on a secondary monitor.
- Previously saved off-screen positions are moved back into the available display area automatically.
- Compatible with plugin 1.7.15; this update requires replacing only the desktop client.

### 简体中文

- 修复副屏上的聊天小窗和主窗口在恢复布局时跑到屏幕外的问题。
- 之前保存的屏幕外位置会自动收回可用显示区域。
- 兼容插件 1.7.15，本次只需更新桌面客户端。

## Plugin 1.7.15 · Desktop 1.3.6 — 2026-09-14

### English

- New chat workspace: conversations, fixed-target tells, unread counts, friends and optional avatars.
- Character-scoped local history, search, favorites, notes and export.
- Item/map cards, equipment comparisons and game screenshots with preview/save/copy.
- Chat popouts, saved layouts, event notifications and first-run guidance.
- Optional self-hosted relay with HTTPS, persistent pairing and English/Chinese web administration.
- New plugin configurations default to English; existing language choices are preserved.
- English-first bilingual website, README and maintained guides.
- Fixed portable startup, short-window navigation and nested plugin ZIPs.
- Runtime resources retain English, Japanese, German, Chinese and French; application UI remains English/Chinese.
- Local tests, process documentation and retired artwork moved outside the product tree.

Upgrade the client and plugin together. Back up configuration, history and relay data, and retain the previous package for rollback. Saved relay pairing normally survives restarts; legacy public-relay codes do not work with the new service.

Validation includes local regression, desktop startup and previous live game integration. Clean-machine installation and extended weak-network/game-restart scenarios have not all been independently verified.

## 简体中文

### 插件 1.7.15 · 客户端 1.3.6 — 2026-09-14

- 新工作台：会话、固定悄悄话、未读、好友和可选头像。
- 按角色隔离的历史、搜索、收藏、备注与导出。
- 物品/地图卡片、装备对比、游戏截图及预览/保存/复制。
- 聊天小窗、布局保存、事件通知和首次引导。
- 可选自建中继：HTTPS、持久配对和中英文网页后台。
- 插件新配置默认英文，已有选择保留。
- 介绍页、README 和维护中的说明提供中英文，英文优先。
- 修复独立分发启动、小窗口导航和插件 ZIP 嵌套。
- 运行库保留英、日、德、中、法，应用界面为中英文。
- 测试、过程文档和旧素材移至产品仓库外本地归档。

请配套升级两端，备份配置、历史和中继数据，并保留旧包回退。已保存配对通常跨重启保留，旧公共中继认证码不能用于新服务。

已完成本地回归、启动检查和此前游戏联调；干净机器、长期弱网与游戏重启场景尚未全部独立验收。
