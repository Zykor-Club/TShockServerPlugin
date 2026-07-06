using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using TShockAPI;

namespace PluginLoader;

public class PluginDependencyResolver
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl = "http://api.terraria.ink:11434";
    private readonly Dictionary<string, Models.PluginVersionInfo> _manifestCache = new();

    public PluginDependencyResolver()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<Models.PluginVersionInfo[]?> FetchPluginManifestsAsync()
    {
        try
        {
            var url = $"{_apiBaseUrl}/plugin/get_plugin_list?tshock_version={TShock.VersionNum}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var manifests = JsonSerializer.Deserialize<Models.PluginVersionInfo[]>(json);

            if (manifests != null)
            {
                _manifestCache.Clear();
                foreach (var manifest in manifests)
                {
                    _manifestCache[manifest.AssemblyName] = manifest;
                }
            }

            return manifests;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 获取插件清单失败: {ex.Message}");
            return null;
        }
    }

    public async Task<List<string>> ResolveDependenciesAsync(string assemblyName)
    {
        var dependencies = new HashSet<string>();
        var visited = new HashSet<string>();

        await ResolveDependenciesRecursive(assemblyName, dependencies, visited);

        return dependencies.ToList();
    }

    private async Task ResolveDependenciesRecursive(string assemblyName, HashSet<string> dependencies, HashSet<string> visited)
    {
        if (visited.Contains(assemblyName))
            return;

        visited.Add(assemblyName);

        if (!_manifestCache.TryGetValue(assemblyName, out var manifest))
        {
            await FetchPluginManifestsAsync();
            if (!_manifestCache.TryGetValue(assemblyName, out manifest))
                return;
        }

        foreach (var dep in manifest.Dependencies)
        {
            if (!string.IsNullOrEmpty(dep) && !dependencies.Contains(dep))
            {
                dependencies.Add(dep);
                await ResolveDependenciesRecursive(dep, dependencies, visited);
            }
        }
    }

    public async Task<List<string>> DownloadDependenciesAsync(List<string> assemblyNames, string targetFolder)
    {
        var downloaded = new List<string>();

        foreach (var name in assemblyNames)
        {
            try
            {
                var path = await DownloadPluginFromCloudAsync(name, targetFolder);
                if (!string.IsNullOrEmpty(path))
                {
                    downloaded.Add(name);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[PluginLoader] 下载依赖 [{name}] 失败: {ex.Message}");
            }
        }

        return downloaded;
    }

    public async Task<string?> DownloadPluginFromCloudAsync(string assemblyName, string targetFolder)
    {
        try
        {
            var url = $"{_apiBaseUrl}/plugin/get_plugin_zip?assembly_name={assemblyName}&tshock_version={TShock.VersionNum}";
            TShock.Log.Info($"[PluginLoader] 正在从云端下载插件: {url}");
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TShock.Log.Error($"[PluginLoader] 下载插件 [{assemblyName}] HTTP错误: {response.StatusCode}, 内容: {errorContent}");
                return null;
            }

            var zipBytes = await response.Content.ReadAsByteArrayAsync();
            TShock.Log.Info($"[PluginLoader] 下载到 {zipBytes.Length} 字节");

            if (zipBytes.Length == 0)
            {
                TShock.Log.Error($"[PluginLoader] 下载插件 [{assemblyName}] 返回空内容");
                return null;
            }

            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms);

            string? dllPath = null;
            var extractedCount = 0;

            foreach (var entry in zip.Entries)
            {
                var entryName = entry.FullName;
                
                if (entryName.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase))
                {
                    entryName = entryName["Plugins/".Length..];
                }
                
                var fileName = Path.GetFileName(entryName);
                if (string.IsNullOrEmpty(fileName)) continue;
                
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                
                if (ext == ".dll" || ext == ".pdb" || ext == ".json" || ext == ".xml" || ext == ".txt" || ext == ".md")
                {
                    var targetPath = Path.Combine(targetFolder, fileName);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetFolder);
                    
                    using var stream = entry.Open();
                    using var fs = File.Create(targetPath);
                    stream.CopyTo(fs);
                    extractedCount++;
                    
                    if (ext == ".dll")
                    {
                        dllPath = targetPath;
                    }
                }
            }

            TShock.Log.Info($"[PluginLoader] 解压了 {extractedCount} 个文件");
            
            if (dllPath == null)
            {
                TShock.Log.Error($"[PluginLoader] 下载的ZIP中未找到DLL文件");
            }

            return dllPath;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 下载插件 [{assemblyName}] 失败: {ex.Message}");
            TShock.Log.Error($"[PluginLoader] 完整异常: {ex.ToString()}");
            return null;
        }
    }

    public async Task<Models.PluginVersionInfo?> SearchPluginAsync(string keyword)
    {
        try
        {
            if (_manifestCache.Count == 0)
            {
                await FetchPluginManifestsAsync();
            }

            var manifests = _manifestCache.Values.ToList();
            
            var exactMatch = manifests.FirstOrDefault(m => 
                m.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
                m.AssemblyName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            
            if (exactMatch != null)
                return exactMatch;

            var containsMatch = manifests.FirstOrDefault(m =>
                m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                m.AssemblyName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            return containsMatch;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 搜索插件失败: {ex.Message}");
            return null;
        }
    }

    public async Task<List<string>> ScanMissingDependenciesAsync(string dllPath, string targetFolder)
    {
        var missingDependencies = new List<string>();
        
        try
        {
            var assemblyBytes = File.ReadAllBytes(dllPath);
            var assembly = Assembly.Load(assemblyBytes);
            var referencedAssemblies = assembly.GetReferencedAssemblies();

            foreach (var refAssembly in referencedAssemblies)
            {
                if (refAssembly.Name == null)
                    continue;

                if (IsSystemAssembly(refAssembly.Name))
                    continue;

                try
                {
                    Assembly.Load(refAssembly.FullName ?? refAssembly.Name);
                }
                catch (FileNotFoundException)
                {
                    missingDependencies.Add(refAssembly.Name);
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 扫描依赖失败: {ex.Message}");
        }

        return missingDependencies;
    }

    private bool IsSystemAssembly(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        var systemAssemblies = new[]
        {
            "mscorlib", "System", "System.Core", "System.Data",
            "System.Xml", "Microsoft.Xna.Framework", "Terraria",
            "TerrariaServer", "TShockAPI", "HttpServer",
            "Rests", "MySqlConnector", "Newtonsoft.Json",
            "OTAPI", "ModFramework", "NetEscapades.Configuration.Yaml",
            "LazyAPI", "LazyAPICore", "AutoFish", "AutoFishR",
            "AutoClear", "AutoBroadcast", "AutoTeam", "AutoReset",
            "AutoStoreItems", "Back", "BadApplePlayer", "BanNpc",
            "BedSet", "BetterWhitelist", "BossLock", "CGive",
            "CNPCShop", "CaiBotLite", "CaiPacketDebug", "CaiRewardChest",
            "ChattyBridge", "ConsoleSql", "DataSync", "DeathDrop",
            "Dummy", "Ezperm", "GenerateMap", "GhostView",
            "HelpPlus", "History", "HouseRegion", "ItemBox",
            "MapTp", "Noagent", "NoteWall", "PermaBuff",
            "Platform", "ProgressBag", "PvPer", "QRCoder",
            "RainbowChat", "RealTime", "Respawn", "Sandstorm",
            "ServerTools", "ShowArmors", "SignInSign", "SmartRegions"
        };

        return systemAssemblies.Any(s => assemblyName.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string?> DownloadNuGetPackageAsync(string packageName, string targetFolder)
    {
        try
        {
            TShock.Log.Info($"[PluginLoader] 正在从NuGet下载依赖: {packageName}");
            
            var searchUrl = $"https://api.nuget.org/v3/registration5-gz-semver2/{packageName.ToLower()}/index.json";
            TShock.Log.Info($"[PluginLoader] NuGet搜索URL: {searchUrl}");
            
            var response = await _httpClient.GetAsync(searchUrl);
            TShock.Log.Info($"[PluginLoader] NuGet搜索状态: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TShock.Log.Warn($"[PluginLoader] NuGet未找到包: {packageName}, 响应: {errorContent}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            
            string? latestVersion = null;
            
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("upper", out var upper))
                    {
                        latestVersion = upper.GetString();
                    }
                }
            }
            
            if (string.IsNullOrEmpty(latestVersion))
            {
                TShock.Log.Warn($"[PluginLoader] 无法获取包版本: {packageName}");
                return null;
            }

            TShock.Log.Info($"[PluginLoader] 找到最新版本: {latestVersion}");
            var downloadUrl = $"https://www.nuget.org/api/v2/package/{packageName.ToLower()}/{latestVersion}";
            TShock.Log.Info($"[PluginLoader] NuGet下载URL: {downloadUrl}");
            
            var downloadResponse = await _httpClient.GetAsync(downloadUrl);
            TShock.Log.Info($"[PluginLoader] NuGet下载状态: {downloadResponse.StatusCode}");
            
            if (!downloadResponse.IsSuccessStatusCode)
            {
                var errorContent = await downloadResponse.Content.ReadAsStringAsync();
                TShock.Log.Warn($"[PluginLoader] NuGet下载失败: {downloadUrl}, 响应: {errorContent}");
                return null;
            }

            var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
            TShock.Log.Info($"[PluginLoader] NuGet下载字节数: {zipBytes.Length}");
            
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms);
            
            string? dllPath = null;
            
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("lib/"))
                {
                    var relativePath = entry.FullName["lib/".Length..];
                    var parts = relativePath.Split('/');
                    if (parts.Length >= 2)
                    {
                        var fileName = parts[parts.Length - 1];
                        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetPath = Path.Combine(targetFolder, fileName);
                            using var stream = entry.Open();
                            using var fs = File.Create(targetPath);
                            stream.CopyTo(fs);
                            dllPath = targetPath;
                            TShock.Log.Info($"[PluginLoader] 解压NuGet DLL: {fileName} -> {targetPath}");
                        }
                    }
                }
            }
            
            if (dllPath == null)
            {
                TShock.Log.Warn($"[PluginLoader] NuGet包中未找到DLL文件");
            }
            
            return dllPath;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 下载NuGet包 [{packageName}] 失败: {ex.Message}");
            TShock.Log.Error($"[PluginLoader] NuGet异常详情: {ex.ToString()}");
            return null;
        }
    }
}