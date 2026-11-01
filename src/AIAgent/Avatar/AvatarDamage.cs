using System;
using System.Reflection;
using TShockAPI;
using Terraria.DataStructures;

namespace AIAgent.Avatar;

/// <summary>记录分身最近一次受伤，供模型判断要不要躲/还嘴。</summary>
public static class AvatarDamage
{
    private static readonly object Gate = new();
    private static DateTime _lastAt;
    private static int _lastAmount;
    private static string _lastSource = string.Empty;
    private static bool _lastPvp;

    public static void Record(int amount, string source, bool pvp)
    {
        lock (Gate)
        {
            _lastAt = DateTime.UtcNow;
            _lastAmount = amount;
            _lastSource = source;
            _lastPvp = pvp;
        }
    }

    public static object? Snapshot()
    {
        lock (Gate)
        {
            if (_lastAt == default) return null;

            return new
            {
                secondsAgo = Math.Max(0, (int)(DateTime.UtcNow - _lastAt).TotalSeconds),
                amount = _lastAmount,
                source = _lastSource,
                pvp = _lastPvp,
            };
        }
    }

    /// <summary>从 PlayerDeathReason 的私有字段里反查来源（玩家/NPC）。</summary>
    public static string ResolveSource(PlayerDeathReason? reason)
    {
        if (reason is null) return "未知";

        var type = reason.GetType();
        var playerIndex = ReadInt(type, reason, "_sourcePlayerIndex");
        if (playerIndex >= 0 && playerIndex < TShock.Players.Length)
        {
            var player = TShock.Players[playerIndex];
            if (player is not null) return player.Name;
        }

        var npcIndex = ReadInt(type, reason, "_sourceNPCIndex");
        if (npcIndex >= 0 && npcIndex < Terraria.Main.npc.Length)
        {
            var npc = Terraria.Main.npc[npcIndex];
            if (npc is not null && npc.active) return Terraria.Lang.GetNPCNameValue(npc.type);
        }

        return "环境";
    }

    private static int ReadInt(Type type, object instance, string name)
    {
        var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(instance) is int value ? value : -1;
    }
}
