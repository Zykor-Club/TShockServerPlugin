using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AIAgent.Config;

namespace AIAgent.Chat;

/// <summary>好感度分档：影响称呼和语气。</summary>
public static class AffinityTier
{
    public static (string Tier, string Address, string Tone) Describe(int affinity) => affinity switch
    {
        < 0 => ("疏远", "你", "客气、保持距离，但还是会帮忙"),
        < 30 => ("一般", "主人", "礼貌、话不多"),
        < 70 => ("熟悉", "主人", "自然亲近，偶尔拌嘴"),
        < 90 => ("亲近", "笨蛋主人", "随意、爱撒娇、会嘴硬"),
        _ => ("很亲近", "笨蛋主人", "黏人，会耍赖，也会吃醋"),
    };
}

/// <summary>
/// 关怀类行为：深夜提醒、连死计数。
/// 深夜关怀按服务器本地时间判断（也就是现实世界时间）。
/// </summary>
public static class CareService
{
    private static readonly ConcurrentDictionary<string, DateTime> LateNightReminded = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, List<DateTime>> Deaths = new(StringComparer.Ordinal);

    /// <summary>凌晨还在线时给一句提示；同一晚只给一次。</summary>
    public static string LateNightHint(string key, DateTime now, CareSettings cfg)
    {
        if (!cfg.LateNight) return string.Empty;
        if (!GreetingPolicy.InQuietHours(cfg.LateNightWindow, now)) return string.Empty;

        if (LateNightReminded.TryGetValue(key, out var last) &&
            now - last < TimeSpan.FromHours(Math.Max(1, cfg.LateNightIntervalHours)))
        {
            return string.Empty;
        }

        LateNightReminded[key] = now;
        return $"现在是凌晨 {now.Hour} 点，主人还在线。可以顺口关心一句让他早点睡，只说一句，别唠叨。";
    }

    /// <summary>记录一次死亡，返回窗口内的连死次数。</summary>
    public static int RecordDeath(string key, DateTime now, int windowMinutes)
    {
        var list = Deaths.GetOrAdd(key, _ => new List<DateTime>());
        lock (list)
        {
            list.Add(now);
            list.RemoveAll(at => (now - at).TotalMinutes > Math.Max(1, windowMinutes));
            return list.Count;
        }
    }

    public static void ResetPlayer(string key)
    {
        LateNightReminded.TryRemove(key, out _);
        Deaths.TryRemove(key, out _);
    }
}
