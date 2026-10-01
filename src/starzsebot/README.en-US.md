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

## Features

- WebSocket long connection with the QQ bot (TLS supported): bind-code binding, unbind, auto reconnect, heartbeat
- Whitelist check on join: player name and device UUID are reported to the bot, which decides whether to allow or kick (unbound / unauthorized device / frozen account)
- Look bag: reads the live character for online players, or the TShock SSC save for offline players; includes health / mana / quests / buffs / item prefixes
- World map: generates a map image, a map file via GenerateMap, or sends the world file
- Progress: world progress, boss kill counts, BossLock / ProgressControls lock state, world icon
- Online player list with count, slots and world progress
- Remote command execution with output returned to the bot
- Server broadcast from the bot (green text)
- Rankings: boss kills / deaths / online time / fishing quests / currency
- Economy info: currency / level / skill of Economics in look-bag results
- Shop & mail: unlock condition checks, delivering purchased items or commands by mail
- Self kick: force a player offline from the bot side

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

## Compatibility

- Target framework: .NET 9.0 (net9.0)
- Built for: TShock 6.2.1 / Terraria 1.4.5.8
- All optional plugin integrations are done via runtime reflection; their assemblies are not referenced at compile time

## Feedback

- Open an issue -> <https://github.com/Zykor-Club/TShockServerPlugin>
- Bot source -> <https://github.com/Zykor-Club/ZSE-StarBot>