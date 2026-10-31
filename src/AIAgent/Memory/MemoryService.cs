using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIAgent.Core;

namespace AIAgent.Memory;

/// <summary>会话收尾：把工作记忆交给抽取器，浓缩成长期记忆。</summary>
public static class MemoryService
{
    public static async Task<bool> FinalizeAsync(string account, string? displayName = null)
    {
        if (!PluginState.Config.Memory.Enabled) return false;

        var taken = PluginState.Sessions.Take(account);
        if (taken is null) return false;

        return await ExtractAndLog(account, taken.Value.DisplayName, taken.Value.Turns).ConfigureAwait(false);
    }

    public static void FinalizeIdle(int idleMinutes)
    {
        if (!PluginState.Config.Memory.Enabled) return;
        if (idleMinutes <= 0) return;

        var idle = TimeSpan.FromMinutes(idleMinutes);
        foreach (var item in PluginState.Sessions.PeekIdle(idle))
        {
            _ = ExtractIdleAsync(item.Key, item.DisplayName, item.Turns);
        }
    }

    /// <summary>空闲抽取：成功才丢弃已抽取的轮次，失败保留。</summary>
    private static async Task ExtractIdleAsync(string account, string displayName, IReadOnlyList<ChatTurn> turns)
    {
        if (turns.Count < 4) return;

        try
        {
            var ok = await PluginState.Extractor
                .ExtractAsync(account, string.IsNullOrWhiteSpace(displayName) ? account : displayName, turns, CancellationToken.None)
                .ConfigureAwait(false);

            if (ok)
            {
                PluginState.Sessions.DropOldest(account, turns.Count);
                PluginLog.Info($"空闲记忆已更新: {account}（{turns.Count} 轮）");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"空闲记忆抽取失败: {account}", ex);
        }
    }

    private static async Task<bool> ExtractAndLog(string account, string displayName, IReadOnlyList<ChatTurn> turns)
    {
        if (turns.Count < 4) return false;

        try
        {
            var ok = await PluginState.Extractor
                .ExtractAsync(account, string.IsNullOrWhiteSpace(displayName) ? account : displayName, turns, CancellationToken.None)
                .ConfigureAwait(false);
            if (ok) PluginLog.Info($"记忆已更新: {account}（{turns.Count} 轮）");
            return ok;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"记忆抽取失败: {account}", ex);
            return false;
        }
    }
}
