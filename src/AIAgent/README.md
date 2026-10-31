# AIAgent 服务器智能助手

- 作者: 星梦
- 出处: `https://github.com/Zykor-Club/TShockServerPlugin`
- 接入大语言模型的服务器助手：聊天栏直接对话、长期记忆、工具调用、事件播报
- 首次加载自动释放人设与离线 Wiki 到 `tshock/AIAgent/`，之后可直接编辑

## 功能

- **聊天栏直连**：玩家直接说话即可，不需要输入指令；支持智能/全部/提及三种接话模式
- **长期记忆**：记住称呼、喜好、事件与承诺，按好感度调整语气；玩家可查看和删除自己的记忆
- **工具调用**：查配方、反查材料、查 Wiki、看 Boss 进度、给物品、治疗、传送、改时间/天气、执行服务器指令
- **事件播报**：Boss 出现与被击败、血月、日食、下雨
- **主动消息**：服务器安静时自言自语一句，不点名、不刷屏
- **问候与关怀**：进服欢迎、深夜提醒、连死安慰
- **权限鉴权**：工具按 TShock 原生权限下发；指令以玩家本人身份执行，不越权

## 指令

| 语法 | 权限 | 说明 |
|-----|:----:|------|
| `/aig <内容>` | `aiagent.use` | 和 AI 单独说话 |
| `/aig help` | `aiagent.use` | 查看帮助与当前可用能力 |
| `/aig status` | `aiagent.use` | 查看身份、记忆键、今日用量、好感度 |
| `/aig memory` | `aiagent.use` | 查看它记得你什么 |
| `/aig memory forget` | `aiagent.use` | 删除它对你的全部记忆 |
| `/aig memory correct <内容>` | `aiagent.use` | 更正一条记忆 |
| `/aig admin doctor` | `aiagent.admin` | 环境自检 |
| `/aig admin probe` | `aiagent.admin` | 测试模型连通性 |
| `/aig admin usage` | `aiagent.admin` | 今日用量 |
| `/aig admin setkey <密钥>` | `aiagent.admin` | 设置 API 密钥（不写日志） |
| `/aig admin model <模型名>` | `aiagent.admin` | 切换模型 |
| `/aig admin flushmem [玩家]` | `aiagent.admin` | 立即整理记忆 |
| `/aig admin simulate <文本>` | `aiagent.admin` | 按聊天消息处理一次 |
| `/aig admin simulatedeath` | `aiagent.admin` | 触发一次死亡回应 |
| `/aig admin save` | `aiagent.admin` | 保存配置 |
| `/aig admin reload` | `aiagent.admin` | 重载配置、人设与 Wiki |

## 工具

工具由模型按需调用，玩家不需要记指令；是否下发取决于玩家权限。

| 工具 | 权限 | 说明 |
|-----|:----:|------|
| `get_my_status` / `get_world_info` / `get_world_progress` | `aiagent.use` | 状态、世界、Boss 进度 |
| `search_wiki` / `get_recipe` / `find_recipes_for` | `aiagent.use` | 离线 Wiki、配方、反查材料 |
| `heal_player` | `tshock.heal` | 治疗 |
| `give_item` | `tshock.item.give` | 给物品 |
| `teleport_to_player` / `bring_player` | `tshock.tp.self` / `tshock.tp.others` | 传送 |
| `set_time` / `set_weather` | `tshock.world.time.set` / `tshock.world.rain` | 时间与天气 |
| `run_command` / `list_commands` | `aiagent.tool.command` + 指令自身权限 | 执行 TShock 指令 |

## 配置

> 配置文件路径：`tshock/AIAgent/config.json`（首次启动生成，带注释；支持 `//` 注释与尾逗号）

```json5
{
  "AI名称": "香草",
  "人设": "",              // 留空 = 读取同目录 persona.md
  "模型名称": "deepseek-chat",
  "温度": 0.8,

  "供应商": {
    "当前": "deepseek",
    "列表": {
      "deepseek": {
        "名称": "DeepSeek",
        "地址": "https://api.deepseek.com/v1",
        "密钥": "",
        "默认模型": "deepseek-chat"
      }
    }
  },

  "聊天": {
    "启用": true,
    "回复模式": "智能",     // 智能 / 全部 / 提及
    "抢话判定": true,       // 关掉后每条都回，但更慢
    "智能阈值": 40,
    "公屏回复": true,
    "事件播报": true,
    "每日回复上限": 200
  },

  "记忆": {
    "启用": true,
    "玩家可删除": true,
    "记忆预算Token": 600,
    "摘要触发轮数": 24
  },

  "问候": { "启用": true, "最小间隔小时": 12, "延迟秒": 12, "公屏": true },
  "关怀": { "深夜关怀": true, "深夜时段": "01:00-05:00", "连死次数": 3 },

  "主动消息": {
    "启用": true,
    "间隔分钟": 45,
    "安静分钟": 15,
    "每日上限": 6,
    "公屏": true
  },

  "指令": {
    "启用": true,           // 是否把 run_command 交给模型
    "禁止指令": ["exit", "stop", "reload", "config", "user", "group", "ban", "kick"]
  },

  "工具": {
    "give_item": { "启用": true, "每日次数": 20 }
  },

  "每日Token预算": 50000,
  "调试日志": false
}
```

## 人设

- 默认读取 `tshock/AIAgent/persona.md`，首次启动会从插件内嵌资源释放一份；
- 配置里的 `人设` 只是兜底：文件不存在时才用它，两者都为空时用内置的一句话；
- 想改人设直接编辑 `persona.md`，执行 `/aig admin reload` 生效。

## 依赖

- TShock 6.x / Terraria 1.4.5.x / .NET 9
- 需要可访问的 OpenAI 兼容接口（DeepSeek / OpenAI / Gemini 均可）

## 构建

```bash
dotnet build src/AIAgent/AIAgent.csproj -c Release
```

产物输出到 `out/Release/AIAgent.dll`，放入服务器 `ServerPlugins/` 即可。

## 更新日志

### v2.0.0 (2026-11-01)
- 全新重构：聊天、长期记忆、工具调用、事件播报、主动消息
- 14 个内置工具，全部按 TShock 原生权限鉴权
- 支持 TShock 指令执行（玩家身份，三层鉴权）
- 首次加载内嵌人设与离线 Wiki

## 反馈

优先发 Issue -> 共同维护的插件库：`https://github.com/Zykor-Club/TShockServerPlugin`
次优先：TShock 官方群：816771079
