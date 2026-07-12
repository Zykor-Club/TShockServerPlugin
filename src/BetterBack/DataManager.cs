using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;

namespace BetterBack;

public sealed class DataManager
{
    private readonly ConcurrentDictionary<string, PlayerData> _playerData = new();

    public PlayerData GetOrCreatePlayerData(string playerName) => 
        _playerData.GetOrAdd(playerName, name => new PlayerData(name));

    public bool AddDeathPoint(string playerName, int x, int y, string reason)
    {
        try
        {
            var data = GetOrCreatePlayerData(playerName);
            var point = new DeathPoint($"死亡点 {data.DeathPoints.Count + 1}", x, y, reason, string.Empty);
            return data.AddDeathPoint(point, BetterBackConfig.Instance.MaxDeathPointsPerPlayer);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[BetterBack] 添加死亡点失败: {ex}");
            return false;
        }
    }

    public IReadOnlyList<DeathPoint> GetPlayerDeathPoints(string playerName) => 
        GetOrCreatePlayerData(playerName).DeathPoints.AsReadOnly();

    public int GetPlayerDeathCount(string playerName) => 
        GetOrCreatePlayerData(playerName).DeathPoints.Count;

    public DeathPoint? GetLastDeathPoint(string playerName) => 
        GetOrCreatePlayerData(playerName).LastDeathPoint;

    public DeathPoint? GetDeathPointByIndex(string playerName, int index) =>
        GetOrCreatePlayerData(playerName).GetDeathPointByIndex(index);

    public void AddCustomBuff(string playerName, int buffId)
    {
        var data = GetOrCreatePlayerData(playerName);
        if (!data.CustomBuffs.Contains(buffId))
            data.CustomBuffs.Add(buffId);
    }

    public void RemoveCustomBuff(string playerName, int buffId)
    {
        GetOrCreatePlayerData(playerName).CustomBuffs.Remove(buffId);
    }

    public IReadOnlyList<int> GetCustomBuffs(string playerName) => 
        GetOrCreatePlayerData(playerName).CustomBuffs.AsReadOnly();

    public void SetCustomGodModeDuration(string playerName, int duration)
    {
        GetOrCreatePlayerData(playerName).CustomGodModeDuration = duration;
    }

    public int GetCustomGodModeDuration(string playerName) => 
        GetOrCreatePlayerData(playerName).CustomGodModeDuration;

    public void SetAutoReturnDelay(string playerName, int seconds)
    {
        GetOrCreatePlayerData(playerName).AutoReturnDelay = seconds;
    }

    public int GetAutoReturnDelay(string playerName) => 
        GetOrCreatePlayerData(playerName).AutoReturnDelay;

    public int ClearPlayerDeathPoints(string playerName)
    {
        return GetOrCreatePlayerData(playerName).ClearDeathPoints();
    }
}
