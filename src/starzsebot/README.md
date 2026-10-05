# starZSEbot

**作者**:

- 主开发：星梦

- **版本**: v2026.7.7.1

> [!IMPORTANT]
> 本插件是 QQ 机器人 [ZSE-StarBot](https://github.com/Zykor-Club/ZSE-StarBot) 的服务器侧适配插件，需要搭配机器人服务端使用，单独安装无法工作。
> 玩家数据经由 WebSocket 长连接在服务器与机器人之间传输，使用前请先在插件配置中填写机器人服务端地址并完成绑定。

> [!IMPORTANT]
> 本插件依赖 `linq2db`（用于统计数据的本地存储），请将 `linq2db.dll` 一同放入 `ServerPlugins` 文件夹。
> 以下插件为**可选**依赖，本插件通过运行时反射探测，未安装时对应功能自动降级、不影响其余功能：
> - `GenerateMap`：不安装则"获取地图"相关数据包不可用
> - `Economics.Core` / `Economics.RPG` / `Economics.Skill`：不安装则查询背包时不附带货币 / 职业 / 技能信息
> - `BossLock` / `ProgressControls`：不安装则进度数据包中不包含进度锁 BOSS 信息
> - `AutoResetPlus`：不安装则群内「重置 / 种子投票（随机生成）」不可用（返回未安装提示）；存档导出不受其影响

## 功能概述

- 通过 WebSocket 长连接（支持 TLS）与 QQ 机器人通信，支持绑定码绑定 / 解绑 / 断线自动重连 / 心跳
- 白名单进服校验与免注册登录：玩家进服时上报玩家名、设备 UUID 与设备平台（PC / PE 等），由机器人判定并回包；通过后插件直接接管登录（自动注册 / 登录，不再出现 /register /login 提示），未绑定 / 未授权设备 / 冻结账号将被踢出（需登录确认时提示玩家到群内 @机器人 发送「登录」）
- 查询背包：在线玩家直读角色数据，离线玩家读取 TShock SSC 存档；附带生命 / 魔力 / 任务数 / Buff / 装备前缀
- 世界地图：调用 GenerateMap 生成地图图片、地图文件，或回传世界文件
- 进度数据：世界进度、各 BOSS 击杀次数、BossLock / ProgressControls 进度锁状态、世界图标
- BOSS 首杀播报：世界内 boss 首次被击杀时推送 `progress_notify` 包（boss / 击杀玩家 / 时间 / 世界名），世界重置后自动重新武装，不补发历史击杀
- 在线玩家列表：名称、在线人数、人数上限、当前世界进度
- 远程指令执行：机器人在群内设置的远程命令由服务器执行并回传输出
- 全服喊话：机器人的广播以绿色文字发到服务器内
- 排行榜：BOSS 击杀 / 死亡 / 在线时长 / 钓鱼任务 / 货币排行
- 经济数据：查询背包附带 Economics 的货币 / 职业 / 技能信息
- 商店与邮件：物品解锁条件校验、购买后通过邮件发放物品或执行指令
- 玩家自踢：机器人可强制某位玩家下线
- 种子投票与重置桥接：向机器人提供世界种子配置读取、写入种子、触发重置的能力（运行时反射桥接 AutoResetPlus）；导出全部账号角色与当前世界存档并打包回传

## 游戏内命令（`/starzsebot` 或 `/zse`）

| 命令 | 权限 | 说明 |
| ---- | ---- | ---- |
| `/starzsebot debug` | starzsebot.admin | 调试模式开关 |
| `/starzsebot code` | starzsebot.admin | 生成服务器绑定码 |
| `/starzsebot info` | starzsebot.admin | 显示插件状态（版本 / WebSocket / 绑定状态 / 可选依赖探测结果） |
| `/starzsebot unbind` | starzsebot.admin | 主动解除机器人绑定 |
| `/starzsebot whitelist` | starzsebot.admin | 开关白名单校验 |
| `/starzsebot group <群号>` | starzsebot.admin | 设置白名单踢出提示中的 QQ 群号 |
| `/starzsebot reset` | starzsebot.admin | 重置统计数据（击杀 / 在线时长 / 邮件缓存） |
| `/starzsebot test` | starzsebot.admin | 预留测试命令 |

## 配置文件

位置：`tshock/starZSEbot.json`（不存在时开服自动生成）

| 配置项 | 类型 | 默认值 | 说明 |
| ---- | ---- | ---- | ---- |
| 白名单开关 | 布尔值 | true | 是否启用进服白名单校验 |
| 服务器地址 | 字符串 | api.terraria.ink:22338 | 机器人服务端地址（IP:端口 或 域名:端口） |
| 启用TLS | 布尔值 | true | 机器人服务端是否使用 https / wss |
| 固定证书指纹 | 布尔值 | true | 用「证书 SHA-256 指纹固定」代替域名/CA 校验（配合 IP 直连使用，详见下节） |
| 证书指纹 | 字符串 | （空） | 留空则首次连接自动固定（TOFU）；换服务器或服务器换证书时清空即可重新固定 |
| 密钥 | 字符串 | （空） | 绑定成功后自动获取并写入，无需手动填写 |
| 群OpenID | 字符串 | 114514 | 绑定成功后自动获取并写入，无需手动填写 |
| 在线显示进度 | 布尔值 | true | 在线玩家列表数据包是否附带世界进度文本 |
| 商店分组标签 | 字符串 | 生存服 | 商店系统按分组标签筛选商品 |
| 白名单拦截提示的群号 | 长整数 | 0 | 被拦截玩家提示加入的 QQ 群号（0 为不显示） |

## 连接与证书（部署必看）

### 推荐配置

```json
{
  "服务器地址": "43.249.195.13:10214",
  "启用TLS": true,
  "固定证书指纹": true,
  "证书指纹": ""
}
```

**填 IP 直连，不要填域名**；`证书指纹` 留空即可，首次连接会自动固定。

> 机器人服务与游戏服在**同一台机器**时，`服务器地址` 可填 `127.0.0.1:13140`（更快，且不受机房入站策略影响）。

### 为什么用 IP 直连而不是域名？

部分机房会对入站流量做 **DPI + 域名过白**：TLS 握手里的 SNI 域名若未在机房登记，连接会被**直接重置**（明文 HTTP 则被机房代理接管，返回「请联系机房域名过白」页面）。

.NET 对 **IP 字面量不发送 SNI**（符合 RFC 6066），因此 IP 直连可以正常握手。代价是证书域名与 IP 不匹配、无法走系统默认校验，所以本插件改为**固定服务器证书的 SHA-256 指纹**：

- 首次连接（`证书指纹` 为空）：记住对端证书指纹并放行（Trust On First Use），日志输出 `首次连接，已固定服务器证书指纹: XXXX`
- 之后每次连接：指纹必须与记录**完全一致**，否则拒绝连接并打印「收到 / 期望」两个指纹
- 指纹不匹配时**不会自动改写**，确认服务器确实换过证书后**手动清空 `证书指纹`** 再重连

### 常见问题

| 日志现象 | 原因与处理 |
| --- | --- |
| `服务器证书指纹不匹配，已拒绝连接！` | 服务器确实换了证书：清空 `证书指纹` 后重启插件重新固定 |
| `由于目标计算机积极拒绝` | 地址/端口不对，或该端口未在机房做好映射 |
| `Bot断开连接`（无更多细节） | 检查 `启用TLS` 与服务端是否一致；服务端开 TLS 时客户端必须开 |

## 绑定流程

1. 将 `starZSEbot.dll`（及 `linq2db.dll`）放入 `ServerPlugins` 后重启服务器
2. 控制台会以 `[starZSEbot]您的服务器绑定码为: xxxxxx` 输出 6 位绑定码（也可用 `/zse code` 重新生成）
3. 在机器人侧使用绑定码登记服务器地址与端口，机器人会分配访问密钥
4. 插件自动请求 `{http(s)}://{服务器地址}/server/token/{绑定码}` 获取密钥，并连接 `{ws(s)}://{服务器地址}/server/ws/{群OpenID}/tshock/`
5. 连接成功后 `hello` 数据包会携带服务器名、游戏版本、TShock 版本、插件版本与白名单开关

如需更换机器人，使用 `/zse unbind` 解绑后重新绑定即可。

## 数据包一览

| 类型 | 方向 | 说明 |
| ---- | ---- | ---- |
| hello | 插件 → 机器人 | 上报服务器名 / 游戏版本 / TShock 版本 / 插件版本 / 白名单开关 |
| heartbeat | 插件 → 机器人 | 心跳保活 |
| whitelist | 双向 | 玩家进服白名单校验：插件上报玩家、UUID 与设备平台，机器人回包判定结果 |
| look_bag | 双向 | 查询背包（在线直读 / 离线读 SSC），回包含物品 / Buff / 前缀 / 经济数据 |
| player_list | 双向 | 在线玩家列表与人数 |
| progress | 双向 | 世界进度、BOSS 击杀数、进度锁、世界图标 |
| progress_notify | 插件 → 机器人 | BOSS 首杀播报：`boss_key` / `players`（击杀玩家并集）/ `kill_time` / `world_name`；世界重置后重新武装，不补发历史击杀 |
| map_image / map_file | 双向 | 地图图片 / 地图文件（需 GenerateMap） |
| world_file | 双向 | 世界文件回传 |
| call_command | 双向 | 远程执行服务器指令并回传输出 |
| say | 机器人 → 插件 | 全服喊话（绿色文字） |
| rank_data | 双向 | 排行榜数据（击杀 / 死亡 / 在线 / 钓鱼 / 货币） |
| shop_condition / shop_buy | 双向 | 商店解锁条件校验 / 购买发货 |
| plugin_list | 双向 | 服务器插件列表 |
| self_kick | 机器人 → 插件 | 强制玩家下线 |
| unbind_server | 机器人 → 插件 | 解除绑定并重新生成绑定码 |
| auto_reset | 双向 | AutoResetPlus 桥接：`is_request=true`，`payload.action` ∈ get_config / set_seed / do_reset。get_config 回包 `installed` / `world_name` / `current_seed` / `random_enable` / `seed_list` / `min` / `max` / `online_minutes`（各账号在线分钟数，取自 zse_statistic）；set_seed 将 `\|` 连接的种子组合写入 AutoResetPlus 预设并保存，回包 `ok`；do_reset 触发重置流程，非 Available 状态时回失败原因；未安装插件回「未检测到 AutoResetPlus 插件」 |
| archive_export | 双向 | 存档导出：导出 tsCharacter 全部账号为 `.plr`（在线玩家取 TPlayer、离线玩家重建 Player）并复制当前 `.wld`，以 `SmallestSize` 打包 zip，本地保留在 `tshock/starZSEbot/Exports/`，回包 `{name, base64}`（base64 为 gzip 压缩后的 zip）；不依赖 AutoResetPlus，未安装也可用 |

> `auto_reset` 由 `Common/AutoResetSupport.cs` 在运行时反射访问 AutoResetPlus（未安装时 `installed=false` 降级、不抛异常）；`archive_export` 由 `Common/ArchiveExport.cs` 实现，不依赖 AutoResetPlus；白名单免注册登录与设备平台识别由 `Common/LoginHelper.cs`（MessageBuffer 只读钩子 + NetGetData 阻断原生握手）与 `Common/PlatformTracker.cs` 实现；首杀播报由 `Common/ProgressNotify.cs` 实现。

## 兼容性

- 目标框架：.NET 9.0（net9.0）
- 适配：TShock 6.2.1 / Terraria 1.4.5.8
- 对可选插件的调用全部通过运行时反射完成，编译时不需要引用其程序集

## 插件版本

### v2026.7.7.1

- 首次发布：白名单校验、查询背包、进度、地图、远程指令、喊话、排行榜、经济数据、商店邮件、多群联合服务器管理等完整功能

## 反馈

- 优先发 issue -> <https://github.com/Zykor-Club/TShockServerPlugin>
- 机器人源码 -> <https://github.com/Zykor-Club/ZSE-StarBot>