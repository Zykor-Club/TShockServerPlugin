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
但由于 OTAPI 3.3.x 的 Bug，`OTAPI.Hooks.WorldGen.InvokeHardmodeTileUpdate()` 方法虽然在 `Hooks.cs` 中定义了，
但在 `mfwh_hardUpdateWorld`（实际的蔓延执行代码）中从未被调用，
导致 `HardmodeTileUpdate` 事件永远不触发，TShock 的 `OnHardUpdate` 无法拦截蔓延。
本插件通过 Hook `HookEvents.Terraria.WorldGen.hardUpdateWorld` 事件（该事件在 OTAPI 中被正确注入），
在蔓延执行前检查源图格类型，根据 TShock 配置决定是否阻止蔓延。

> **双路径封堵（v1.0.0.0）**：感染蔓延存在两条独立路径，仅拦一条仍会漏：
> - **慢路径** `SpreadGrass`：草→草地逐格转化，TShock 已拦截；
> - **快路径** `hardUpdateWorld → WorldGen.Convert`：以蔓延源格为中心批量转化邻块，TShock 漏挂、本插件补挂。
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