# HiddenPlayerPlugin
- **Author**: 星梦
- **Version**: v1.1.0

## Features
- Hide join/leave broadcast messages for specific players
- Hide hidden players from `/who`, `/playing`, `/online` commands
- Use Harmony Patch to modify TShock internal methods
- Support filtering player list in MOTD `%players%` variable
- Support filtering player count in `%onlineplayers%` variable

## Commands
| Command | Permission | Description |
|---------|:----------:|:-----------:|
| `/hideplayer add <playername>` | `hiddenplayer.admin` | Add hidden player |
| `/hideplayer remove <playername>` | `hiddenplayer.admin` | Remove hidden player |
| `/hideplayer list` | `hiddenplayer.admin` | List all hidden players |
| `/hideplayer reload` | `hiddenplayer.admin` | Reload configuration |
| `/hideplayer save` | `hiddenplayer.admin` | Save configuration |

## Configuration
Config path: `tshock/HiddenPlayerConfig.json`
```json
{
  "HiddenPlayerNames": [
    "Player1",
    "Player2"
  ]
}
```

## Permissions
| Permission | Description |
|------------|-------------|
| `hiddenplayer.admin` | Manage hidden player list |

## Version History
### v1.1.0
- Use Harmony Patch to modify TShock internal methods
- Support filtering player list in MOTD `%players%` variable
- Support filtering `/who`, `/playing`, `/online` commands
- Support filtering `%onlineplayers%` variable
- Remove permission system, all players cannot see hidden players

### v1.0.0
- Initial release
- Support hiding player join/leave broadcasts
