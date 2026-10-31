using System;
using System.Collections.Concurrent;

namespace AIAgent.Core;

/// <summary>
/// 每玩家串行 + 冷却。同一玩家同时只能有一个请求在跑，
/// 否则两条请求会并发改写同一个会话上下文。
/// </summary>
public sealed class RateLimiter
{
    private readonly ConcurrentDictionary<string, DateTime> _last = new();
    private readonly ConcurrentDictionary<string, byte> _busy = new();

    public bool TryBegin(string key, int cooldownSeconds, out string? reason)
    {
        reason = null;

        if (!_busy.TryAdd(key, 0))
        {
            reason = "我还在想上一条呢，等我一下。";
            return false;
        }

        if (cooldownSeconds > 0)
        {
            var now = DateTime.UtcNow;
            if (_last.TryGetValue(key, out var last))
            {
                var elapsed = now - last;
                if (elapsed.TotalSeconds < cooldownSeconds)
                {
                    _busy.TryRemove(key, out _);
                    var wait = Math.Ceiling(cooldownSeconds - elapsed.TotalSeconds);
                    reason = $"别急，{wait:0} 秒后再问我。";
                    return false;
                }
            }
        }

        _last[key] = DateTime.UtcNow;
        return true;
    }

    public void End(string key) => _busy.TryRemove(key, out _);
}
