using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TShockAPI;

namespace GroupBlacklistPlugin;

public class BlacklistConfig
{
    [JsonPropertyName("插件设置")]
    public PluginSettings Settings { get; set; } = new();

    [JsonPropertyName("黑名单组列表")]
    public List<string> BlacklistedGroups { get; set; } = ["poooo", "如悠"];

    [JsonPropertyName("豁免玩家列表")]
    public List<string> ExemptPlayers { get; set; } = [];

    [JsonIgnore]
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "GroupBlacklist.json");

    public static BlacklistConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var cfg = new BlacklistConfig();
            cfg.Save();
            TShock.Log.Info("[组黑名单] 已创建默认配置文件");
            return cfg;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<BlacklistConfig>(json) ?? new BlacklistConfig();
        }
        catch (System.Exception ex)
        {
            TShock.Log.Error($"[组黑名单] 加载配置失败: {ex.Message}");
            return new BlacklistConfig();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (System.Exception ex)
        {
            TShock.Log.Error($"[组黑名单] 保存配置失败: {ex.Message}");
        }
    }
}

public class PluginSettings
{
    [JsonPropertyName("启用插件")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("拒绝加入提示信息")]
    public string KickMessage { get; set; } = "你的用户组被禁止进入此服务器";

    [JsonPropertyName("踢出提示信息")]
    public string InGameKickMessage { get; set; } = "你所属的用户组已被列入黑名单";

    [JsonPropertyName("检测间隔(秒)")]
    public int CheckInterval { get; set; } = 10;

    [JsonPropertyName("是否踢出在线黑名单玩家")]
    public bool KickOnlineBlacklist { get; set; } = true;

    [JsonPropertyName("是否记录日志")]
    public bool LogActions { get; set; } = true;
}