# AutoResetPlus (Enhanced World Reset Plugin)

- Authors: Eustia & cc04 & Leader & 棱镜 & Cai & 肝帝熙恩 & 星梦 (Stardream)
- Enhanced world-reset plugin with customizable reset content
- Compatibility: TShock 6.2.1 / Terraria 1.4.5.8

## Commands

| Syntax | Permission | Description |
| ---- | :----: | ---- |
| `/reset` | reset.admin | Reset the world; uses a random seed when no seed is configured |
| `/resetdata` | reset.admin | Reset data only (without generating a new map) |
| `/rs info` | reset.admin | Show the current preset |
| `/rs name <map name>` | reset.admin | Set the map name after reset; without an argument, it follows the original world name |
| `/rs seed <world seed>\|<secret world seed>` | reset.admin | Set the seed used by the reset; split multiple seeds with `\|`, e.g. `rs 114514\|truck stop\|mole people`. A secret seed can also be used directly: `rs truck stop` |

## Configuration

> Config path: `tshock/AutoResetPlus/AutoReset.<language>.json` (usually `AutoReset.zh-Hans.json` on Chinese servers)
> Replace folder: `tshock/AutoResetPlus/ReplaceFiles`

> [!NOTE]
> Property names in the config file are localized to the server language (the sample below shows the `zh-Hans` file).

```json5
{
  "替换文件": {},                       // Files to replace: target path -> source file name in ReplaceFiles
  "击杀重置": {                         // Kill-triggered reset
    "击杀重置开关": false,              // Enable kill-triggered reset
    "已击杀次数": 0,                    // Kills so far
    "生物ID": 50,                       // NPC id to count
    "需要击杀次数": 50                  // Kills required to auto reset
  },
  "重置后指令": [],                     // Commands to run after the reset
  "重置前指令": [],                     // Commands to run before the reset
  "重置后SQL命令": [
    "DELETE FROM tsCharacter"
  ],
  "地图预设": {                         // Map preset
    "地图名": null,                     // Map name (null = follow the original world name)
    "地图种子": null                    // Map seed
  },
  "随机种子配置": {                     // Random seed config (for the NEXT reset)
    "开启重置后自动设置种子": false,    // Enable auto-picking the next seed after a reset
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

### ReplaceFiles

- The folder `tshock/AutoResetPlus/ReplaceFiles` is created on startup.
- "Files to replace" defaults to an empty `{}`; fill it as needed: the left side is the target file path, the right side is the source file name inside the ReplaceFiles folder.
- The target path is resolved relative to the server working directory (e.g. `tshock/原神.json` means `tshock\原神.json` under the server directory); an absolute path also works. **Do not start it with `/`** — on Windows `/tshock/...` is resolved from the drive root as `C:\tshock\...`, which breaks the replacement.
- When the right side is a non-empty string, the source file is copied over the target path after a reset; an empty string `""` means "delete the target file".
- For copying, put the source file into `tshock/AutoResetPlus/ReplaceFiles/` first, otherwise the reset reports a failed replacement.
- Examples:
  - `"tshock/原神.json": "原神.json"` → overwrites `tshock/原神.json` with `tshock/AutoResetPlus/ReplaceFiles/原神.json`
  - `"tshock/XSB数据缓存.json": ""` → deletes `tshock/XSB数据缓存.json`

### Kill-triggered auto reset

- Disabled by default: the `击杀重置开关` option is `false` (check with `/rs info`).
- When enabled: once the kill count reaches the required number and the NPC id matches, `/reset` is executed automatically.
- Manual `/reset` is always available; the automatic reset only happens when kill-triggered reset is enabled and its condition is met.

### Random seed config

- Disabled by default; enable it manually.
- It does NOT affect the seed of the current reset — it only prepares the seed for the NEXT reset.
- When enabled, after a reset finishes a seed is picked randomly from the configured list, so the next reset can use it right away.

## Changelog

### v2026.10.2.0

- Adapted to TShock 6.2.1 / Terraria 1.4.5.8
- Removed the LazyAPI dependency; replaced by a built-in config base class (config file format unchanged)
- Build reference switched to the official NuGet package TShock 6.2.1
- Fixed the wrong example path in the default "Files to replace" config: `/tshock/...` is resolved from the drive root on Windows and breaks the replacement; the default is now an empty config with examples in the docs above
- Added the 2 new secret seeds from 1.4.5.7 (electric boogaloo, calm before the storm) to both the random seed pool and the pre-reset cleanup

### v2026.3.1.14

- Changed default config

### v2026.2.24.23

- Code cleanup; added auto-picking the next reset seed after a reset finishes

### v2026.2.23.16

- Changed how the version number is obtained

### v2026.2.23.14

- Removed redundant code; reworked reset logic; changed the action script

### v2026.2.9.10

- Fixed the double-dungeon seed bug

### v2026.2.9.9

- Support mixing multiple seeds

### v2026.2.8.4

- Support secret/egg seeds and improved the docs

### v2024.6.23.0

- Removed the CaiAPI reset notice
- World events are cleared after a reset

### v2024.12.8.1

- Fixed the config file location
- Show progress in the world name

### v2024.9.1

- Added English translation
- Added `/resetdata` to reset data without generating a map

### v2024.8.24

- Attempted to improve the unload function