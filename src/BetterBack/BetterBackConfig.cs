using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TShockAPI;

namespace BetterBack;

public sealed class BetterBackConfig
{
    private static readonly Lazy<BetterBackConfig> _instance = new(() => new());
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "BetterBack.json");
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Converters = new List<JsonConverter> { new StringEnumConverter() }
    };

    public static BetterBackConfig Instance => _instance.Value;

    [JsonProperty("最大死亡点数量")] public int MaxDeathPointsPerPlayer { get; set; } = 5;
    [JsonProperty("传送冷却时间")] public int TeleportCooldown { get; set; } = 30;
    
    [JsonProperty("默认传送BuffID")] public List<int> DefaultBuffIDs { get; set; } = new() { 1, 3, 5 };
    [JsonProperty("Buff持续时间")] public int BuffDuration { get; set; } = 10;
    [JsonProperty("无敌时间")] public int GodModeDuration { get; set; } = 5;

    [JsonProperty("死亡点记录消息")] public string DeathPointRecordedMessage { get; set; } = "已记录死亡点 ({0}/{1})";
    [JsonProperty("传送成功消息")] public string TeleportSuccessMessage { get; set; } = "已传送至死亡点: {0}";
    [JsonProperty("无敌时间消息")] public string GodModeMessage { get; set; } = "您获得了 {0} 秒无敌时间";
    [JsonProperty("冷却时间消息")] public string CooldownMessage { get; set; } = "请等待 {0:F1} 秒后再试";

    [JsonProperty("禁止记录未击败骷髅王的地牢死亡")] public bool BlockDungeonDeathBeforeSkeletron { get; set; } = true;
    [JsonProperty("禁止记录未击败世纪之花的神庙死亡")] public bool BlockTempleDeathBeforePlantera { get; set; } = true;

    private BetterBackConfig() { }

    public void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Save();
                return;
            }

            var json = File.ReadAllText(ConfigPath);
            JsonConvert.PopulateObject(json, this, JsonSettings);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[BetterBack] 配置加载失败: {ex}");
            Save();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(this, JsonSettings);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[BetterBack] 配置保存失败: {ex}");
        }
    }
}
