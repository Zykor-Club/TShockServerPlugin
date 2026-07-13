# HiddenPlayerPlugin 隐藏玩家插件
- **作者**: 星梦
- **版本**: v1.1.0
- 出自：https://github.com/Zykor-Club/TShockServerPlugin/src/HiddenPlayerPlugin

## 功能概述
- 隐藏特定玩家的加入离开服务器广播消息
- 在 `/who`、`/playing`、`/online` 等查询在线玩家的功能中不显示隐藏玩家

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

## 插件版本
### v1.1.0
- 使用 Harmony Patch 修改 TShock 内部方法
- 移除权限系统，所有玩家统一看不到隐藏玩家

### v1.0.0
- 初始版本
- 支持隐藏玩家加入/离开广播

## 反馈
- 优先发issue -> 星梦的插件库：https://github.com/Zykor-Club
- 次优先：TShock官方群：816771079
