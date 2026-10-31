using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AIAgent.Memory;

/// <summary>
/// 工作记忆：当前这轮对话的最近若干轮 + 滚动摘要。
/// 超过阈值时把最早的若干轮交给压缩器浓缩成摘要，避免直接丢弃。
/// </summary>
public sealed class SessionStore
{
    private sealed class Conversation
    {
        public readonly List<ChatTurn> Turns = new();
        public DateTime LastActivity = DateTime.Now;
        public DateTime StartedAt = DateTime.Now;
        public string DisplayName = string.Empty;
        public string Summary = string.Empty;
    }

    private readonly ConcurrentDictionary<string, Conversation> _map = new();

    public void Append(string key, string displayName, string role, string content, int maxTurns = 40)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        var conversation = _map.GetOrAdd(key, _ => new Conversation());
        lock (conversation)
        {
            if (!string.IsNullOrWhiteSpace(displayName)) conversation.DisplayName = displayName;
            conversation.Turns.Add(new ChatTurn { Role = role, Content = content.Trim() });
            conversation.LastActivity = DateTime.Now;
            if (conversation.Turns.Count > maxTurns)
            {
                conversation.Turns.RemoveRange(0, conversation.Turns.Count - maxTurns);
            }
        }
    }

    public IReadOnlyList<ChatTurn> Recent(string key, int maxTurns)
    {
        if (!_map.TryGetValue(key, out var conversation)) return Array.Empty<ChatTurn>();

        lock (conversation)
        {
            var count = Math.Min(maxTurns, conversation.Turns.Count);
            return conversation.Turns.GetRange(conversation.Turns.Count - count, count);
        }
    }

    public string Summary(string key) =>
        _map.TryGetValue(key, out var conversation) ? ReadSummary(conversation) : string.Empty;

    private static string ReadSummary(Conversation conversation)
    {
        lock (conversation) return conversation.Summary;
    }

    public bool NeedsCompression(string key, int threshold)
    {
        if (threshold <= 0) return false;
        if (!_map.TryGetValue(key, out var conversation)) return false;
        lock (conversation) return conversation.Turns.Count > threshold;
    }

    /// <summary>取最早的若干轮（不删除），keep 表示要保留的最近轮数。</summary>
    public (List<ChatTurn> Turns, string Summary)? PeekOldest(string key, int keep)
    {
        if (!_map.TryGetValue(key, out var conversation)) return null;

        lock (conversation)
        {
            var count = conversation.Turns.Count - Math.Max(1, keep);
            if (count <= 0) return null;
            return (conversation.Turns.GetRange(0, count), conversation.Summary);
        }
    }

    public void DropOldest(string key, int count)
    {
        if (count <= 0) return;
        if (!_map.TryGetValue(key, out var conversation)) return;

        lock (conversation)
        {
            if (count >= conversation.Turns.Count) conversation.Turns.Clear();
            else conversation.Turns.RemoveRange(0, count);
        }
    }

    public void SetSummary(string key, string summary)
    {
        var conversation = _map.GetOrAdd(key, _ => new Conversation());
        lock (conversation) conversation.Summary = summary;
    }

    /// <summary>取走某人的整个会话（原子移除），用于抽取长期记忆。</summary>
    public (string Key, string DisplayName, List<ChatTurn> Turns, DateTime StartedAt)? Take(string key)
    {
        if (!_map.TryRemove(key, out var conversation)) return null;

        lock (conversation)
        {
            if (conversation.Turns.Count == 0) return null;
            return (key, conversation.DisplayName, new List<ChatTurn>(conversation.Turns), conversation.StartedAt);
        }
    }

    public IReadOnlyList<(string Key, string DisplayName, List<ChatTurn> Turns, DateTime StartedAt)> TakeIdle(TimeSpan idle)
    {
        var result = new List<(string, string, List<ChatTurn>, DateTime)>();
        var now = DateTime.Now;

        foreach (var pair in _map)
        {
            if (now - pair.Value.LastActivity < idle) continue;
            if (!_map.TryRemove(pair.Key, out var conversation)) continue;

            lock (conversation)
            {
                if (conversation.Turns.Count > 0)
                {
                    result.Add((pair.Key, conversation.DisplayName, new List<ChatTurn>(conversation.Turns), conversation.StartedAt));
                }
            }
        }

        return result;
    }

    public IReadOnlyList<(string Key, string DisplayName, string Summary, List<ChatTurn> Turns)> SnapshotAll()
    {
        var result = new List<(string, string, string, List<ChatTurn>)>();
        foreach (var pair in _map)
        {
            lock (pair.Value)
            {
                if (pair.Value.Turns.Count == 0 && string.IsNullOrWhiteSpace(pair.Value.Summary)) continue;
                result.Add((pair.Key, pair.Value.DisplayName, pair.Value.Summary, new List<ChatTurn>(pair.Value.Turns)));
            }
        }
        return result;
    }

    public void Restore(string key, string displayName, string summary, IReadOnlyList<ChatTurn> turns)
    {
        var conversation = _map.GetOrAdd(key, _ => new Conversation());
        lock (conversation)
        {
            conversation.DisplayName = displayName ?? string.Empty;
            conversation.Summary = summary ?? string.Empty;
            conversation.Turns.Clear();
            conversation.Turns.AddRange(turns);
            conversation.LastActivity = DateTime.Now;
        }
    }

    public void Clear(string key) => _map.TryRemove(key, out _);

    /// <summary>查看空闲会话（不删除），抽取成功后再丢弃。</summary>
    public IReadOnlyList<(string Key, string DisplayName, List<ChatTurn> Turns)> PeekIdle(TimeSpan idle)
    {
        var result = new List<(string, string, List<ChatTurn>)>();
        var now = DateTime.Now;

        foreach (var pair in _map)
        {
            lock (pair.Value)
            {
                if (now - pair.Value.LastActivity < idle) continue;
                if (pair.Value.Turns.Count == 0) continue;
                result.Add((pair.Key, pair.Value.DisplayName, new List<ChatTurn>(pair.Value.Turns)));
            }
        }

        return result;
    }

    public int Count => _map.Count;
}
