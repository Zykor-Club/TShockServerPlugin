using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AIAgent.Chat;

/// <summary>
/// 记录聊天活动：谁刚说过话、AI 刚回复过谁、最近几条公屏内容。
/// 仲裁器靠这些做"对话延续 / 人多闭嘴 / 冷场加分"，提示词靠它提供上下文。
/// </summary>
public static class ChatActivity
{
    private sealed record Line(DateTime At, string Key, string Name, string Text);

    private static readonly ConcurrentDictionary<string, DateTime> LastReplyTo = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly Queue<Line> Recent = new();
    private static DateTime _lastReplyAnywhere = DateTime.MinValue;
    private static DateTime _lastAnyMessage = DateTime.MinValue;
    private static DateTime _replyDay = DateTime.Today;
    private static int _repliesToday;

    public static void RecordPlayerMessage(string key, string name, string text)
    {
        var now = DateTime.UtcNow;
        _lastAnyMessage = now;

        lock (Gate)
        {
            Recent.Enqueue(new Line(now, key, name, text));
            while (Recent.Count > 50) Recent.Dequeue();
        }
    }

    public static void RecordReply(string key)
    {
        var now = DateTime.UtcNow;
        LastReplyTo[key] = now;
        _lastReplyAnywhere = now;

        lock (Gate)
        {
            if (_replyDay != DateTime.Today)
            {
                _replyDay = DateTime.Today;
                _repliesToday = 0;
            }
            _repliesToday++;
        }
    }

    public static int RepliesToday()
    {
        lock (Gate)
        {
            return _replyDay == DateTime.Today ? _repliesToday : 0;
        }
    }

    public static bool IsRecentPartner(string key, int windowSeconds)
    {
        if (!LastReplyTo.TryGetValue(key, out var at)) return false;
        return (DateTime.UtcNow - at).TotalSeconds <= Math.Max(1, windowSeconds);
    }

    public static double SecondsSinceReplyTo(string key) =>
        LastReplyTo.TryGetValue(key, out var at) ? (DateTime.UtcNow - at).TotalSeconds : double.MaxValue;

    public static double SecondsSinceAnyReply() =>
        _lastReplyAnywhere == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastReplyAnywhere).TotalSeconds;

    public static double MinutesSinceLastMessage() =>
        _lastAnyMessage == DateTime.MinValue ? 999 : (DateTime.UtcNow - _lastAnyMessage).TotalMinutes;

    public static int OthersWithin(string key)
    {
        var since = DateTime.UtcNow.AddSeconds(-8);
        lock (Gate)
        {
            return Recent.Count(item => item.At >= since && !string.Equals(item.Key, key, StringComparison.Ordinal));
        }
    }

    /// <summary>最近一个说话的玩家名（用于主动消息挑人）。</summary>
    public static string? LastSpeaker()
    {
        lock (Gate)
        {
            return Recent.Count == 0 ? null : Recent.Last().Name;
        }
    }

    /// <summary>最近的公屏内容，给模型当上下文（不含当前这条）。</summary>
    public static IReadOnlyList<string> RecentLines(int count, string? excludeText = null)
    {
        lock (Gate)
        {
            return Recent
                .Where(item => excludeText is null || item.Text.Trim() != excludeText.Trim())
                .TakeLast(Math.Max(1, count))
                .Select(item => $"{item.Name}: {item.Text}")
                .ToList();
        }
    }
}
