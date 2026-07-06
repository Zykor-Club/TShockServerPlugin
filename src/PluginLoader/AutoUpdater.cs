using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TShockAPI;

namespace PluginLoader;

public class AutoUpdater
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner = "Zykor-Club";
    private readonly string _repoName = "TShockServerPlugin";
    private readonly string _serverPluginsFolder;
    private readonly string _hotPluginsFolder;

    public event Action? UpdateCompleted;

    public AutoUpdater(string? githubToken = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PluginLoader");
        if (!string.IsNullOrEmpty(githubToken))
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {githubToken}");
        _serverPluginsFolder = Path.Combine(AppContext.BaseDirectory, "ServerPlugins");
        _hotPluginsFolder = Path.Combine(AppContext.BaseDirectory, "HotPlugins");
    }

    public async Task<List<PluginUpdateInfo>> CheckForUpdatesAsync()
    {
        var updates = new List<PluginUpdateInfo>();
        try
        {
            var jsonUrl = $"https://raw.githubusercontent.com/{_repoOwner}/{_repoName}/main/Plugins.json";
            var jsonBytes = await _httpClient.GetByteArrayAsync(jsonUrl);
            var remotePlugins = JsonSerializer.Deserialize<List<PluginJsonEntry>>(jsonBytes);
            if (remotePlugins == null) return updates;

            var searchFolders = new[] { _serverPluginsFolder, _hotPluginsFolder };

            foreach (var remote in remotePlugins)
            {
                if (string.IsNullOrEmpty(remote.AssemblyName)) continue;

                string? localPath = null;
                foreach (var folder in searchFolders)
                {
                    var testPath = Path.Combine(folder, $"{remote.AssemblyName}.dll");
                    if (File.Exists(testPath))
                    {
                        localPath = testPath;
                        break;
                    }
                }

                var localVersion = localPath != null ? GetLocalPluginVersion(localPath) : null;

                if (localVersion == null || IsNewerVersion(remote.Version, localVersion.ToString()))
                {
                    updates.Add(new PluginUpdateInfo
                    {
                        AssemblyName = remote.AssemblyName,
                        LocalVersion = localVersion?.ToString() ?? "未安装",
                        RemoteVersion = remote.Version,
                        IsNewInstall = localVersion == null
                    });
                }
            }
        }
        catch (HttpRequestException ex)
        {
            TShock.Log.Info($"[PluginLoader] 检查更新网络错误: {ex.Message}");
        }
        catch (Exception ex)
        {
            TShock.Log.Info($"[PluginLoader] 检查更新失败: {ex.Message}");
        }
        return updates;
    }

    public async Task<bool> DownloadAndInstallUpdatesAsync(IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("正在获取最新Release信息...");
            var releaseUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var releaseJson = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var zipAsset = release?.Assets?.FirstOrDefault(a => a.Name == "Plugins.zip");
            if (zipAsset == null)
            {
                TShock.Log.Error("[PluginLoader] Release中未找到Plugins.zip");
                progress?.Report("Release中未找到Plugins.zip");
                return false;
            }

            progress?.Report("正在下载 Plugins.zip...");
            TShock.Log.Info($"[PluginLoader] 下载中: {zipAsset.BrowserDownloadUrl}");
            var zipBytes = await _httpClient.GetByteArrayAsync(zipAsset.BrowserDownloadUrl);

            progress?.Report("正在解压安装...");
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms);

            int count = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.Name;
                if (string.IsNullOrEmpty(name)) continue;

                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    var dest = Path.Combine(_serverPluginsFolder, name);
                    try { entry.ExtractToFile(dest, overwrite: true); count++; }
                    catch (Exception ex) { TShock.Log.Warn($"[PluginLoader] 解压失败 {name}: {ex.Message}"); }
                }
            }

            TShock.Log.Info($"[PluginLoader] 更新完成，更新 {count} 个文件");
            progress?.Report($"更新完成，共更新 {count} 个文件。使用 /pl ra 重载或重启服务器生效。");

            UpdateCompleted?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[PluginLoader] 更新失败: {ex.Message}");
            progress?.Report($"更新失败: {ex.Message}");
            return false;
        }
    }

    private static Version? GetLocalPluginVersion(string dllPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(dllPath);
            var asm = System.Reflection.Assembly.Load(bytes);
            return asm.GetName().Version;
        }
        catch { return null; }
    }

    private static bool IsNewerVersion(string remoteVersion, string localVersionStr)
    {
        if (Version.TryParse(remoteVersion, out var rv) &&
            Version.TryParse(localVersionStr, out var lv))
        {
            return rv > lv;
        }
        return false;
    }

    public void Dispose() => _httpClient.Dispose();
}

public class PluginUpdateInfo
{
    public string AssemblyName { get; set; } = "";
    public string LocalVersion { get; set; } = "";
    public string RemoteVersion { get; set; } = "";
    public bool IsNewInstall { get; set; }
}

public class PluginJsonEntry
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Dependencies { get; set; } = new();
}

public class GitHubRelease
{
    public string TagName { get; set; } = "";
    public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    public string Name { get; set; } = "";
    public string BrowserDownloadUrl { get; set; } = "";
}
