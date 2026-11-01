# AIAgentAvatar AI 分身

- 作者: 星梦
- 出处: `https://github.com/Zykor-Club/TShockServerPlugin`
- 原始项目: [FakePlayer](https://github.com/1242509682/FakePlayer)（少司命、羽学，GPLv3）
- 用 TrProtocol 建立真实客户端连接，为 AIAgent 提供一个可被 AI 操控的"分身"
- 默认只允许 1 个分身，默认开启说话

## 功能

- 单分身：`最大假人数` 默认 1，避免占满玩家槽位
- 真实客户端：登录、走路、跳跃、开门、攻击、躲弹幕、超距离传送都走协议
- AI 接口：`FakeApi` 提供说话、跟随、定点、指定攻击目标、停止
- 默认发言：`默认发言` 打开时，分身可以说话
- 自动创建：`自动创建分身` 打开时，服务器启动 10 秒后自动登录一个分身

## 指令

沿用原版 FakePlayer 的 `/fake` 指令：

| 语法 | 权限 | 说明 |
|-----|:----:|------|
| `/fake add` | `fake.use` | 创建一个假人（本改造默认上限 1） |
| `/fake del [索引|all]` | `fake.use` | 移除假人 |
| `/fake me [索引|all]` | `fake.use` | 跟随/停止跟随 |
| `/fake list` | `fake.use` | 列表 |
| `/fake set` / `/fake item` | `fake.use` | 背包 |
| `/fake save` / `/fake load` | `fake.use` | 角色模板 |
| `/fake reset` | `fake.admin` | 重置数据 |

## 给 AIAgent 的接口（FakeApi）

| 方法 | 作用 |
|---|---|
| `FakeApi.Get()` | 分身状态（槽位、名字、状态、坐标、血量、跟随目标） |
| `FakeApi.Say(text)` | 让分身说话 |
| `FakeApi.Follow(player)` | 跟随玩家 |
| `FakeApi.Goto(x, y)` | 走到世界坐标（像素）并停住 |
| `FakeApi.Attack(npcSlot)` | 指定攻击目标 |
| `FakeApi.Stop()` | 停止跟随/攻击/移动 |
| `FakeApi.Spawn(name)` / `Despawn()` | 创建/移除分身 |

## 配置

> 路径：`tshock/假人插件/配置文件.json`

```json5
{
  "最大假人数": 1,
  "默认发言": true,
  "自动创建分身": false,
  "分身名称": "香草",
  "名称前缀": "假人",
  "自动注册": true,
  "注册密码": "123456",
  "移动速度": 12,
  "寻敌检查半径": 40,
  "漫游随机范围格数": 500
}
```

## 依赖

- TShock 6.2.1 / Terraria 1.4.5.8
- `TrProtocol.dll`：放在 `ServerPlugins/`，编译期从仓库根目录 `lib/TrProtocol.dll` 引用
- 建议配合 [FixShock](https://github.com/1242509682/FixShock) 缓解高负载下的 socket 内存分配

## 更新日志

### v1.1.0 (2026-11-01)
- 单分身模式（上限可配）
- 新增 `FakeApi`：说话/跟随/定点/指定目标/停止
- 定点模式与指定目标（原版只有自动寻敌）
- 发送加锁，避免心跳与主线程并发写包
- 创建失败有日志，不再静默
- 默认发言、自动创建开关

### 原版 v1.0.6
- 适配 Terraria 1.4.5.8，加入 FixShock 依赖

## 许可证

GPLv3。本插件基于 GPLv3 的 FakePlayer 改造，保留原作者署名；修改部分同样以 GPLv3 发布。
