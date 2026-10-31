using System;
using AIAgent.Config;

namespace AIAgent.Chat;

/// <summary>问候策略（纯逻辑，方便单测）。</summary>
public static class GreetingPolicy
{
    /// <summary>解析 "01:00-07:00" 形式的静默时段，支持跨午夜。</summary>
    public static bool InQuietHours(string? spec, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!TryParseTime(parts[0], out var start) || !TryParseTime(parts[1], out var end)) return false;

        var current = now.TimeOfDay;
        return start <= end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    private static bool TryParseTime(string text, out TimeSpan value)
    {
        value = default;
        var bits = text.Split(':');
        if (bits.Length < 2) return false;
        if (!int.TryParse(bits[0], out var hour) || !int.TryParse(bits[1], out var minute)) return false;
        if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;
        value = new TimeSpan(hour, minute, 0);
        return true;
    }

    public static bool Allowed(DateTime now, DateTime? lastGreeted, GreetingSettings cfg)
    {
        if (!cfg.Enabled) return false;
        if (InQuietHours(cfg.QuietHours, now)) return false;
        if (lastGreeted is not null && now - lastGreeted.Value < TimeSpan.FromHours(Math.Max(0, cfg.MinIntervalHours))) return false;
        return true;
    }
}

