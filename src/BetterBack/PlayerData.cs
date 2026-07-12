using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace BetterBack;

public sealed class PlayerData
{
    [JsonProperty("玩家名称")] public string PlayerName { get; set; } = string.Empty;
    [JsonProperty("死亡点列表")] public List<DeathPoint> DeathPoints { get; set; } = new();
    [JsonProperty("自定义Buff列表")] public List<int> CustomBuffs { get; set; } = new();
    [JsonProperty("自定义无敌时间")] public int CustomGodModeDuration { get; set; }
    [JsonProperty("自动返回秒数")] public int AutoReturnDelay { get; set; }

    public DeathPoint? LastDeathPoint => DeathPoints.LastOrDefault();

    public PlayerData() { }

    public PlayerData(string playerName)
    {
        PlayerName = playerName;
    }

    public bool AddDeathPoint(DeathPoint point, int maxPoints)
    {
        if (DeathPoints.Count >= maxPoints)
            DeathPoints.RemoveAt(0);
        
        DeathPoints.Add(point);
        return true;
    }

    public int ClearDeathPoints()
    {
        var count = DeathPoints.Count;
        DeathPoints.Clear();
        return count;
    }

    public DeathPoint? GetDeathPointByIndex(int index) => 
        (index >= 1 && index <= DeathPoints.Count) ? DeathPoints[index - 1] : null;
}
