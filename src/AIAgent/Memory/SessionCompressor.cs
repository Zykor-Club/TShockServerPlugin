using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIAgent.Core;
using AIAgent.Llm;

namespace AIAgent.Memory;

/// <summary>
/// 长会话压缩：把最早的若干轮浓缩进滚动摘要。
/// 先取后删——压缩失败时保留原对话，不会丢内容。
/// </summary>
public sealed class SessionCompressor
{
    private readonly ConcurrentDictionary<string, byte> _busy = new(StringComparer.Ordinal);

    public async Task<bool> CompressAsync(string key, CancellationToken ct)
    {
        var cfg = PluginState.Config;
        if (!cfg.Memory.Enabled) return false;

        var threshold = cfg.Memory.SummaryTriggerTurns;
        if (!PluginState.Sessions.NeedsCompression(key, threshold)) return false;
        if (!_busy.TryAdd(key, 0)) return false;

        try
        {
            var peek = PluginState.Sessions.PeekOldest(key, cfg.Memory.KeepRecentTurns);
            if (peek is null) return false;

            var (oldTurns, existing) = peek.Value;
            if (oldTurns.Count < 2) return false;

            var provider = cfg.Providers.CurrentProvider;
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey)) return false;

            var client = new OpenAiCompatClient(provider.BaseUrl, provider.ApiKey);
            var options = new ChatOptions(
                provider.BaseUrl,
                provider.ApiKey,
                string.IsNullOrWhiteSpace(cfg.ExtractModel) ? cfg.Model : cfg.ExtractModel,
                new[] { ChatMessage.User(BuildPrompt(oldTurns, existing)) },
                Temperature: 0,
                MaxTokens: 500,
                Stream: false,
                MaxTokensParam: provider.MaxTokensParam,
                SupportsTemperature: provider.SupportsTemperature,
                SupportsStreamUsage: false);

            var result = await client.SendAsync(options, null, ct).ConfigureAwait(false);
            var summary = (result.Text ?? string.Empty).Trim();
            if (summary.Length < 10) return false;

            PluginState.Sessions.DropOldest(key, oldTurns.Count);
            PluginState.Sessions.SetSummary(key, summary);

            PluginLog.Info($"{key} 会话摘要已更新（压缩 {oldTurns.Count} 轮，摘要 {summary.Length} 字）");
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error("会话压缩失败（保留原对话）", ex);
            return false;
        }
        finally
        {
            _busy.TryRemove(key, out _);
        }
    }

    private static string BuildPrompt(IReadOnlyList<ChatTurn> turns, string existing)
    {
        var sb = new StringBuilder();
        sb.AppendLine("把下面这段游戏聊天压缩成一段不超过 200 字的摘要。");
        sb.AppendLine("必须保留：玩家说过的关键事实、约定、情绪、还没做完的事。用第三人称中文。");
        sb.AppendLine("只输出摘要正文，不要解释，不要加前缀。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(existing))
        {
            sb.AppendLine("已有摘要（要和新内容合并）：");
            sb.AppendLine(existing);
            sb.AppendLine();
        }

        sb.AppendLine("新增对话：");
        foreach (var turn in turns)
        {
            sb.AppendLine((turn.Role == "assistant" ? "AI: " : "玩家: ") + turn.Content);
        }

        return sb.ToString();
    }
}
