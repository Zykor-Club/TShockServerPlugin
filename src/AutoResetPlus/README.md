# AutoResetPlus 重置插件增强版

- 作者: Eustia & cc04 & Leader & 棱镜 & Cai & 肝帝熙恩 & 星梦
- 重置插件增强版,自定义要重置什么
- 适配: TShock 6.2.1 / Terraria 1.4.5.8
## 指令

| 语法                        |     权限      | 说明                                                                             |
|---------------------------|:-----------:|:-------------------------------------------------------------------------------|
| /reset 或 /重置世界            | reset.admin | 重置世界；不带参数时使用随机种子                                                               |
| /resetdata 或 /重置数据        | reset.admin | 仅重置数据（不生成地图）                                                                   |
| /rs info                  | reset.admin | 查看当前预设                                                                         |
| /rs name <地图名>            | reset.admin | 设置重置后地图名；不带参数跟随原世界名                                                            |
| /rs seed <世界种子>\|<秘密世界种子> | reset.admin | 设置重置用种子，使用\|来分割，例如rs 114514\|truck stop\|mole people ,也可以直接秘密世界种子rs truck stop |

## 配置

> 配置文件位置: tshock/AutoResetPlus/AutoReset.<语言>.json（中文服务器通常为 AutoReset.zh-Hans.json）
> 替换文件夹: tshock/AutoResetPlus/ReplaceFiles

```json5
{
  "替换文件": {},
  "击杀重置": {
    "击杀重置开关": false,
    "已击杀次数": 0,
    "生物ID": 50,
    "需要击杀次数": 50
  },
  "重置后指令": [],
  "重置前指令": [],
  "重置后SQL命令": [
    "DELETE FROM tsCharacter"
  ],
  "地图预设": {
    "地图名": null,
    "地图种子": null
  },
  "随机种子配置": {
    "开启重置后自动设置种子": false,
    "最少数量": 2,
    "最多数量": 4,
    "种子列表": [
      "no traps",
      "not the bees",
      "for the worthy",
      "don't dig up",
      "celebrationmk10",
      "constant",
      "get fixed boi",
      "how did i get here",
      "royale with cheese",
      "mole people",
      "night of the living dead",
      "too easy",
      "what a horrible night to have a curse",
      "bring a towel",
      "hocus pocus",
      "jingle all the way",
      "pumpkin season",
      "arachnophobia",
      "more traps please",
      "beam me up",
      "we don‘t even test for that",
      "abandoned manors",
      "save the rainforest",
      "the care bears movie",
      "double daring dangers",
      "such great heights",
      "sandy britches",
      "toadstool",
      "does that sparkle",
      "fish mox",
      "purify this",
      "winter is coming",
      "truck stop",
      "rainbow road",
      "jagged rocks",
      "waterpark",
      "planetoids",
      "i am error",
      "monochrome",
      "negative infinity",
      "xray vision",
      "invisible plane",
      "electric boogaloo",
      "calm before the storm"
    ]
  }
}
```

### 替换文件 ReplaceFiles 说明
- 启动时会创建目录 `tshock/AutoResetPlus/ReplaceFiles`。
- “替换文件”默认为空 `{}`，按需填写：左侧为目标文件路径，右侧为 ReplaceFiles 目录中的源文件名。
- 目标路径相对服务器运行目录解析（例如 `tshock/原神.json` 指服务器目录下的 `tshock\原神.json`），也可以写绝对路径；**开头不要带 `/`**，Windows 下 `/tshock/...` 会被解析成盘符根目录 `C:\tshock\...`，导致替换失败。
- 当右侧为非空字符串时，重置完成后会将源文件复制并覆盖到目标路径；当右侧为空字符串 `""` 时表示删除目标文件。
- 使用复制时，必须先把源文件放进 `tshock/AutoResetPlus/ReplaceFiles/`，否则重置时会提示“替换失败”。
- 示例：
  - "tshock/原神.json": "原神.json" → 将 `tshock/AutoResetPlus/ReplaceFiles/原神.json` 覆盖到服务器目录 `tshock/原神.json`
  - "tshock/XSB数据缓存.json": "" → 删除服务器目录 `tshock/XSB数据缓存.json`

### 击杀触发自动重置
- 默认关闭：配置项“击杀重置开关”为 false（可在 /rs info 查看）。
- 开启后：当击杀数达到“需要击杀次数”且生物 ID 匹配时自动执行 /reset。
- 始终可以手动执行 /reset；只有在开启击杀重置且达成条件时才会自动重置。
### 随机种子配置说明
- 默认关闭，需要手动开启
- 此功能开启并不会影响本次重置的种子，仅针对下次重置的种子
- 开启后，在重置完成之后，将会从预先设置的种子中，随机选取并设置种子，方便下次重置
## 更新日志
### v2026.10.2.0
- 适配 TShock 6.2.1 / Terraria 1.4.5.8
- 移除对 LazyAPI 的依赖，改用插件内置的配置基类（配置文件格式与旧版完全一致）
- 编译引用改为官方 NuGet 包 TShock 6.2.1
- 修复默认配置“替换文件”示例路径错误的问题：`/tshock/...` 在 Windows 下会被解析为盘符根目录导致替换失败；默认改为空配置，示例见上文说明
- 补充 1.4.5.7 新增的 2 个秘密种子（electric boogaloo、calm before the storm），随机种子池与重置前清理均已覆盖

### v2026.3.1.14
- 更改默认配置
### v2026.2.24.23
- 代码优化，新增重置完成后自动设置下一次重置种子的功能
### v2026.2.23.16
- 修改版本号的获取方式
### v2026.2.23.14
- 移除冗余代码;修改重置逻辑;修改action脚本
### v2026.2.9.10
- 修复双地牢种子错误问题
### v2026.2.9.9
- 支持多种子混合
### v2026.2.8.4
- 支持彩蛋种子并完善文档

### v2024.6.23.0

- 移除CaiAPI重置提示
- 重置后清除世界事件

### v2024.12.8.1

- 修复配置文件位置错误
- 在世界名称显示进度

### v2024.9.1

- 添加英文翻译
- 添加`/resetdata`以重置数据不生成地图

### v2024.8.24

- 尝试完善卸载函数