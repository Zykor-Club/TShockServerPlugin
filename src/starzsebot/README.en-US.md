# starZSEbot

**Author**:

- Main developer: 星梦 (Stardream)

- **Version**: v2026.7.7.1

> [!IMPORTANT]
> This is the server-side adapter plugin for the QQ bot [ZSE-StarBot](https://github.com/Zykor-Club/ZSE-StarBot).
> It must be used together with the bot server and cannot work standalone. Fill in the bot server address in the config and complete the binding before use.

> [!IMPORTANT]
> This plugin depends on `linq2db` (local storage for statistics). Put `linq2db.dll` in the `ServerPlugins` folder as well.
> The following plugins are **optional** dependencies detected via runtime reflection; related features degrade gracefully when they are not installed:
> - `GenerateMap`: map packets are unavailable without it
> - `Economics.Core` / `Economics.RPG` / `Economics.Skill`: look-bag results will not include currency / level / skill info
> - `BossLock` / `ProgressControls`: progress packets will not include locked-boss info
> - `AutoResetPlus`: without it, the in-group "reset / seed vote (random generation)" commands are unavailable (they return a not-installed notice); archive export is unaffected

## Features

- WebSocket long connection with the QQ bot (TLS supported): bind-code binding, unbind, auto reconnect, heartbeat
- Whitelist check with register/login bypass: player name, device UUID and device platform (PC / PE, etc.) are reported to the bot, which decides whether to allow or kick; when accepted the plugin takes over login directly (auto register / login, no more /register /login prompts); unbound / unauthorized device / frozen accounts are kicked (when login confirmation is needed, the player is told to @ the bot with "登录" in the group)
- Look bag: reads the live character for online players, or the TShock SSC save for offline players; includes health / mana / quests / buffs / item prefixes
- World map: generates a map image, a map file via GenerateMap, or sends the world file
- Progress: world progress, boss kill counts, BossLock / ProgressControls lock state, world icon
- Boss first-kill notification: pushes a `progress_notify` packet (boss / players / time / world) when a boss is defeated for the first time; re-arms automatically after a world reset and never back-fills historical kills
- Online player list with count, slots and world progress
- Remote command execution with output returned to the bot
- Server broadcast from the bot (green text)
- Rankings: boss kills / deaths / online time / fishing quests / currency
- Economy info: currency / level / skill of Economics in look-bag results
- Shop & mail: unlock condition checks, delivering purchased items or commands by mail
- Self kick: force a player offline from the bot side
- Seed-vote & reset bridge: lets the bot read world-seed config, write a seed, and trigger a reset (runtime reflection bridge to AutoResetPlus); exports all account characters and the current world save and returns them as a package

## In-game commands (`/starzsebot` or `/zse`)

| Command | Permission | Description |
| ---- | ---- | ---- |
| `/starzsebot debug` | starzsebot.admin | Toggle debug mode |
| `/starzsebot code` | starzsebot.admin | Generate the server bind code |
| `/starzsebot info` | starzsebot.admin | Show plugin status (version / WebSocket / binding / optional deps) |
| `/starzsebot unbind` | starzsebot.admin | Unbind from the bot |
| `/starzsebot whitelist` | starzsebot.admin | Toggle whitelist check |
| `/starzsebot group <group id>` | starzsebot.admin | Set the QQ group id shown in kick messages |
| `/starzsebot reset` | starzsebot.admin | Reset statistics |
| `/starzsebot test` | starzsebot.admin | Reserved test command |

## Configuration

Path: `tshock/starZSEbot.json` (generated automatically on first start)

| Key | Type | Default | Description |
| ---- | ---- | ---- | ---- |
| 白名单开关 | bool | true | Enable whitelist check on join |
| 服务器地址 | string | api.terraria.ink:22338 | Bot server address (host:port) |
| 启用TLS | bool | true | Use https / wss for the bot server |
| 密钥 | string | (empty) | Assigned automatically after binding |
| 群OpenID | string | 114514 | Assigned automatically after binding |
| 在线显示进度 | bool | true | Append world progress text to player list packets |
| 商店分组标签 | string | 生存服 | Shop tag used to filter goods |
| 白名单拦截提示的群号 | long | 0 | QQ group id shown to kicked players (0 = hide) |

## Binding flow

1. Put `starZSEbot.dll` (and `linq2db.dll`) into `ServerPlugins` and restart the server
2. The console prints a 6-digit bind code: `[starZSEbot]您的服务器绑定码为: xxxxxx` (use `/zse code` to regenerate)
3. Register the server address/port with the bind code on the bot side; the bot assigns an access token
4. The plugin requests `{http(s)}://{address}/server/token/{code}` for the token, then connects to `{ws(s)}://{address}/server/ws/{group_open_id}/tshock/`
5. The `hello` packet reports server name, game version, TShock version, plugin version and whitelist state

Use `/zse unbind` to switch to another bot later.

## Data packets

| Type | Direction | Description |
| ---- | ---- | ---- |
| hello | Plugin → Bot | Reports server name / game version / TShock version / plugin version / whitelist state |
| heartbeat | Plugin → Bot | Heartbeat keep-alive |
| whitelist | Both | Join whitelist check: the plugin reports player, UUID and device platform, the bot replies with the verdict |
| look_bag | Both | Look bag (live read / offline SSC); the reply contains items / buffs / prefixes / economy data |
| player_list | Both | Online player list and count |
| progress | Both | World progress, boss kill counts, progress locks, world icon |
| progress_notify | Plugin → Bot | Boss first-kill notification: `boss_key` / `players` (union of killer names) / `kill_time` / `world_name`; re-arms after a world reset and never back-fills historical kills |
| map_image / map_file | Both | Map image / map file (requires GenerateMap) |
| world_file | Both | World file transfer |
| call_command | Both | Remote command execution with output returned |
| say | Bot → Plugin | Server broadcast (green text) |
| rank_data | Both | Rankings (kills / deaths / online / fishing / currency) |
| shop_condition / shop_buy | Both | Shop unlock condition check / purchase delivery |
| plugin_list | Both | Server plugin list |
| self_kick | Bot → Plugin | Force a player offline |
| unbind_server | Bot → Plugin | Unbind and regenerate the bind code |
| auto_reset | Both | AutoResetPlus bridge: `is_request=true`, `payload.action` ∈ get_config / set_seed / do_reset. get_config replies `installed` / `world_name` / `current_seed` / `random_enable` / `seed_list` / `min` / `max` / `online_minutes` (per-account online minutes from zse_statistic); set_seed writes the `\|`-joined seed combination into the AutoResetPlus preset and saves it, replying `ok`; do_reset triggers the reset flow and returns the failure reason when the state is not Available; replies "未检测到 AutoResetPlus 插件" (AutoResetPlus not detected) when the plugin is missing |
| archive_export | Both | Archive export: exports all tsCharacter accounts as `.plr` (online players use TPlayer, offline players are rebuilt) and copies the current `.wld`, packs them into a zip with `SmallestSize`, keeps it locally under `tshock/starZSEbot/Exports/`, and replies `{name, base64}` (base64 of the gzipped zip); independent of AutoResetPlus and works without it |

> `auto_reset` is implemented by `Common/AutoResetSupport.cs`, accessing AutoResetPlus at runtime via reflection (degrades to `installed=false` without throwing when it is not installed); `archive_export` is implemented by `Common/ArchiveExport.cs` and does not depend on AutoResetPlus; whitelist register/login bypass and device platform detection are implemented by `Common/LoginHelper.cs` (read-only MessageBuffer hook + NetGetData handshake interception) and `Common/PlatformTracker.cs`; first-kill notifications are implemented by `Common/ProgressNotify.cs`.

## Compatibility

- Target framework: .NET 9.0 (net9.0)
- Built for: TShock 6.2.1 / Terraria 1.4.5.8
- All optional plugin integrations are done via runtime reflection; their assemblies are not referenced at compile time

## Feedback

- Open an issue -> <https://github.com/Zykor-Club/TShockServerPlugin>
- Bot source -> <https://github.com/Zykor-Club/ZSE-StarBot>