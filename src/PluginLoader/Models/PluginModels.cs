using System.Collections.Generic;
using System;
using TerrariaApi.Server;
using TShockAPI;
using System.Text.Json.Serialization;

namespace PluginLoader.Models;

public class LoadedPlugin
{
    public string AssemblyPath { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public TerrariaPlugin? Plugin { get; set; }
    public List<Command> Commands { get; set; } = new List<Command>();
    public bool Enabled { get; set; } = true;
    public DateTime LoadedTime { get; set; } = DateTime.UtcNow;
}

public class PluginConfig
{
    public bool AutoReload { get; set; } = true;
    public List<string> DisabledPlugins { get; set; } = new List<string>();
    public string? GitHubToken { get; set; }
    public string WatchFolder { get; set; } = "HotPlugins";
}

public class PluginVersionInfo
{
    public Version Version { get; set; } = new Version();
    public string Author { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Description { get; set; } = new Dictionary<string, string>();
    public string FileName { get; set; } = string.Empty;
    public string[] Dependencies { get; set; } = Array.Empty<string>();
    public bool HotReload { get; set; } = true;
    public string AssemblyName { get; set; } = string.Empty;
}

public class GitHubAsset
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}