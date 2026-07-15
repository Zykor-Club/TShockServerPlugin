# HiddenPlayerPlugin 隐藏玩家插件

- **作者**: 星梦
- **版本**: v1.3.0
- 出自：https://github.com/Zykor-Club/TShockServerPlugin/tree/main/src/HiddenPlayerPlugin

## 功能概述

- 隐藏特定玩家的加入离开服务器广播消息
- 在 `/who`、`/playing`、`/online` 等查询在线玩家的功能中不显示隐藏玩家
- 隐藏玩家加入服务器后对其它玩家完全隐身（无模型、无名牌）
- 占卜球旁观列表过滤隐藏玩家

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
  ],
  "EnableInvisibility": true
}
```

- `HiddenPlayerNames`：隐藏玩家名单
- `EnableInvisibility`：是否让隐藏玩家对其它玩家隐身（默认 `true`）

## 插件版本

### v1.3.0

- 隐身实现改为纯数据包操控方案：服务器端保持 `active = true`（确保物品拾取），只拦截并修改网络数据包
- 配置文件在开服时自动生成
- 支持服务器 `/reload` 命令自动重载配置
- `/hideplayer add` 对在线玩家立即隐身，`/hideplayer remove` 对在线玩家立即解除隐身

### v1.2.0

- 隐藏玩家加入后对其它玩家完全隐身
- 通过 `PlayerActive` 包过滤 + `invis` 标记实现，不依赖 buff
- 新增配置项 `EnableInvisibility`

### v1.1.0

- 使用 Harmony Patch 修改 TShock 内部方法
- 移除权限系统，所有玩家统一看不到隐藏玩家

### v1.0.0

- 初始版本
- 支持隐藏玩家加入/离开广播

## 反馈

- 优先发 issue -> 星梦的插件库：https://github.com/Zykor-Club/TShockServerPlugin
- 次优先：TShock 官方群：816771079