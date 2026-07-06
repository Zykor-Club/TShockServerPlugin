# PluginLoader 热加载插件管理

- **作者**: 淦,星梦

## 功能
- 动态加载/卸载/重载插件（无需重启服务器）
- 文件监控：HotPlugins 文件夹放 DLL 自动加载
- 云端下载：从插件库安装插件
- 自动更新：从 Zykor-Club/TShockServerPlugin 仓库检查并更新插件

## 命令

| 语法 | 权限 | 说明 |
|------|:----:|:-----|
| `/pl list` | `pluginloader.manage` | 列出已加载的插件 |
| `/pl install <url>` | `pluginloader.manage` | 从 URL 安装插件 |
| `/pl delete <插件名>` | `pluginloader.manage` | 删除插件 |
| `/pl reload <插件名>` | `pluginloader.manage` | 重载插件 |
| `/pl reloadall` | `pluginloader.manage` | 重载所有插件 |
| `/pl enable <插件名>` | `pluginloader.manage` | 启用插件 |
| `/pl disable <插件名>` | `pluginloader.manage` | 禁用插件 |
| `/pl enableall` | `pluginloader.manage` | 启用所有插件 |
| `/pl disableall` | `pluginloader.manage` | 禁用所有插件 |
| `/pl autore` | `pluginloader.manage` | 切换自动重载 |
| `/pl cloud [页数]` | `pluginloader.manage` | 浏览云端插件列表 |
| `/pl search <关键词>` | `pluginloader.manage` | 搜索云端插件 |
| `/plupdate` | `pluginloader.update` | 从仓库检查并更新所有插件 |

## 使用说明

1. 将 DLL 放入 `ServerPlugins` 文件夹
2. 重启服务器或使用 `/reload` 命令
3. 将其他插件 DLL 放入 `HotPlugins` 文件夹即可动态加载
4. 使用 `/plupdate` 检查并安装来自仓库的最新插件
