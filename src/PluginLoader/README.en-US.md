# PluginLoader - Hot-reload Plugin Manager

- **Authors**: Gan, 星梦

## Features
- Dynamic load/unload/reload plugins (no server restart needed)
- File watcher: auto-load DLLs from HotPlugins folder
- Cloud download: install plugins from repository
- Auto-update: check and update plugins from Zykor-Club/TShockServerPlugin repository

## Commands

| Syntax | Permission | Description |
|--------|:---------:|:-----------|
| `/pl list` | `pluginloader.manage` | List loaded plugins |
| `/pl install <url>` | `pluginloader.manage` | Install plugin from URL |
| `/pl delete <name>` | `pluginloader.manage` | Delete plugin |
| `/pl reload <name>` | `pluginloader.manage` | Reload plugin |
| `/pl reloadall` | `pluginloader.manage` | Reload all plugins |
| `/pl enable <name>` | `pluginloader.manage` | Enable plugin |
| `/pl disable <name>` | `pluginloader.manage` | Disable plugin |
| `/plupdate` | `pluginloader.update` | Check and update all plugins from repo |

## Usage

1. Put the DLL into `ServerPlugins` folder
2. Restart server or use `/reload` command
3. Put other plugin DLLs into `HotPlugins` folder for dynamic loading
4. Use `/plupdate` to check and install latest plugins from repository
