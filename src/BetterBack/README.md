# BetterBack 更好的死亡返回
- **作者**: 星梦XM
- **版本**: v2026.5.2.0
## 功能概述
- 记录玩家死亡位置，支持快速返回
- 支持自定义传送Buff和无敌时间
- 支持自动返回死亡点
- 禁止在未击败骷髅王/世纪之花前记录地牢/神庙死亡点
## 指令
| 语法 | 权限 | 说明 |
|------|:----:|:-----:|
| `/bet` | `betterback.use` | 传送至最新死亡点 |
| `/bet [序号]` | `betterback.use` | 传送至指定死亡点 |
| `/bet list` | `betterback.use` | 查看死亡点列表 |
| `/bet clear` | `betterback.use` | 清除所有死亡点 |
| `/bet auto <秒/off>` | `betterback.use` | 设置自动返回倒计时 |
| `/bet help` | `betterback.use` | 显示帮助信息 |
| `/betbuff add <id>` | `betterback.buff` | 添加自定义Buff |
| `/betbuff remove <id>` | `betterback.buff` | 移除自定义Buff |
| `/betbuff list` | `betterback.buff` | 查看自定义Buff列表 |
| `/betgod time <秒>` | `betterback.god` | 设置无敌时间 |
| `/betgod info` | `betterback.god` | 查看无敌时间设置 |
## 配置
配置文件路径：`tshock/BetterBack.json`
```json
{
  "最大死亡点数量": 5,
  "传送冷却时间": 30,
  "默认传送BuffID": [1, 3, 5],
  "Buff持续时间": 10,
  "无敌时间": 5,
  "死亡点记录消息": "已记录死亡点 ({0}/{1})",
  "传送成功消息": "已传送至死亡点: {0}",
  "无敌时间消息": "您获得了 {0} 秒无敌时间",
  "冷却时间消息": "请等待 {0:F1} 秒后再试",
  "禁止记录未击败骷髅王的地牢死亡": true,
  "禁止记录未击败世纪之花的神庙死亡": true
}
```
## 配置说明
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|:------:|------|
| 最大死亡点数量 | int | 5 | 每个玩家最多记录的死亡点数量 |
| 传送冷却时间 | int | 30 | 传送冷却时间（秒） |
| 默认传送BuffID | array | [1,3,5] | 传送后自动给予的Buff ID列表 |
| Buff持续时间 | int | 10 | Buff持续时间（秒） |
| 无敌时间 | int | 5 | 传送后无敌时间（秒） |
| 死亡点记录消息 | string | - | 记录死亡点时显示的消息 |
| 传送成功消息 | string | - | 传送成功时显示的消息 |
| 无敌时间消息 | string | - | 获得无敌时间时显示的消息 |
| 冷却时间消息 | string | - | 冷却中时显示的消息 |
| 禁止记录未击败骷髅王的地牢死亡 | bool | true | 是否禁止记录地牢死亡点 |
| 禁止记录未击败世纪之花的神庙死亡 | bool | true | 是否禁止记录神庙死亡点 |
## 权限
| 权限节点 | 说明 |
|----------|------|
| `betterback.use` | 使用传送命令 |
| `betterback.buff` | 管理传送Buff |
| `betterback.god` | 管理无敌时间 |
| `betterback.admin` | 管理员权限（预留） |
## 插件版本
### v2026.5.2.0
- 初始版本发布
- 支持死亡点记录和传送
- 支持自定义Buff和无敌时间
- 支持自动返回功能
- 支持地牢/神庙死亡点限制
## 反馈
- 优先发issue -> 星梦的插件库：https://github.com/Zykor-Club
- 次优先：TShock官方群：816771079
