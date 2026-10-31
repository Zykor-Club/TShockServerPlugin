using System;
using System.Collections.Concurrent;
using System.Text;

namespace AIAgent.Llm;

/// <summary>
/// 每日 token 计量。M1 先放内存（重启清零），M2 会随记忆一起落盘。
/// 估算函数只作为兜底：优先用 API 返回的 usage 真值。
/// </summary>
public sealed class UsageMeter
{
    private sealed class Bucket
    {
        public DateTime Day = DateTime.Today;
        public long Tokens;
        public int Calls;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public long TokensToday(string key)
    {
        if (!_buckets.TryGetValue(key, out var bucket)) return 0;
        lock (bucket)
        {
            if (bucket.Day != DateTime.Today) return 0;
            return bucket.Tokens;
        }
    }

    public int CallsToday(string key)
    {
        if (!_buckets.TryGetValue(key, out var bucket)) return 0;
        lock (bucket)
        {
            if (bucket.Day != DateTime.Today) return 0;
            return bucket.Calls;
        }
    }

    public bool HasBudget(string key, long dailyLimit)
    {
        if (dailyLimit <= 0) return true;
        return TokensToday(key) < dailyLimit;
    }

    public void Record(string key, int promptTokens, int completionTokens)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        lock (bucket)
        {
            if (bucket.Day != DateTime.Today)
            {
                bucket.Day = DateTime.Today;
                bucket.Tokens = 0;
                bucket.Calls = 0;
            }

            bucket.Tokens += promptTokens + completionTokens;
            bucket.Calls++;
        }
    }

    /// <summary>
    /// 兜底估算：中日韩字符约 1 token/字，其余约 4 字符/token。
    /// 旧版把非 ASCII 记成 1、ASCII 记成 25 再除 100，导致中文偏小约 100 倍。
    /// </summary>
    public static long Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        long wide = 0;
        long narrow = 0;
        foreach (var ch in text)
        {
            if (ch > 0x7F) wide++;
            else narrow++;
        }

        return wide + (narrow + 3) / 4;
    }

    public static long Estimate(IEnumerable<string> texts)
    {
        long total = 0;
        foreach (var text in texts) total += Estimate(text);
        return total;
    }
}
