using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using TShockAPI;

namespace AIAgent;

public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "AIAgent.json");

    private static readonly JsonSerializerOptions StatsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void LoadConfig()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonConvert.DeserializeObject<Config>(json);
                PluginState.Config = cfg ?? new Config();
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[AIAgent] 配置文件加载失败: {ex.Message}，已使用默认配置");
                PluginState.Config = new Config();
                SaveConfig();
            }
        }
        else
        {
            SaveConfig();
            TShock.Log.Info("[AIAgent] 已创建默认配置文件");
        }
    }

    public static void SaveConfig()
    {
        try
        {
            var json = JsonConvert.SerializeObject(PluginState.Config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[AIAgent] 配置文件保存失败: {ex.Message}");
        }
    }

    public static void LoadTokenStats()
    {
        var path = Path.Combine(TShock.SavePath, PluginState.StatsFileName);
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var stats = System.Text.Json.JsonSerializer.Deserialize<List<PlayerTokenStats>>(json, StatsJsonOptions);
            if (stats != null)
                foreach (var s in stats)
                    PluginState.TokenStats[s.PlayerName] = s;
        }
        catch { }
    }

    public static void SaveTokenStats()
    {
        try
        {
            var path = Path.Combine(TShock.SavePath, PluginState.StatsFileName);
            var stats = PluginState.TokenStats.Values.ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(stats, StatsJsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public static void RecordTokenUsage(string playerName, long promptTokens, long completionTokens)
    {
        if (!PluginState.Config.EnableTokenStats) return;
        var stats = PluginState.TokenStats.GetOrAdd(playerName, _ => new PlayerTokenStats
        {
            PlayerName = playerName,
            FirstUsed = DateTime.Now
        });
        stats.PromptTokens += promptTokens;
        stats.CompletionTokens += completionTokens;
        stats.TotalTokens += promptTokens + completionTokens;
        stats.RequestCount++;
        stats.LastUsed = DateTime.Now;
    }
}