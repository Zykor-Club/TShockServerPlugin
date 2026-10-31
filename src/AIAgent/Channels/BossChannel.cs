using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using AIAgent.Chat;
using AIAgent.Core;

namespace AIAgent.Channels;

/// <summary>
/// 服务器事件播报：Boss 出现/被击败、血月、日食、下雨。
/// 只读游戏状态，不涉及玩家权限；所有播报都走 ChatService 的事件通道。
/// </summary>
public static class BossChannel
{
    private static readonly ConcurrentDictionary<int, (string Name, DateTime At)> LastStriker = new();
    private static readonly Dictionary<int, int> ActiveBossRoots = new();
    private static readonly ConcurrentDictionary<int, DateTime> AnnouncedRoots = new();
    private static readonly ConcurrentDictionary<string, DateTime> Announced = new(StringComparer.Ordinal);

    private static bool _initialized;
    private static bool _bloodMoon;
    private static bool _eclipse;
    private static bool _raining;

    public static void OnStrike(NpcStrikeEventArgs args)
    {
        try { HandleStrike(args); }
        catch (Exception ex) { PluginLog.Error("NpcStrike 钩子异常", ex); }
    }

    private static void HandleStrike(NpcStrikeEventArgs args)
    {
        if (args?.Npc is null || args.Player is null) return;

        var index = args.Player.whoAmI;
        if (index < 0 || index >= TShock.Players.Length) return;

        var name = TShock.Players[index]?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        LastStriker[args.Npc.whoAmI] = (name, DateTime.UtcNow);
    }

    public static void OnNpcKilled(NpcKilledEventArgs args)
    {
        try { HandleKilled(args); }
        catch (Exception ex) { PluginLog.Error("NpcKilled 钩子异常", ex); }
    }

    private static void HandleKilled(NpcKilledEventArgs args)
    {
        var npc = args?.npc;
        if (npc is null || !npc.boss) return;

        var name = NpcName(npc.type);
        if (string.IsNullOrWhiteSpace(name)) return;

        // 多节 Boss 的每一节都会触发 NpcKilled：用 realLife 归并到本体，同一个 Boss 只播一次
        var root = npc.realLife >= 0 ? npc.realLife : npc.whoAmI;
        if (!AllowRoot(root, TimeSpan.FromSeconds(15))) return;

        // 同类型 Boss 90 秒内只播一次（防止刷同一种 Boss 刷屏）
        if (!Allow("kill:" + npc.type, TimeSpan.FromSeconds(90))) return;

        var killer = ResolveKiller(npc);
        var prompt = string.IsNullOrWhiteSpace(killer)
            ? $"（系统事件）服务器里的 {name} 被击败了。用一句话感慨一下，别念稿。"
            : $"（系统事件）{killer} 刚刚击败了 {name}。具体地夸他一句，别只说“厉害”。";

        Announce(prompt, killer);
    }

    /// <summary>主循环里每 15 帧调一次。</summary>
    public static void Poll()
    {
        var cfg = PluginState.Config;
        if (!cfg.Chat.Enabled || !cfg.Chat.EventAnnouncements) return;

        var current = new Dictionary<int, int>();

        foreach (var npc in Main.npc)
        {
            if (npc is null || !npc.active || !npc.boss) continue;
            var root = npc.realLife >= 0 ? npc.realLife : npc.whoAmI;
            current[root] = npc.type;
        }

        if (!_initialized)
        {
            // 启动时不要补播报，只记录当前状态
            _initialized = true;
            foreach (var pair in current) ActiveBossRoots[pair.Key] = pair.Value;
            _bloodMoon = Main.bloodMoon;
            _eclipse = Main.eclipse;
            _raining = Main.raining;
            return;
        }

        foreach (var pair in current)
        {
            if (ActiveBossRoots.ContainsKey(pair.Key)) continue;

            var name = NpcName(pair.Value);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!Allow("spawn:" + pair.Value, TimeSpan.FromSeconds(60))) continue;

            Announce($"（系统事件）{name} 在服务器里出现了。用一句话提醒在场的主人小心，别长篇。", null);
        }

        ActiveBossRoots.Clear();
        foreach (var pair in current) ActiveBossRoots[pair.Key] = pair.Value;

        if (Main.bloodMoon != _bloodMoon)
        {
            _bloodMoon = Main.bloodMoon;
            if (_bloodMoon && Allow("bloodmoon", TimeSpan.FromMinutes(20)))
            {
                Announce("（系统事件）血月开始了。用一句话提醒主人怪物会变凶，别念稿。", null);
            }
        }

        if (Main.eclipse != _eclipse)
        {
            _eclipse = Main.eclipse;
            if (_eclipse && Allow("eclipse", TimeSpan.FromMinutes(20)))
            {
                Announce("（系统事件）日食开始了。用一句话提醒主人白天也会刷怪。", null);
            }
        }

        if (Main.raining != _raining)
        {
            _raining = Main.raining;
            if (_raining && Allow("rain", TimeSpan.FromMinutes(20)))
            {
                Announce("（系统事件）下雨了。随口说一句，别啰嗦。", null);
            }
        }
    }

    private static bool AllowRoot(int root, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (AnnouncedRoots.TryGetValue(root, out var last) && now - last < cooldown) return false;
        AnnouncedRoots[root] = now;

        // 顺手清理过期记录，避免长期运行膨胀
        if (AnnouncedRoots.Count > 200)
        {
            foreach (var pair in AnnouncedRoots)
            {
                if (now - pair.Value > TimeSpan.FromMinutes(5)) AnnouncedRoots.TryRemove(pair.Key, out _);
            }
        }

        return true;
    }

    private static bool Allow(string key, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (Announced.TryGetValue(key, out var last) && now - last < cooldown) return false;
        Announced[key] = now;
        return true;
    }

    private static string? ResolveKiller(NPC npc)
    {
        if (!LastStriker.TryGetValue(npc.whoAmI, out var strike)) return null;
        if (DateTime.UtcNow - strike.At > TimeSpan.FromSeconds(60)) return null;
        return strike.Name;
    }

    private static string NpcName(int type)
    {
        try
        {
            var name = Lang.GetNPCNameValue(type);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch
        {
            // 忽略，回退到内部名
        }

        return Terraria.ID.NPCID.Search.GetName(type);
    }

    private static void Announce(string prompt, string? preferredPlayer)
    {
        var cfg = PluginState.Config;
        if (cfg.DebugLog) PluginLog.Info($"事件检测: {prompt}");

        var player = FindPlayer(preferredPlayer) ?? TShock.Players.FirstOrDefault(p => p is { Active: true } && p.HasPermission("aiagent.use"));
        if (player is null) return;

        var mode = cfg.Chat.PublicReply ? ChatReplyMode.Public : ChatReplyMode.Private;
        _ = ChatService.HandleAsync(player, prompt, mode, isEvent: true);
    }

    private static TSPlayer? FindPlayer(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return TShock.Players.FirstOrDefault(p =>
            p is { Active: true } && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

}
