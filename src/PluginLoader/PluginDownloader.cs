using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TShockAPI;

namespace PluginLoader;

public class DownloadProgressEventArgs : EventArgs
{
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public double Percent => TotalBytes > 0 ? (ReceivedBytes * 100.0 / TotalBytes) : 0;
    public double Speed { get; set; }
    public string FileName { get; set; } = string.Empty;
}

public class PluginDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string? _githubToken;

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgressChanged;

    public PluginDownloader(string? githubToken = null)
    {
        _githubToken = githubToken;
        var handler = new HttpClientHandler
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<byte[]> DownloadFromUrlAsync(string url, string fileName = "")
    {
        int retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var stream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;
                var startTime = DateTime.Now;
                var lastProgressTime = DateTime.Now;

                using var memoryStream = new MemoryStream();
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    var now = DateTime.Now;
                    var elapsed = (now - startTime).TotalSeconds;
                    if (elapsed > 0 && (now - lastProgressTime).TotalSeconds >= 0.5)
                    {
                        lastProgressTime = now;
                        OnProgressChanged(new DownloadProgressEventArgs
                        {
                            TotalBytes = totalBytes,
                            ReceivedBytes = totalBytesRead,
                            Speed = totalBytesRead / 1024.0 / elapsed,
                            FileName = fileName
                        });
                    }
                }

                OnProgressChanged(new DownloadProgressEventArgs
                {
                    TotalBytes = totalBytesRead,
                    ReceivedBytes = totalBytesRead,
                    Speed = totalBytesRead / 1024.0 / (DateTime.Now - startTime).TotalSeconds,
                    FileName = fileName
                });

                return memoryStream.ToArray();
            }
            catch (HttpRequestException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                await Task.Delay(2000 * retryCount);
            }
        }

        throw new Exception("下载失败，重试次数已达上限");
    }

    public async Task<byte[]?> DownloadFromGitHubAsync(string owner, string repo, string version = "latest")
    {
        return await DownloadFromGitHubAsync(owner, repo, version, null);
    }

    public async Task<byte[]?> DownloadFromGitHubAsync(string owner, string repo, string version, string? saveDirectory)
    {
        string? tag = null;
        Models.GitHubRelease? release = null;

        var tagsToTry = new List<string> { version };
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tagsToTry.Add(version.Substring(1));
        else
            tagsToTry.Add("v" + version);

        foreach (var tagTry in tagsToTry)
        {
            release = await FetchReleaseByTagAsync(owner, repo, tagTry);
            if (release != null)
            {
                tag = tagTry;
                break;
            }
        }

        if (release == null)
        {
            release = await FetchLatestReleaseAsync(owner, repo);
            if (release != null)
                tag = release.TagName;
        }

        if (release == null)
        {
            release = await FetchReleasesListAsync(owner, repo);
            if (release != null)
                tag = release.TagName;
        }

        if (release == null || release.Assets.Length == 0)
        {
            TShock.Log.Error($"无法获取GitHub Release信息，尝试直接构造下载链接...");
            return await TryDirectDownloadAsync(owner, repo, version);
        }

        var dllAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (dllAsset == null)
        {
            TShock.Log.Error($"Release中没有找到.dll文件，可用文件: {string.Join(", ", release.Assets.Select(a => a.Name))}");
            return null;
        }

        TShock.Log.Info($"找到DLL文件: {dllAsset.Name}, 大小: {dllAsset.Size} bytes, 下载链接: {dllAsset.BrowserDownloadUrl}");

        OnProgressChanged(new DownloadProgressEventArgs
        {
            FileName = dllAsset.Name,
            TotalBytes = dllAsset.Size
        });

        byte[] dllBytes = await DownloadFromUrlAsync(dllAsset.BrowserDownloadUrl, dllAsset.Name);

        if (!string.IsNullOrEmpty(saveDirectory))
        {
            var pdbAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
            if (pdbAsset != null)
            {
                TShock.Log.Info($"找到PDB文件: {pdbAsset.Name}, 正在下载...");
                try
                {
                    var pdbBytes = await DownloadFromUrlAsync(pdbAsset.BrowserDownloadUrl, pdbAsset.Name);
                    var pdbPath = Path.Combine(saveDirectory, pdbAsset.Name);
                    await File.WriteAllBytesAsync(pdbPath, pdbBytes);
                    TShock.Log.Info($"PDB文件已保存到: {pdbPath}");
                }
                catch (Exception ex)
                {
                    TShock.Log.Warn($"下载PDB文件失败: {ex.Message}");
                }
            }

            var zipAssets = release.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var zipAsset in zipAssets)
            {
                TShock.Log.Info($"找到ZIP文件: {zipAsset.Name}, 正在下载...");
                try
                {
                    var zipBytes = await DownloadFromUrlAsync(zipAsset.BrowserDownloadUrl, zipAsset.Name);
                    var zipPath = Path.Combine(saveDirectory, zipAsset.Name);
                    await File.WriteAllBytesAsync(zipPath, zipBytes);
                    TShock.Log.Info($"ZIP文件已保存到: {zipPath}");
                }
                catch (Exception ex)
                {
                    TShock.Log.Warn($"下载ZIP文件失败: {ex.Message}");
                }
            }
        }

        return dllBytes;
    }

    private async Task<Models.GitHubRelease?> FetchReleaseByTagAsync(string owner, string repo, string tag)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{tag}";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            request.Headers.Add("User-Agent", "PluginLoader/2.0");
            if (!string.IsNullOrEmpty(_githubToken))
            {
                request.Headers.Add("Authorization", $"token {_githubToken}");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                TShock.Log.Info($"FetchReleaseByTagAsync {tag} failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Models.GitHubRelease>(json, options);
        }
        catch (Exception ex)
        {
            TShock.Log.Info($"FetchReleaseByTagAsync {tag} exception: {ex.Message}");
            return null;
        }
    }

    private async Task<Models.GitHubRelease?> FetchLatestReleaseAsync(string owner, string repo)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            request.Headers.Add("User-Agent", "PluginLoader/2.0");
            if (!string.IsNullOrEmpty(_githubToken))
            {
                request.Headers.Add("Authorization", $"token {_githubToken}");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                TShock.Log.Info($"FetchLatestReleaseAsync failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Models.GitHubRelease>(json, options);
        }
        catch (Exception ex)
        {
            TShock.Log.Info($"FetchLatestReleaseAsync exception: {ex.Message}");
            return null;
        }
    }

    private async Task<Models.GitHubRelease?> FetchReleasesListAsync(string owner, string repo)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            request.Headers.Add("User-Agent", "PluginLoader/2.0");
            if (!string.IsNullOrEmpty(_githubToken))
            {
                request.Headers.Add("Authorization", $"token {_githubToken}");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                TShock.Log.Info($"FetchReleasesListAsync failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var releases = JsonSerializer.Deserialize<Models.GitHubRelease[]>(json, options);
            return releases?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            TShock.Log.Info($"FetchReleasesListAsync exception: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> TryDirectDownloadAsync(string owner, string repo, string version)
    {
        try
        {
            var potentialTags = new List<string> { version };
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                potentialTags.Add(version.Substring(1));
            else
                potentialTags.Add("v" + version);

            foreach (var tag in potentialTags)
            {
                var downloadUrl = $"https://github.com/{owner}/{repo}/releases/download/{tag}/{repo}.dll";
                TShock.Log.Info($"尝试直接下载: {downloadUrl}");

                try
                {
                    var bytes = await DownloadFromUrlAsync(downloadUrl, $"{repo}.dll");
                    if (bytes.Length > 0)
                    {
                        TShock.Log.Info($"直接下载成功，文件大小: {bytes.Length} bytes");
                        return bytes;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Info($"直接下载 {tag} 失败: {ex.Message}");
                }
            }

            var latestUrl = $"https://github.com/{owner}/{repo}/releases/download/latest/{repo}.dll";
            TShock.Log.Info($"尝试直接下载latest: {latestUrl}");

            try
            {
                var bytes = await DownloadFromUrlAsync(latestUrl, $"{repo}.dll");
                if (bytes.Length > 0)
                {
                    TShock.Log.Info($"直接下载latest成功，文件大小: {bytes.Length} bytes");
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"所有直接下载尝试均失败: {ex.Message}");
            }

            return null;
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"TryDirectDownloadAsync exception: {ex.Message}");
            return null;
        }
    }

    public void CopyLocalFile(string sourcePath, string targetFolder)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("源文件不存在", sourcePath);

        var fileName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(targetFolder, fileName);
        File.Copy(sourcePath, targetPath, true);
    }

    public bool IsValidDll(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var buffer = new byte[2];
            var bytesRead = fs.Read(buffer, 0, 2);
            if (bytesRead >= 2 && buffer[0] == 0x4D && buffer[1] == 0x5A)
            {
                try
                {
                    var assemblyBytes = File.ReadAllBytes(filePath);
                    System.Reflection.Assembly.Load(assemblyBytes);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    protected virtual void OnProgressChanged(DownloadProgressEventArgs e)
    {
        DownloadProgressChanged?.Invoke(this, e);
    }
}