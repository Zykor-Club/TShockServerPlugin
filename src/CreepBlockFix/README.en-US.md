# CreepBlockFix Biome Spread Configuration Fix
- **Author**: 星梦
- **Version**: v1.0.0.0
## Overview
- Fixes the bug where `AllowCrimsonCreep`/`AllowCorruptionCreep`/`AllowHallowCreep` settings in TShock 6.1.0 / OTAPI 3.3.x don't take effect
- Intercepts the biome spread **fast path** using OTAPI's `HookEvents.Terraria.WorldGen.hardUpdateWorld` event
- Reuses the spread switches in TShock's main config `tshock/config.json`; no extra configuration is needed
## Background
TShock's `tshock/config.json` provides three biome spread control switches:
```json
{
  "AllowCrimsonCreep": false,
  "AllowCorruptionCreep": false,
  "AllowHallowCreep": false
}
```
Due to an OTAPI 3.3.x bug, `OTAPI.Hooks.WorldGen.InvokeHardmodeTileUpdate()` is defined in `Hooks.cs` but never called inside `mfwh_hardUpdateWorld` (the actual spread code). This means the `HardmodeTileUpdate` event never fires, and TShock's `OnHardUpdate` handler cannot intercept biome spread.
This plugin hooks into `HookEvents.Terraria.WorldGen.hardUpdateWorld` (which is correctly injected by OTAPI), checks the source tile type before spread execution, and blocks spread based on TShock config.

> **Dual-path blocking (v1.0.0.0)**: Biome spread works through two independent paths; blocking only one still leaks:
> - **Slow path** `SpreadGrass`: grass-to-grass tile-by-tile conversion, already handled by TShock;
> - **Fast path** `hardUpdateWorld → WorldGen.Convert`: batch-converts surrounding tiles from a spread source, missed by TShock and handled by this plugin.
> The blocking criteria directly align with `TileID.Sets.SpreadsCorruption/SpreadsCrimson/SpreadsHallow`, fully covering grass/stone/sand/thorns and other spread sources.
## Configuration
Spread switches follow TShock's main config file `tshock/config.json`:
| Setting | Type | Default | Description |
|--------|------|:------:|------|
| AllowCrimsonCreep | bool | true | Allow crimson to spread |
| AllowCorruptionCreep | bool | true | Allow corruption to spread |
| AllowHallowCreep | bool | true | Allow hallow to spread |
> This plugin has no standalone config file and generates none.
## Compatibility
- TShock: 6.1.0
- OTAPI: 3.3.x
- Terraria: 1.4.5.6
- .NET: 9.0
## Changelog
### v1.0.0.0
- Removed built-in debug logging and standalone config; pure blocking injection that follows TShock's main config
- Align block criteria with the `TileID.Sets.Spreads*` sets, fixing missed sources (`24/201/352/110/113`) in the old `Corrupt/Crimson/Hallow` criteria
### v2026.8.29.0
- Align block criteria with the `TileID.Sets.Spreads*` sets, fixing missed sources (`24/201/352/110/113`)
- Added optional debug logging `creepblockfix.json` (DebugLog, default off)
### v2026.7.19.0
- Initial release
- Fixed AllowCrimsonCreep/AllowCorruptionCreep/AllowHallowCreep config not working
## Feedback
- Open an issue at: https://github.com/Zykor-Club/TShockServerPlugin