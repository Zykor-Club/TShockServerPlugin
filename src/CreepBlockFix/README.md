# CreepBlockFix 修复生物群系蔓延配置
- **作者**: 星梦
- **版本**: v1.0.0.0
## 功能概述
- 修复 TShock 6.1.0 / OTAPI 3.3.x 中 `AllowCrimsonCreep`/`AllowCorruptionCreep`/`AllowHallowCreep` 配置不生效的 Bug
- 利用 OTAPI 的 `HookEvents.Terraria.WorldGen.hardUpdateWorld` 事件拦截生物群系**快路径**批量转化
- 复用 TShock 主配置文件 `tshock/config.json` 中的蔓延开关，无需本插件任何额外配置
## 问题背景
TShock 的 `tshock/config.json` 中提供了三个蔓延控制开关：
```json
{
  "AllowCrimsonCreep": false,
  "AllowCorruptionCreep": false,
  "AllowHallowCreep": false
}
```
但实测（移除外挂插件后，相同配置挂机对比）确认：**TShock 实际上只阻止了"邪/神草皮的蔓延"**——
即把普通泥土染成邪/神草这一步（走 `WorldGrassSpread` / `SpreadGrass` 钩子）。
对感染蔓延真正的大头——已存在的草皮、石头、沙子在 `hardUpdateWorld → WorldGen.Convert`
被批量替换成感染块——TShock 完全没有拦截。其原因：TShock 挂钩的
`OTAPI.Hooks.WorldGen.InvokeHardmodeTileUpdate()`（对应 `GameHardmodeTileUpdate`）在
OTAPI 3.3.x 中已被上游改动所移除调用点，该事件永不触发，`OnHardUpdate` 形同虚设。

因此光靠 TShock 配置，在困难模式下感染会照常扩散。本插件通过 Hook
`HookEvents.Terraria.WorldGen.hardUpdateWorld` 事件（该事件在 OTAPI 中被正确注入），
在蔓延源格处直接切断，使 `WorldGen.Convert` 不再执行，从而连同草皮/石/沙的批量转化一起封堵。

> **双钩子封堵（v1.0.0.0）**：感染蔓延存在两条路径，TShock 只拦了其中一条的"染草"步骤：
> - **SpreadGrass（TShock 已挂钩，本插件保留）**：把泥土染成邪/神草这一步；
> - **hardUpdateWorld → WorldGen.Convert（TShock 未挂钩，本插件补挂）**：蔓延源格为主循环批量转化邻近草皮/石/沙，这才是被 TShock 遗漏、真实导致"关了还在蔓延"的路径。
> 拦截判据直接对齐 `TileID.Sets.SpreadsCorruption/SpreadsCrimson/SpreadsHallow`，完整覆盖草/石/沙/棘等全部蔓延源。
## 配置
蔓延开关沿用 TShock 主配置文件 `tshock/config.json`：
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|:------:|------|
| AllowCrimsonCreep | bool | true | 是否允许猩红蔓延 |
| AllowCorruptionCreep | bool | true | 是否允许腐化蔓延 |
| AllowHallowCreep | bool | true | 是否允许神圣蔓延 |
> 本插件无独立配置文件，也不生成任何配置。
## 兼容性
- TShock 版本: 6.1.0
- OTAPI: 3.3.x
- Terraria: 1.4.5.6
- .NET: 9.0
## 插件版本
### v1.0.0.0
- 移除内置调试日志与独立配置文件，纯拦截注入，行为跟随 TShock 主配置
- 拦截判据对齐 `TileID.Sets.Spreads*` 集合，修正旧判据漏判 `24/201/352/110/113` 等蔓延源导致的"部分方向仍在蔓延"
### v2026.8.29.0
- 拦截判据对齐 `TileID.Sets.Spreads*` 集合，修正旧判据漏判 `24/201/352/110/113` 等蔓延源导致的"部分方向仍在蔓延"
- 新增可选调试日志 `creepblockfix.json`（DebugLog，默认关闭），用于实测确认钩子触发与拦截行为
### v2026.7.19.0
- 初始版本发布
- 修复 AllowCrimsonCreep/AllowCorruptionCreep/AllowHallowCreep 配置不生效的问题
## 反馈
- 优先发 issue -> 星梦的插件库：https://github.com/Zykor-Club/TShockServerPlugin
- 次优先：TShock官方群：816771079