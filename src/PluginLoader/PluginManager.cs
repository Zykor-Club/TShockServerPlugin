using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace PluginLoader;

internal class PluginManager
{
    private readonly Main _game;
    private readonly string _tshockRoot;
    private readonly string _hotPluginsFolder;
    private readonly string _serverPluginsFolder;
    private readonly string _configFile;
    private readonly ConcurrentDictionary<string, Models.LoadedPlugin> _loadedPlugins = new();
    private Models.PluginConfig _config = new();

    private FileSystemWatcher? _watcher;
    private readonly object _watcherLock = new();
    private readonly HashSet<string> _pendingEvents = new();
    private readonly PluginDownloader _downloader;
    private readonly PluginDependencyResolver _dependencyResolver;

    public PluginManager(Main game)
    {
        _game = game;
        _tshockRoot = AppContext.BaseDirectory;
        _hotPluginsFolder = Path.Combine(_tshockRoot, "HotPlugins");
        _serverPluginsFolder = Path.Combine(_tshockRoot, "ServerPlugins");
        _configFile = Path.Combine(TShock.SavePath, "PluginLoader.json");
        Directory.CreateDirectory(_hotPluginsFolder);
        LoadConfig();
        _downloader = new PluginDownloader(_config.GitHubToken);
        _dependencyResolver = new PluginDependencyResolver();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFile))
            {
                var json = File.ReadAllText(_configFile);
                _config = JsonSerializer.Deserialize<Models.PluginConfig>(json) ?? new Models.PluginConfig();
            }
        }
        catch { }
    }

    private void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config);
            File.WriteAllText(_configFile, json);
        }
        catch { }
    }

    public void StartWatching()
    {
        if (_watcher != null) return;

        Directory.CreateDirectory(_hotPluginsFolder);
        _watcher = new FileSystemWatcher(_hotPluginsFolder, "*.dll")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnDllChanged;
        _watcher.Changed += OnDllChanged;
        _watcher.Deleted += OnDllDeleted;
        _watcher.Renamed += OnDllRenamed;

        TShock.Log.ConsoleInfo($"[PluginLoader] 已监视文件夹: {_hotPluginsFolder}");
        TShock.Log.ConsoleInfo($"[PluginLoader] 自动重载: {(_config.AutoReload ? "开启" : "关闭")}");
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private async void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        if (!_config.AutoReload) return;

        lock (_watcherLock)
        {
            if (!_pendingEvents.Add(e.FullPath))
                return;
        }

        try
        {
            await Task.Delay(1000);
            lock (_watcherLock) _pendingEvents.Remove(e.FullPath);

            if (!File.Exists(e.FullPath)) return;

            TShock.Log.ConsoleInfo($"[PluginLoader] 检测到DLL变化: {e.Name}");

            var existing = _loadedPlugins.Values.FirstOrDefault(p => string.Equals(p.AssemblyPath, e.FullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null && existing.Plugin != null)
                ReloadPlugin(existing.Plugin.Name, TSPlayer.Server);
            else
            {
                LoadPlugin(e.FullPath, TSPlayer.Server);
                var loaded = _loadedPlugins.Values.FirstOrDefault(p => string.Equals(p.AssemblyPath, e.FullPath, StringComparison.OrdinalIgnoreCase));
                if (loaded != null && !_config.DisabledPlugins.Contains(loaded.AssemblyName))
                {
                    loaded.Enabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 处理DLL变化时出错: {ex}");
            lock (_watcherLock) _pendingEvents.Remove(e.FullPath);
        }
    }

    private void OnDllDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_config.AutoReload) return;

        var existing = _loadedPlugins.Values.FirstOrDefault(p => string.Equals(p.AssemblyPath, e.FullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.Plugin != null)
        {
            string pluginName = existing.Plugin.Name;
            UnloadPluginInternal(pluginName);
            TShock.Log.ConsoleInfo($"[PluginLoader] DLL被删除，自动卸载: {pluginName}");
        }
    }

    private void OnDllRenamed(object sender, RenamedEventArgs e)
    {
        OnDllDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted, e.OldFullPath, e.OldName));
        OnDllChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Created, e.FullPath, e.Name));
    }

    public void HandleCommand(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            ShowHelp(args.Player);
            return;
        }

        var subCmd = args.Parameters[0].ToLowerInvariant();
        try
        {
            switch (subCmd)
            {
                case "i":
                case "install":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    Task.Run(async () => await InstallFromUrlAsync(args.Parameters[1], args.Player));
                    break;

                case "d":
                case "del":
                case "delete":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    DeletePlugin(GetPluginName(args.Parameters[1]), args.Player);
                    break;

                case "l":
                case "list":
                    ListPlugins(args.Player);
                    break;

                case "r":
                case "reload":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    ReloadPlugin(GetPluginName(args.Parameters[1]), args.Player);
                    break;

                case "e":
                case "enable":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    EnablePlugin(GetPluginName(args.Parameters[1]), args.Player);
                    break;

                case "di":
                case "disable":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    DisablePlugin(GetPluginName(args.Parameters[1]), args.Player);
                    break;

                case "ea":
                case "enableall":
                    EnableAllPlugins(args.Player);
                    break;

                case "da":
                case "disableall":
                    DisableAllPlugins(args.Player);
                    break;

                case "ra":
                case "reloadall":
                    ReloadAllPlugins(args.Player);
                    break;

                case "a":
                case "autore":
                    ToggleAutoReload(args.Player);
                    break;

                case "inf":
                case "info":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    ShowPluginInfo(GetPluginName(args.Parameters[1]), args.Player);
                    break;

                case "c":
                case "cloud":
                    if (args.Parameters.Count > 1 && int.TryParse(args.Parameters[1], out int page))
                        _ = ShowCloudPluginsAsync(args.Player, page);
                    else
                        _ = ShowCloudPluginsAsync(args.Player, 1);
                    break;

                case "s":
                case "search":
                    if (args.Parameters.Count < 2) { ShowHelp(args.Player); return; }
                    if (int.TryParse(args.Parameters[1], out int index))
                        _ = InstallCloudPluginByIndexAsync(index, args.Player);
                    else
                        _ = SearchAndInstallCloudPluginAsync(args.Parameters[1], args.Player);
                    break;

                default:
                    args.Player.SendErrorMessage($"未知子命令: {subCmd}");
                    ShowHelp(args.Player);
                    break;
            }
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage($"命令执行失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private string GetPluginName(string param)
    {
        if (int.TryParse(param, out int index))
        {
            var name = GetPluginNameByIndex(index);
            if (name != null) return name;
        }
        return param;
    }

    private void ShowHelp(TSPlayer player)
    {
        var gold = new Color(255, 215, 0);

        player.SendMessage("======= PluginLoader 命令列表 =======", gold);
        player.SendMessage("  [i:75] 插件开发 by [c/FFD700:淦 & 星梦]", Color.LightGray);
        player.SendMessage(" ", Color.White);
        player.SendMessage("  [c/87CEEB:pl i]    [c/FFB6C1:安装插件 (URL/GitHub/本地路径)]", Color.White);
        player.SendMessage("  [c/87CEEB:pl d]    [c/FFB6C1:删除插件 (卸载并删除文件)]", Color.White);
        player.SendMessage("  [c/87CEEB:pl l]    [c/FFB6C1:列出所有动态插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl r]    [c/FFB6C1:重载指定插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl e]    [c/FFB6C1:启用指定插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl di]   [c/FFB6C1:禁用指定插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl ea]   [c/FFB6C1:启用所有插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl da]   [c/FFB6C1:禁用所有插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl ra]   [c/FFB6C1:重载所有插件]", Color.White);
        player.SendMessage("  [c/87CEEB:pl a]    [c/FFB6C1:切换自动重载开关]", Color.White);
        player.SendMessage("  [c/87CEEB:pl inf]  [c/FFB6C1:查看插件详细信息]", Color.White);
        player.SendMessage("  [c/87CEEB:pl c]    [c/FFB6C1:查看云端插件库列表 (支持翻页: /pl c 2)]", Color.White);
        player.SendMessage("  [c/87CEEB:pl s]    [c/FFB6C1:搜索插件名或输入序号安装]", Color.White);
        player.SendMessage(" ", Color.White);
        if (_config.AutoReload)
            player.SendMessage("  [i:4956] 自动重载状态: [c/90EE90:开启]", gold);
        else
            player.SendMessage("  [i:4956] 自动重载状态: [c/FF6347:关闭]", gold);
        player.SendMessage("==================================", gold);
    }

    private string? GetPluginNameByIndex(int index)
    {
        if (index < 1) return null;
        var ordered = _loadedPlugins.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (index <= ordered.Count)
            return ordered[index - 1].Key;
        return null;
    }

    public async Task InstallFromUrlAsync(string url, TSPlayer op)
    {
        try
        {
            if (url.Contains("-") && url.Split('-').Length == 2 && 
                int.TryParse(url.Split('-')[0], out int startIndex) && 
                int.TryParse(url.Split('-')[1], out int endIndex))
            {
                op.SendInfoMessage($"正在安装序号 {startIndex}-{endIndex} 的插件...");
                var manifests = await _dependencyResolver.FetchPluginManifestsAsync();
                if (manifests == null || manifests.Length == 0)
                {
                    op.SendErrorMessage("无法获取云端插件列表，请稍后重试。");
                    return;
                }
                
                if (startIndex < 1 || endIndex > manifests.Length)
                {
                    op.SendErrorMessage($"序号范围无效，范围: 1-{manifests.Length}");
                    return;
                }

                for (int i = startIndex; i <= endIndex; i++)
                {
                    var pluginInfo = manifests[i - 1];
                    op.SendInfoMessage($"正在下载: {pluginInfo.Name} v{pluginInfo.Version}");
                    var dllPath = await _dependencyResolver.DownloadPluginFromCloudAsync(pluginInfo.AssemblyName, _hotPluginsFolder);
                    if (!string.IsNullOrEmpty(dllPath))
                    {
                        op.SendSuccessMessage($"下载完成: {Path.GetFileName(dllPath)}");
                        await InstallPluginWithDependencies(dllPath, op);
                    }
                    else
                    {
                        op.SendErrorMessage($"下载失败: {pluginInfo.Name}");
                    }
                }
                return;
            }

            if (int.TryParse(url, out int singleIndex))
            {
                await InstallCloudPluginByIndexAsync(singleIndex, op);
                return;
            }

            if (File.Exists(url))
            {
                op.SendInfoMessage($"检测到本地文件: {url}");
                string localFileName = Path.GetFileName(url);
                string destPath = Path.Combine(_hotPluginsFolder, localFileName);
                File.Copy(url, destPath, true);
                op.SendSuccessMessage($"已复制本地文件到: {destPath}");
                await InstallPluginWithDependencies(destPath, op);
                return;
            }

            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                if (url.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
                {
                    await InstallFromGitHubShortUrl(url, op);
                    return;
                }
                op.SendErrorMessage($"无效的 URL 或文件路径: {url}");
                return;
            }

            Uri uri = new Uri(url);
            
            if (uri.Host.Contains("github.com"))
            {
                if (uri.AbsolutePath.Contains("/releases/tag/"))
                {
                    await InstallFromGitHubReleaseUrl(url, op);
                    return;
                }
                
                if (uri.AbsolutePath.StartsWith("/"))
                {
                    var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
                    if (pathSegments.Length >= 2 && !uri.AbsolutePath.Contains("/releases/download/"))
                    {
                        var owner = pathSegments[0];
                        var repo = pathSegments[1];
                        op.SendInfoMessage($"检测到GitHub仓库主页，正在获取 {owner}/{repo} 最新发布...");
                        await InstallFromGitHubShortUrl($"github:{owner}/{repo}/latest", op);
                        return;
                    }
                }
            }

            string finalFileName;

            if (!string.IsNullOrEmpty(uri.LocalPath))
            {
                finalFileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(finalFileName) || !finalFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    finalFileName = $"plugin_{Guid.NewGuid():N}.dll";
            }
            else
            {
                finalFileName = $"plugin_{Guid.NewGuid():N}.dll";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
                finalFileName = finalFileName.Replace(c, '_');

            string localPath = Path.Combine(_hotPluginsFolder, finalFileName);

            op.SendInfoMessage($"正在从 {url} 下载插件...");

            _downloader.DownloadProgressChanged += (sender, e) =>
            {
                op.SendInfoMessage($"下载进度: {e.Percent:F1}% ({e.ReceivedBytes / 1024:F1} KB)");
            };

            byte[] bytes = await _downloader.DownloadFromUrlAsync(url, finalFileName);
            if (bytes.Length == 0)
            {
                op.SendErrorMessage("下载的文件为空，请检查 URL 是否正确。");
                return;
            }

            await File.WriteAllBytesAsync(localPath, bytes);
            op.SendSuccessMessage($"下载完成 ({bytes.Length / 1024.0:F1} KB)");

            if (_downloader.IsValidDll(localPath))
                await InstallPluginWithDependencies(localPath, op);
            else
            {
                op.SendErrorMessage("下载的文件不是有效的 DLL，可能 URL 指向了网页或其他文件。");
                File.Delete(localPath);
            }
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"安装失败: {ex.Message}");
            TShock.Log.Error($"[PluginLoader] 安装失败: {url}\n{ex}");
        }
    }

    private async Task InstallFromGitHubReleaseUrl(string url, TSPlayer op)
    {
        try
        {
            Uri uri = new Uri(url);
            var pathSegments = uri.AbsolutePath.Split('/');
            if (pathSegments.Length < 5)
            {
                op.SendErrorMessage("GitHub Release URL格式不正确");
                return;
            }

            var owner = pathSegments[1];
            var repo = pathSegments[2];
            var tag = pathSegments[4];

            op.SendInfoMessage($"检测到GitHub Release页面，正在获取 {owner}/{repo} {tag}...");

            _downloader.DownloadProgressChanged += (sender, e) =>
            {
                op.SendInfoMessage($"下载进度: {e.Percent:F1}%");
            };

            var bytes = await _downloader.DownloadFromGitHubAsync(owner, repo, tag, _hotPluginsFolder);
            if (bytes == null)
            {
                op.SendErrorMessage("GitHub下载失败，请检查仓库是否存在或是否有权限访问。");
                return;
            }

            var fileName = $"{repo}_{tag}.dll";
            var localPath = Path.Combine(_hotPluginsFolder, fileName);
            await File.WriteAllBytesAsync(localPath, bytes);

            op.SendSuccessMessage($"下载完成 ({bytes.Length / 1024.0:F1} KB)");

            if (_downloader.IsValidDll(localPath))
                await InstallPluginWithDependencies(localPath, op);
            else
            {
                op.SendErrorMessage("下载的文件不是有效的 DLL");
                File.Delete(localPath);
            }
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"GitHub安装失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private async Task InstallFromGitHubShortUrl(string url, TSPlayer op)
    {
        try
        {
            var parts = url.Split(':');
            if (parts.Length < 2)
            {
                op.SendErrorMessage("GitHub URL格式错误，使用: github:用户名/仓库名/版本");
                return;
            }

            var repoParts = parts[1].Split('/');
            if (repoParts.Length < 2)
            {
                op.SendErrorMessage("GitHub仓库路径错误，使用: 用户名/仓库名");
                return;
            }

            var owner = repoParts[0];
            var repo = repoParts[1];
            var version = repoParts.Length > 2 ? repoParts[2] : "latest";

            op.SendInfoMessage($"正在从GitHub获取 {owner}/{repo} {version}...");

            _downloader.DownloadProgressChanged += (sender, e) =>
            {
                op.SendInfoMessage($"下载进度: {e.Percent:F1}%");
            };

            var bytes = await _downloader.DownloadFromGitHubAsync(owner, repo, version, _hotPluginsFolder);
            if (bytes == null)
            {
                op.SendInfoMessage("GitHub下载失败，尝试从云端插件库搜索...");
                var cloudPlugin = await _dependencyResolver.SearchPluginAsync(repo);
                if (cloudPlugin != null)
                {
                    op.SendSuccessMessage($"在云端插件库找到: {cloudPlugin.Name} v{cloudPlugin.Version}");
                    op.SendInfoMessage("正在从云端下载插件...");
                    var dllPath = await _dependencyResolver.DownloadPluginFromCloudAsync(cloudPlugin.AssemblyName, _hotPluginsFolder);
                    if (!string.IsNullOrEmpty(dllPath))
                    {
                        op.SendSuccessMessage($"下载完成: {Path.GetFileName(dllPath)}");
                        await InstallPluginWithDependencies(dllPath, op);
                        return;
                    }
                }
                
                op.SendErrorMessage("GitHub和云端插件库均下载失败，请检查仓库是否存在或是否有权限访问。");
                return;
            }

            var fileName = $"{repo}_{version}.dll";
            var localPath = Path.Combine(_hotPluginsFolder, fileName);
            await File.WriteAllBytesAsync(localPath, bytes);

            op.SendSuccessMessage($"下载完成 ({bytes.Length / 1024.0:F1} KB)");

            if (_downloader.IsValidDll(localPath))
                await InstallPluginWithDependencies(localPath, op);
            else
            {
                op.SendErrorMessage("下载的文件不是有效的 DLL");
                File.Delete(localPath);
            }
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"GitHub安装失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private async Task ShowCloudPluginsAsync(TSPlayer op, int page = 1)
    {
        try
        {
            op.SendInfoMessage("正在获取云端插件列表...");
            var manifests = await _dependencyResolver.FetchPluginManifestsAsync();
            
            if (manifests == null || manifests.Length == 0)
            {
                op.SendErrorMessage("无法获取云端插件列表，请稍后重试。");
                return;
            }

            const int itemsPerPage = 20;
            var totalPages = (int)Math.Ceiling((double)manifests.Length / itemsPerPage);
            
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var startIndex = (page - 1) * itemsPerPage;
            var endIndex = Math.Min(startIndex + itemsPerPage, manifests.Length);

            var gold = new Color(255, 215, 0);
            op.SendMessage($"======= 云端插件库 [{page}/{totalPages}] ({manifests.Length}个) =======", gold);
            
            for (int i = startIndex; i < endIndex; i++)
            {
                var m = manifests[i];
                var desc = m.Description.ContainsKey("zh-CN") ? m.Description["zh-CN"] :
                           m.Description.ContainsKey("en-US") ? m.Description["en-US"] :
                           m.Description.Values.FirstOrDefault() ?? "";
                op.SendInfoMessage($"{i + 1}. [c/87CEEB:{m.Name}] v{m.Version} - {desc} (by {m.Author})");
            }

            if (page > 1)
                op.SendInfoMessage($"使用 [c/90EE90:/pl c {page - 1}] 上一页");
            if (page < totalPages)
                op.SendInfoMessage($"使用 [c/90EE90:/pl c {page + 1}] 下一页");
            op.SendInfoMessage($"使用 [c/90EE90:/pl i {startIndex + 1}-{endIndex}] 安装指定插件");
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"获取云端插件列表失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private async Task SearchAndInstallCloudPluginAsync(string keyword, TSPlayer op)
    {
        try
        {
            op.SendInfoMessage($"正在搜索插件: {keyword}");
            var pluginInfo = await _dependencyResolver.SearchPluginAsync(keyword);
            
            if (pluginInfo == null)
            {
                op.SendErrorMessage($"未找到匹配的插件: {keyword}");
                op.SendInfoMessage("使用 [c/90EE90:/pl c] 查看所有云端插件");
                return;
            }

            op.SendSuccessMessage($"找到插件: {pluginInfo.Name} v{pluginInfo.Version} (by {pluginInfo.Author})");
            op.SendInfoMessage("正在从云端下载插件...");

            var dllPath = await _dependencyResolver.DownloadPluginFromCloudAsync(pluginInfo.AssemblyName, _hotPluginsFolder);
            if (string.IsNullOrEmpty(dllPath))
            {
                op.SendErrorMessage("下载失败，请稍后重试。");
                return;
            }

            op.SendSuccessMessage($"下载完成: {Path.GetFileName(dllPath)}");
            await InstallPluginWithDependencies(dllPath, op);
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"安装失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private async Task InstallCloudPluginByIndexAsync(int index, TSPlayer op)
    {
        try
        {
            op.SendInfoMessage($"正在获取插件列表...");
            var manifests = await _dependencyResolver.FetchPluginManifestsAsync();
            
            if (manifests == null || manifests.Length == 0)
            {
                op.SendErrorMessage("无法获取云端插件列表，请稍后重试。");
                return;
            }

            if (index < 1 || index > manifests.Length)
            {
                op.SendErrorMessage($"序号无效，范围: 1-{manifests.Length}");
                return;
            }

            var pluginInfo = manifests[index - 1];
            op.SendSuccessMessage($"安装插件: {pluginInfo.Name} v{pluginInfo.Version} (by {pluginInfo.Author})");
            op.SendInfoMessage("正在从云端下载插件...");

            var dllPath = await _dependencyResolver.DownloadPluginFromCloudAsync(pluginInfo.AssemblyName, _hotPluginsFolder);
            if (string.IsNullOrEmpty(dllPath))
            {
                op.SendErrorMessage("下载失败，请稍后重试。");
                return;
            }

            op.SendSuccessMessage($"下载完成: {Path.GetFileName(dllPath)}");
            await InstallPluginWithDependencies(dllPath, op);
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"安装失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private async Task InstallPluginWithDependencies(string dllPath, TSPlayer op)
    {
        op.SendInfoMessage("正在检查插件依赖...");

        var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
        var cloudDependencies = await _dependencyResolver.ResolveDependenciesAsync(assemblyName);

        if (cloudDependencies.Count > 0)
        {
            op.SendInfoMessage($"发现 {cloudDependencies.Count} 个云端依赖，正在下载...");
            var downloaded = await _dependencyResolver.DownloadDependenciesAsync(cloudDependencies, _serverPluginsFolder);
            op.SendSuccessMessage($"已下载 {downloaded.Count}/{cloudDependencies.Count} 个依赖");
        }

        var missingDependencies = await _dependencyResolver.ScanMissingDependenciesAsync(dllPath, _hotPluginsFolder);
        if (missingDependencies.Count > 0)
        {
            op.SendInfoMessage($"发现 {missingDependencies.Count} 个缺失依赖，正在处理...");
            
            foreach (var dep in missingDependencies)
            {
                var cloudPlugin = await _dependencyResolver.SearchPluginAsync(dep);
                if (cloudPlugin != null)
                {
                    op.SendInfoMessage($"在云端插件库找到依赖: {cloudPlugin.Name}");
                    var result = await _dependencyResolver.DownloadPluginFromCloudAsync(cloudPlugin.AssemblyName, _serverPluginsFolder);
                    if (!string.IsNullOrEmpty(result))
                    {
                        op.SendSuccessMessage($"已下载依赖: {dep}");
                    }
                    else
                    {
                        op.SendErrorMessage($"无法下载云端依赖: {dep}");
                    }
                }
                else
                {
                    op.SendInfoMessage($"{dep} 不是插件，尝试从NuGet下载...");
                    var result = await _dependencyResolver.DownloadNuGetPackageAsync(dep, _serverPluginsFolder);
                    if (!string.IsNullOrEmpty(result))
                    {
                        op.SendSuccessMessage($"已下载依赖: {dep}");
                    }
                    else
                    {
                        op.SendErrorMessage($"无法下载依赖: {dep}，请手动安装");
                    }
                }
            }
        }

        LoadPlugin(dllPath, op);
    }

    public void LoadPlugin(string assemblyPath, TSPlayer op)
    {
        assemblyPath = Path.GetFullPath(assemblyPath);
        
        try
        {
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            Assembly assembly = Assembly.Load(assemblyBytes);

            Type? pluginType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsSubclassOf(typeof(TerrariaPlugin)) &&
                                     t.GetCustomAttribute<ApiVersionAttribute>() != null);
            if (pluginType == null)
            {
                op.SendErrorMessage("该 DLL 不包含有效的 TShock 插件。");
                return;
            }

            TerrariaPlugin? plugin = (TerrariaPlugin?)Activator.CreateInstance(pluginType, _game);
            if (plugin == null)
            {
                op.SendErrorMessage("无法创建插件实例。");
                return;
            }

            string assemblyName = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(assemblyPath);
            bool shouldEnable = !_config.DisabledPlugins.Contains(assemblyName);

            plugin.Initialize();

            List<Command> commands = CapturePluginCommands(assembly);

            Models.LoadedPlugin loaded = new Models.LoadedPlugin
            {
                AssemblyPath = assemblyPath,
                AssemblyName = assemblyName,
                Plugin = plugin,
                Commands = commands,
                Enabled = shouldEnable,
                LoadedTime = DateTime.UtcNow
            };

            if (!shouldEnable)
            {
                plugin.Dispose();
                foreach (Command cmd in commands)
                    Commands.ChatCommands.Remove(cmd);
            }

            _loadedPlugins[plugin.Name] = loaded;

            op.SendSuccessMessage($"[i:29] 插件 [{plugin.Name} v{plugin.Version}] 已动态加载！({(shouldEnable ? "已启用" : "已禁用")})");
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"加载失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    private void UnloadPluginInternal(string pluginName)
    {
        if (!_loadedPlugins.TryRemove(pluginName, out Models.LoadedPlugin? loaded) || loaded == null)
            return;

        try
        {
            if (loaded.Plugin != null)
                loaded.Plugin.Dispose();
            foreach (Command cmd in loaded.Commands)
                Commands.ChatCommands.Remove(cmd);

            loaded.Plugin = null;
            loaded.Commands.Clear();
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 卸载插件 [{pluginName}] 失败: {ex}");
        }
    }

    public void DeletePlugin(string pluginName, TSPlayer op)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? loaded) || loaded == null)
        {
            op.SendErrorMessage($"未找到名为 '{pluginName}' 的动态插件。");
            return;
        }

        string assemblyPath = loaded.AssemblyPath;
        string assemblyName = loaded.AssemblyName;

        try
        {
            UnloadPluginInternal(pluginName);

            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
                op.SendSuccessMessage($"[i:29] 插件 [{pluginName}] 已删除（文件已移除）");
            }
            else
            {
                op.SendSuccessMessage($"[i:29] 插件 [{pluginName}] 已卸载（文件不存在）");
            }

            _config.DisabledPlugins.Remove(assemblyName);
            SaveConfig();
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"删除失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    public void EnablePlugin(string pluginName, TSPlayer op)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? loaded) || loaded == null)
        {
            op.SendErrorMessage($"未找到名为 '{pluginName}' 的动态插件。");
            return;
        }

        if (loaded.Enabled)
        {
            op.SendInfoMessage($"插件 [{pluginName}] 已经是启用状态。");
            return;
        }

        try
        {
            string path = loaded.AssemblyPath;
            byte[] assemblyBytes = File.ReadAllBytes(path);
            Assembly assembly = Assembly.Load(assemblyBytes);

            Type? pluginType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsSubclassOf(typeof(TerrariaPlugin)) &&
                                     t.GetCustomAttribute<ApiVersionAttribute>() != null);
            if (pluginType == null)
            {
                op.SendErrorMessage("该 DLL 不包含有效的 TShock 插件。");
                return;
            }

            TerrariaPlugin? plugin = (TerrariaPlugin?)Activator.CreateInstance(pluginType, _game);
            if (plugin == null)
            {
                op.SendErrorMessage("无法创建插件实例。");
                return;
            }

            plugin.Initialize();

            List<Command> commands = CapturePluginCommands(assembly);

            loaded.Plugin = plugin;
            loaded.Commands = commands;
            loaded.Enabled = true;
            _config.DisabledPlugins.Remove(loaded.AssemblyName);
            SaveConfig();

            op.SendSuccessMessage($"[i:29] 插件 [{pluginName}] 已启用");
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"启用失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    public void DisablePlugin(string pluginName, TSPlayer op)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? loaded) || loaded == null)
        {
            op.SendErrorMessage($"未找到名为 '{pluginName}' 的动态插件。");
            return;
        }

        if (!loaded.Enabled)
        {
            op.SendInfoMessage($"插件 [{pluginName}] 已经是禁用状态。");
            return;
        }

        try
        {
            if (loaded.Plugin != null)
                loaded.Plugin.Dispose();

            foreach (Command cmd in loaded.Commands)
                Commands.ChatCommands.Remove(cmd);

            loaded.Plugin = null;
            loaded.Commands.Clear();
            loaded.Enabled = false;
            if (!_config.DisabledPlugins.Contains(loaded.AssemblyName))
                _config.DisabledPlugins.Add(loaded.AssemblyName);
            SaveConfig();

            op.SendSuccessMessage($"[i:267] 插件 [{pluginName}] 已禁用");
        }
        catch (Exception ex)
        {
            op.SendErrorMessage($"禁用失败: {ex.Message}");
            TShock.Log.Error(ex.ToString());
        }
    }

    public void EnableAllPlugins(TSPlayer op)
    {
        int count = 0;
        foreach (var kv in _loadedPlugins.ToList())
        {
            if (!kv.Value.Enabled)
            {
                EnablePlugin(kv.Key, op);
                count++;
            }
        }
        op.SendInfoMessage($"已启用 {count} 个插件。");
    }

    public void DisableAllPlugins(TSPlayer op)
    {
        int count = 0;
        foreach (var kv in _loadedPlugins.ToList())
        {
            if (kv.Value.Enabled)
            {
                DisablePlugin(kv.Key, op);
                count++;
            }
        }
        op.SendInfoMessage($"已禁用 {count} 个插件。");
    }

    public void ReloadPlugin(string pluginName, TSPlayer op)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? existing) || existing == null)
        {
            op.SendErrorMessage($"未找到名为 '{pluginName}' 的动态插件。");
            return;
        }

        string path = existing.AssemblyPath;
        bool wasEnabled = existing.Enabled;
        op.SendInfoMessage($"正在重载插件 [{pluginName}]...");
        UnloadPluginInternal(pluginName);
        LoadPlugin(path, op);

        if (!wasEnabled && _loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? reloaded) && reloaded != null)
        {
            DisablePlugin(pluginName, op);
        }
    }

    public void ReloadAllPlugins(TSPlayer op)
    {
        var plugins = _loadedPlugins.Keys.ToList();
        op.SendInfoMessage($"正在重载 {plugins.Count} 个插件...");
        foreach (string name in plugins)
        {
            if (_loadedPlugins.TryGetValue(name, out Models.LoadedPlugin? loaded) && loaded != null)
            {
                ReloadPlugin(name, op);
            }
        }
        op.SendSuccessMessage("[i:29] 所有插件重载完成");
    }

    private void ToggleAutoReload(TSPlayer op)
    {
        _config.AutoReload = !_config.AutoReload;
        SaveConfig();
        op.SendSuccessMessage($"自动重载已{(_config.AutoReload ? "开启 [i:29]" : "关闭 [i:267]")}");
    }

    private void ShowPluginInfo(string pluginName, TSPlayer op)
    {
        if (!_loadedPlugins.TryGetValue(pluginName, out Models.LoadedPlugin? loaded) || loaded == null || loaded.Plugin == null)
        {
            op.SendErrorMessage($"未找到名为 '{pluginName}' 的动态插件。");
            return;
        }

        TerrariaPlugin p = loaded.Plugin;

        op.SendInfoMessage("═══ 插件信息 ═══");
        op.SendInfoMessage($"  名称: {p.Name}");
        op.SendInfoMessage($"  作者: {p.Author}");
        op.SendInfoMessage($"  版本: v{p.Version}");
        op.SendInfoMessage($"  描述: {p.Description}");
        op.SendInfoMessage($"  状态: {(loaded.Enabled ? "[i:29] 已启用" : "[i:267] 已禁用")}");
        op.SendInfoMessage($"  加载时间: {loaded.LoadedTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        op.SendInfoMessage($"  文件路径: {loaded.AssemblyPath}");
        op.SendInfoMessage($"");
        op.SendInfoMessage($"  注册命令 ({loaded.Commands.Count} 个):");
        if (loaded.Commands.Count == 0)
        {
            op.SendInfoMessage("    (无)");
        }
        else
        {
            foreach (Command cmd in loaded.Commands)
            {
                string names = string.Join("/", cmd.Names);
                string perm = (cmd.Permissions == null || cmd.Permissions.Count == 0) ? "(无权限要求)" : string.Join(", ", cmd.Permissions);
                op.SendInfoMessage($"    /{names}  -  权限: {perm}");
                if (!string.IsNullOrEmpty(cmd.HelpText))
                    op.SendInfoMessage($"      说明: {cmd.HelpText}");
            }
        }
        op.SendInfoMessage("═══════════════");
    }

    public void ListPlugins(TSPlayer op)
    {
        if (_loadedPlugins.IsEmpty)
        {
            op.SendInfoMessage("当前没有动态加载的插件。");
            return;
        }

        op.SendSuccessMessage("[i:57] 已加载的动态插件:");
        int index = 1;
        foreach (var kv in _loadedPlugins.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value.Plugin is TerrariaPlugin p)
            {
                string status = kv.Value.Enabled ? "[i:29]" : "[i:267]";
                op.SendInfoMessage($"  [{index}] {status} {p.Name} v{p.Version} (作者: {p.Author})");
                index++;
            }
        }
        op.SendInfoMessage($"共 {_loadedPlugins.Count} 个插件 ([i:29] 启用 / [i:267] 禁用)");
    }

    public void LoadAll()
    {
        foreach (var dll in Directory.GetFiles(_hotPluginsFolder, "*.dll"))
        {
            try
            {
                LoadPlugin(dll, TSPlayer.Server);
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"启动时加载动态插件 [{Path.GetFileName(dll)}] 失败: {ex.Message}");
            }
        }
    }

    public void UnloadAll()
    {
        foreach (string name in _loadedPlugins.Keys.ToList())
            UnloadPluginInternal(name);
    }

    private List<Command> CapturePluginCommands(Assembly pluginAssembly)
    {
        List<Command> commands = new List<Command>();
        foreach (Command cmd in Commands.ChatCommands)
        {
            if (cmd.CommandDelegate.Method?.DeclaringType?.Assembly == pluginAssembly)
                commands.Add(cmd);
        }
        return commands;
    }
}