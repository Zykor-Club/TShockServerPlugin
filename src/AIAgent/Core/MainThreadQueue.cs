using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIAgent.Core;

/// <summary>
/// 主线程执行队列。
/// Terraria 是单线程游戏循环：任何对 Main.player / Main.npc / Main.tile 的读写
/// 都必须回到主线程进行，否则会与主循环竞争，轻则数据错乱，重则崩服。
/// </summary>
public sealed class MainThreadQueue
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<(DateTime Due, Action Action)> _delayed = new();
    private readonly object _delayGate = new();
    private long _posted;
    private long _pumped;

    public int Depth => _queue.Count;

    public long Posted => Interlocked.Read(ref _posted);

    public long Pumped => Interlocked.Read(ref _pumped);

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Enqueue(action);
        Interlocked.Increment(ref _posted);
    }

    /// <summary>延迟到主线程执行。用于让 AI 的气泡一句一句出现。</summary>
    public void PostDelayed(Action action, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (delay <= TimeSpan.Zero)
        {
            Post(action);
            return;
        }

        lock (_delayGate)
        {
            _delayed.Add((DateTime.UtcNow + delay, action));
        }
        Interlocked.Increment(ref _posted);
    }

    private int DrainDelayed(int budget)
    {
        var due = new List<Action>();
        var now = DateTime.UtcNow;

        // 只取到期且不超过本帧预算的任务；没取到的一律留在队列里，绝不丢弃
        lock (_delayGate)
        {
            for (var i = 0; i < _delayed.Count;)
            {
                if (due.Count >= budget) break;

                if (_delayed[i].Due > now)
                {
                    i++;
                    continue;
                }

                due.Add(_delayed[i].Action);
                _delayed.RemoveAt(i);
            }
        }

        var processed = 0;
        foreach (var action in due)
        {
            processed++;
            try { action(); }
            catch (Exception ex) { PluginLog.Error("延迟任务异常", ex); }
            Interlocked.Increment(ref _pumped);
        }
        return processed;
    }

    /// <summary>
    /// 把委托排到主线程执行，并等待结果。调用方通常在线程池上（await 之后）。
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> func, int timeoutMs = 5000)
    {
        ArgumentNullException.ThrowIfNull(func);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new StrongBox<bool>(false);

        Post(() =>
        {
            // 已经超时取消的任务不要再执行（避免副作用重复生效）
            if (Volatile.Read(ref cancelled.Value)) return;

            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });

        return WaitAsync(tcs.Task, timeoutMs, cancelled);
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, int timeoutMs, StrongBox<bool> cancelled)
    {
        var delay = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (completed != task)
        {
            Volatile.Write(ref cancelled.Value, true);
            throw new TimeoutException($"主线程执行超时（{timeoutMs}ms）");
        }

        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// 每帧排空队列，budget 用来限制单帧处理量，避免卡帧。
    /// </summary>
    public int Pump(int budget = 32)
    {
        var processed = DrainDelayed(budget);
        while (processed < budget && _queue.TryDequeue(out var action))
        {
            processed++;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PluginLog.Error("主线程队列任务抛出异常", ex);
            }
            Interlocked.Increment(ref _pumped);
        }
        return processed;
    }
}
