using System;
using System.Linq;
using Terraria;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Chat;

/// <summary>
/// 主动搭话：服务器安静一段时间后，挑一个在线玩家开口。
/// 只在主线程调用（Tick 由 GamePostUpdate 驱动），读世界状态安全。
/// </summary>
public static class ProactiveService
{
    private static DateTime _lastAt = DateTime.MinValue;
    private static DateTime _day = DateTime.Today;
    private static int _todayCount;

    public static void Tick(DateTime now)
    {
        var cfg = PluginState.Config.Proactive;
        if (!cfg.Enabled) return;

        if (now.Date != _day)
        {
            _day = now.Date;
            _todayCount = 0;
        }

        if (_todayCount >= cfg.DailyLimit) return;
        if (now - _lastAt < TimeSpan.FromMinutes(Math.Max(1, cfg.IntervalMinutes))) return;
        if (GreetingPolicy.InQuietHours(cfg.QuietHours, now)) return;
        if (ChatActivity.MinutesSinceLastMessage() < Math.Max(1, cfg.QuietMinutes)) return;
        if (Main.npc.Any(n => n.active && n.boss)) return;

        var candidates = TShock.Players
            .Where(p => p is { Active: true } && p.HasPermission("aiagent.use"))
            .ToList();

        if (candidates.Count == 0) return;

        // 优先挑最近说过话的人（让自言自语能接上前文），否则随机
        var lastSpeaker = ChatActivity.LastSpeaker();
        var target = candidates.FirstOrDefault(p => string.Equals(p.Name, lastSpeaker, StringComparison.OrdinalIgnoreCase))
                     ?? candidates[Random.Shared.Next(candidates.Count)];

        _lastAt = now;
        _todayCount++;

        var quiet = (int)ChatActivity.MinutesSinceLastMessage();
        var prompt = $"（系统事件）服务器安静了大约 {quiet} 分钟。你自言自语一句，或者对服务器里的大家随口说一句：" +
                     "不要叫「主人」，不要点名任何人，不要提问，不要追问；一句话，像自己嘀咕，别像公告。";

        var mode = cfg.Public ? ChatReplyMode.Public : ChatReplyMode.Private;

        if (PluginState.Config.DebugLog) PluginLog.Info($"主动消息: {target.Name}");

        _ = ChatService.HandleAsync(target, prompt, mode, isEvent: true);
    }
}
