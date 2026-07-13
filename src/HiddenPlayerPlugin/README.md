# HiddenPlayerPlugin 隐藏玩家插件
- **作者**: 星梦
- **版本**: v1.1.0

## 功能概述
- 隐藏特定玩家的加入离开服务器广播消息
- 在 `/who`、`/playing`、`/online` 等查询在线玩家中不显示隐藏玩家
- 使用 Harmony Patch 修改 TShock 内部方法
- 支持过滤 MOTD 中 `%players%` 变量显示的玩家列表
- 支持过滤 `%onlineplayers%` 变量的玩家计数

## 指令
| 语法 | 权限 | 说明 |
|------|:----:|:-----:|
| `/hideplayer add <玩家名>` | `hiddenplayer.admin` | 添加隐藏玩家 |
| `/hideplayer remove <玩家名>` | `hiddenplayer.admin` | 移除隐藏玩家 |
| `/hideplayer list` | `hiddenplayer.admin` | 列出所有隐藏玩家及在线状态 |
| `/hideplayer reload` | `hiddenplayer.admin` | 重新加载配置文件 |
| `/hideplayer save` | `hiddenplayer.admin` | 保存配置文件 |

## 配置
配置文件路径：`tshock/HiddenPlayerConfig.json`
```json
{
  "HiddenPlayerNames": [
    "星梦",
    "夜空"
  ]
}
```

## 配置说明
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|:------:|------|
| HiddenPlayerNames | array | [] | 隐藏玩家名称列表 |

## 权限
| 权限节点 | 说明 |
|----------|------|
| `hiddenplayer.admin` | 管理隐藏玩家列表 |

## 插件版本
### v1.1.0
- 使用 Harmony Patch 修改 TShock 内部方法
- 支持过滤 MOTD 中 `%players%` 变量显示的玩家列表
- 支持过滤 `/who`、`/playing`、`/online` 命令中的玩家列表
- 支持过滤 `%onlineplayers%` 变量的玩家计数
- 移除权限系统，所有玩家统一看不到隐藏玩家

### v1.0.0
- 初始版本
- 支持隐藏玩家加入/离开广播

## 反馈
- 优先发issue -> 星梦的插件库：https://github.com/Zykor-Club
- 次优先：TShock官方群：816771079
