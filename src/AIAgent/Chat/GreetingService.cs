using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;
using AIAgent.Config;
using AIAgent.Core;

namespace AIAgent.Chat;

/// <summary>
/// 主动问候：玩家进服后等几秒，条件都满足才开口。
/// 记录落在 greetings.json，重启不会重复打招呼。
/// </summary>
public static class GreetingService
{
    private static readonly ConcurrentDictionary<string, DateTime> LastGreeted = new(StringComparer.Ordinal);
    private static readonly object SaveGate = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var path = StorePath();
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path, Encoding.UTF8));
            if (saved is null) return;
            foreach (var pair in saved) LastGreeted[pair.Key] = pair.Value;
        }
        catch (Exception ex)
        {
            PluginLog.Warn("问候记录读取失败: " + ex.Message);
        }
    }

    public static void OnPlayerGreeted(TSPlayer player)
    {
        if (!PluginState.Config.Greeting.Enabled) return;
        _ = GreetAsync(player);
    }

    private static async Task GreetAsync(TSPlayer player)
    {
        var cfg = PluginState.Config.Greeting;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, cfg.DelaySeconds))).ConfigureAwait(false);

            // 客户端还在加载时发出去，玩家是看不到的：等握手完成，最多再等 30 秒
            for (var i = 0; i < 10 && player.Active && !player.FinishedHandshake; i++)
            {
                await Task.Delay(3000).ConfigureAwait(false);
            }

            if (!player.Active) return;
            if (!player.HasPermission("aiagent.use")) return;
            if (player.TPlayer is null || player.TPlayer.dead) return;

            var subject = PluginState.Players.Resolve(player);
            var key = subject?.Key ?? player.Name;

            LastGreeted.TryGetValue(key, out var last);
            if (!GreetingPolicy.Allowed(DateTime.Now, last, cfg)) return;
            if (await IsBossFightAsync().ConfigureAwait(false)) return;

            // 先记账，避免并发重复问候
            LastGreeted[key] = DateTime.Now;
            TrySave();

            if (PluginState.Config.DebugLog) PluginLog.Info($"主动问候: {player.Name}");

            var mode = cfg.Public ? ChatReplyMode.Public : ChatReplyMode.Private;
            var prompt = $"（系统事件）{player.Name} 刚刚上线。先明确欢迎他回来（一句话），最多 {cfg.MaxLength} 字；" +
                         "如果有答应过但没做完的事，可以顺口提一句，不要连续追问。";

            var ok = await ChatService.HandleAsync(player, prompt, mode, isEvent: true).ConfigureAwait(false);
            if (!ok)
            {
                // 没发出去就撤销记录，下次进服再试
                LastGreeted.TryRemove(key, out _);
                TrySave();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error("主动问候失败", ex);
        }
    }

    private static async Task<bool> IsBossFightAsync()
    {
        try
        {
            return await PluginState.MainThread
                .InvokeAsync(() => Main.npc.Any(n => n.active && n.boss), 1500)
                .ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static string StorePath() => Path.Combine(PluginState.DataDirectory, "greetings.json");

    private static void TrySave()
    {
        try
        {
            lock (SaveGate)
            {
                File.WriteAllText(StorePath(), JsonSerializer.Serialize(LastGreeted), new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warn("问候记录保存失败: " + ex.Message);
        }
    }
}
