using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;
using AIAgent.Config;
using AIAgent.Core;
using AIAgent.Llm;
using AIAgent.Memory;
using AIAgent.Persona;

namespace AIAgent.Chat;

public enum ChatReplyMode
{
    Private,
    Public,
}

/// <summary>
/// 一次对话的完整流程：校验 → 限流 → 预算 → 提示词 → 请求 → 工具调用 → 逐句发送 → 计量。
/// 带工具时先缓冲整段回复（因为可能要执行工具后再问一次），不带工具时直接流式显示。
/// </summary>
public static class ChatService
{
    private const int MaxToolSteps = 3;

    public static async Task<bool> HandleAsync(
        TSPlayer player,
        string input,
        ChatReplyMode mode = ChatReplyMode.Private,
        bool isEvent = false)
    {
        var cfg = PluginState.Config;
        var reqId = Interlocked.Increment(ref PluginState.ReqId);
        var subject = PluginState.Players.Resolve(player);
        var key = subject?.Key ?? player.Name;
        var playerName = player.Name;

        if (string.IsNullOrWhiteSpace(input)) return false;

        if (!isEvent && cfg.MaxInputLength > 0 && input.Length > cfg.MaxInputLength)
        {
            ReplyPrivate(player, $"你说的有点长（{input.Length} 字），压到 {cfg.MaxInputLength} 字以内再说一遍？");
            return false;
        }

        var provider = cfg.Providers.CurrentProvider;
        if (provider is null)
        {
            ReplyPrivate(player, "管理员还没给我配好供应商，等我一下。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            ReplyPrivate(player, "管理员还没给我配密钥，我现在还不会说话。");
            return false;
        }

        if (!PluginState.Usage.HasBudget(key, cfg.DailyTokenBudget))
        {
            ReplyPrivate(player, "我今天说得太多了，明天再聊吧。");
            return false;
        }

        var arbitrationOff = !cfg.Chat.ArbitrationEnabled;
        var rateKey = isEvent ? key + "#event" : key;
        if (!PluginState.Limiter.TryBegin(rateKey, isEvent || arbitrationOff ? 0 : cfg.CooldownSeconds, out var reason))
        {
            if (!string.IsNullOrEmpty(reason) && !isEvent) ReplyPrivate(player, reason);
            return false;
        }

        try
        {
            if (arbitrationOff && !isEvent && cfg.Chat.NoArbitrationDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(cfg.Chat.NoArbitrationDelaySeconds)).ConfigureAwait(false);
            }

            // 后台压缩：不阻塞当前消息，摘要下一次请求生效
            if (cfg.Memory.Enabled && PluginState.Sessions.NeedsCompression(key, cfg.Memory.SummaryTriggerTurns))
            {
                _ = PluginState.Compressor.CompressAsync(key, CancellationToken.None);
            }

            var memoryBlock = string.Empty;
            var affinity = 50;
            if (cfg.Memory.Enabled)
            {
                var record = PluginState.Memory.Load(key);
                affinity = record.Profile.Affinity;
                memoryBlock = MemoryRetriever.BuildBlock(record, input, cfg.Memory.MemoryBudgetTokens);
            }

            var recentChat = ChatActivity.RecentLines(6, input);
            var sessionSummary = cfg.Memory.Enabled ? PluginState.Sessions.Summary(key) : string.Empty;
            var emotionHint = EmotionDetector.Hint(EmotionDetector.Detect(isEvent ? string.Empty : input));
            var careHint = isEvent ? string.Empty : CareService.LateNightHint(key, DateTime.Now, cfg.Care);
            var taskHint = BuildTaskHint(input);
            var system = PromptBuilder.BuildSystem(
                cfg, playerName, subject?.GroupName, DateTime.Now,
                memoryBlock, PluginState.Persona, Crafting.WorldProgress.CachedBrief,
                emotionHint, sessionSummary, recentChat, careHint, affinity, taskHint);
            var messages = new List<ChatMessage> { ChatMessage.System(system) };

            if (cfg.Memory.Enabled && !isEvent)
            {
                foreach (var turn in PluginState.Sessions.Recent(key, cfg.Memory.KeepRecentTurns))
                {
                    messages.Add(new ChatMessage { Role = turn.Role, Content = turn.Content });
                }
            }

            messages.Add(ChatMessage.User(input));

            var toolSchema = cfg.EnableTools
                ? await BuildSchemaOnMainAsync(player).ConfigureAwait(false)
                : new JsonArray();
            var useTools = toolSchema.Count > 0;

            var client = new OpenAiCompatClient(provider.BaseUrl, provider.ApiKey);
            var emitter = new BubbleEmitter(player, cfg, mode);
            var started = DateTime.UtcNow;
            var promptTokens = 0;
            var completionTokens = 0;
            var finalText = string.Empty;

            PluginLog.Info($"{PluginLog.Req(reqId)} {playerName} {(isEvent ? "事件" : "提问")} {input.Length} 字 → {cfg.Model} | 工具 {(useTools ? toolSchema.Count : 0)} 个");

            for (var step = 0; step < MaxToolSteps; step++)
            {
                var options = new ChatOptions(
                    provider.BaseUrl,
                    provider.ApiKey,
                    cfg.Model,
                    messages,
                    cfg.Temperature,
                    MaxTokens: 0,
                    Stream: cfg.EnableStream,
                    MaxTokensParam: provider.MaxTokensParam,
                    SupportsTemperature: provider.SupportsTemperature,
                    SupportsStreamUsage: provider.SupportsStreamUsage,
                    Tools: useTools ? toolSchema : null);

                var result = await client
                    .SendAsync(options, useTools ? null : delta => emitter.Push(delta), CancellationToken.None)
                    .ConfigureAwait(false);

                promptTokens += result.Usage.PromptTokens;
                completionTokens += result.Usage.CompletionTokens;

                if (result.ToolCalls.Count > 0)
                {
                    // 调工具前的"我看看"也给玩家看到，别吞掉
                    if (!string.IsNullOrWhiteSpace(result.Text)) emitter.Push(result.Text);

                    var assistant = new ChatMessage { Role = "assistant", Content = result.Text ?? string.Empty };
                    foreach (var call in result.ToolCalls)
                    {
                        assistant.ToolCalls ??= new List<ToolCallPayload>();
                        assistant.ToolCalls.Add(new ToolCallPayload
                        {
                            Id = call.Id,
                            Function = new FunctionPayload { Name = call.Name, Arguments = call.Arguments },
                        });
                    }
                    messages.Add(assistant);

                    var batch = await PluginState.Tools
                        .HandleAsync(player, key, result.ToolCalls, reqId)
                        .ConfigureAwait(false);

                    messages.AddRange(batch.Messages);
                    continue;
                }

                finalText = result.Text ?? string.Empty;
                if (useTools) emitter.Push(finalText);
                emitter.Flush();
                break;
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                // 轮次用完还没正文：强制一次不带工具的收尾，保证玩家拿到回答
                var closing = new ChatOptions(
                    provider.BaseUrl, provider.ApiKey, cfg.Model, messages,
                    cfg.Temperature, 0, cfg.EnableStream,
                    provider.MaxTokensParam, provider.SupportsTemperature, provider.SupportsStreamUsage,
                    Tools: null);
                var last = await client.SendAsync(closing, useTools ? null : delta => emitter.Push(delta), CancellationToken.None).ConfigureAwait(false);
                promptTokens += last.Usage.PromptTokens;
                completionTokens += last.Usage.CompletionTokens;
                finalText = last.Text ?? string.Empty;
                if (useTools) emitter.Push(finalText);
                emitter.Flush();
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                ReplyPrivate(player, "我想复杂了，换个说法再来一次？");
                return false;
            }

            ChatActivity.RecordReply(key);

            if (cfg.Memory.Enabled && !isEvent)
            {
                PluginState.Sessions.Append(key, playerName, "user", input);
                PluginState.Sessions.Append(key, playerName, "assistant", finalText);
            }

            if (promptTokens == 0 && completionTokens == 0)
            {
                promptTokens = (int)UsageMeter.Estimate(system + input);
                completionTokens = (int)UsageMeter.Estimate(finalText);
            }

            PluginState.Usage.Record(key, promptTokens, completionTokens);

            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
            PluginLog.Info($"{PluginLog.Req(reqId)} 完成 {elapsed:0}ms | tokens {promptTokens + completionTokens} | " +
                           $"今日 {PluginState.Usage.TokensToday(key)}");
            return true;
        }
        catch (OperationCanceledException)
        {
            ReplyPrivate(player, "我先不说了。");
            return false;
        }
        catch (LlmException ex)
        {
            PluginLog.Error($"{PluginLog.Req(reqId)} {ex.Message}", ex);
            ReplyPrivate(player, ex.UserMessage);
            return false;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"{PluginLog.Req(reqId)} 未预期异常", ex);
            ReplyPrivate(player, "我刚才走神了，再说一遍？");
            return false;
        }
        finally
        {
            PluginState.Limiter.End(rateKey);
        }
    }

    private static async Task<JsonArray> BuildSchemaOnMainAsync(TSPlayer player)
    {
        try
        {
            return await PluginState.MainThread
                .InvokeAsync(() => PluginState.Tools.Registry.BuildSchemaFor(player), 2000)
                .ConfigureAwait(false);
        }
        catch
        {
            return PluginState.Tools.Registry.BuildSchemaFor(player);
        }
    }

    /// <summary>识别"在问配方"的消息，强制模型先查游戏数据。</summary>
    private static string BuildTaskHint(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string[] markers =
        {
            "怎么做", "怎么合成", "怎么造", "合成", "配方", "材料", "需要什么", "要什么",
            "几把", "几个", "哪里做", "在哪做", "工作站", "用什么做", "怎么获得",
        };

        var recipeHit = markers.Any(marker => input.Contains(marker, StringComparison.Ordinal));
        if (recipeHit)
        {
            return "这条消息在问配方或材料：回答前必须先调用 get_recipe 查游戏数据；查不到就说不知道，绝对不要凭记忆报数量或工作站。";
        }

        string[] summonMarkers =
        {
            "召唤", "怎么召唤", "召唤条件", "怎么打", "触发条件", "出现条件", "在哪打", "怎么遇到",
        };
        if (summonMarkers.Any(marker => input.Contains(marker, StringComparison.Ordinal)))
        {
            return "这条在问 Boss 的召唤方式或出现条件：先调用 search_wiki 查该 Boss 页面，按原文回答；" +
                   "原文没写的细节不要补充（例如凭空说【戴上】），查不到就说查不到。";
        }

        return string.Empty;
    }

    private static void ReplyPrivate(TSPlayer player, string text) => Reply(player, text, ChatReplyMode.Private);

    private static void Reply(TSPlayer player, string text, ChatReplyMode mode)
    {
        var line = $"[c/FF69B4:[{PluginState.Config.AIName}]] {text}";
        PluginState.MainThread.Post(() =>
        {
            try
            {
                if (mode == ChatReplyMode.Public)
                {
                    TSPlayer.All.SendMessage(line, 255, 105, 180);
                }
                else if (player.Active)
                {
                    player.SendInfoMessage(line);
                }
            }
            catch
            {
                // 玩家已离线
            }
        });
    }

    /// <summary>把流式增量按句切开发送，看起来像人一句一句说。</summary>
    private sealed class BubbleEmitter
    {
        private readonly TSPlayer _player;
        private readonly PluginConfig _cfg;
        private readonly ChatReplyMode _mode;
        private readonly StringBuilder _buffer = new();
        private int _sentChars;
        private int _bubbleIndex;
        private bool _done;
        private bool _truncated;

        public BubbleEmitter(TSPlayer player, PluginConfig cfg, ChatReplyMode mode)
        {
            _player = player;
            _cfg = cfg;
            _mode = mode;
        }

        public void Push(string delta)
        {
            if (_done || string.IsNullOrEmpty(delta)) return;
            _buffer.Append(delta);
            Pump(final: false);
        }

        public void Flush()
        {
            if (_done) return;
            Pump(final: true);
            _done = true;
            if (_truncated) Post("……剩下的下次再说。");
        }

        private void Pump(bool final)
        {
            while (!_done)
            {
                var text = _buffer.ToString();
                var cut = FindCut(text, final);
                if (cut <= 0) return;

                var piece = text.Substring(0, cut).Trim();
                _buffer.Remove(0, cut);

                if (piece.Length == 0) continue;
                if (!Send(piece)) return;
                if (!final && _buffer.Length < 8) return;
            }
        }

        private int FindCut(string text, bool final)
        {
            for (var i = 6; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch is '。' or '！' or '？' or '!' or '?' or '\n') return i + 1;
                if ((ch is '；' or ';' || ch == '，') && i >= 40) return i + 1;
            }

            if (text.Length >= _cfg.BubbleLength) return _cfg.BubbleLength;
            return final && text.Length > 0 ? text.Length : -1;
        }

        private bool Send(string piece)
        {
            var clean = OutputPostProcessor.Clean(piece, _cfg.BubbleLength);
            if (clean.Length == 0) return true;

            if (_sentChars >= _cfg.MaxResponseLength)
            {
                _truncated = true;
                _done = true;
                return false;
            }

            var remaining = _cfg.MaxResponseLength - _sentChars;
            if (clean.Length > remaining)
            {
                clean = clean.Substring(0, remaining).TrimEnd() + "…";
                _truncated = true;
            }

            _sentChars += clean.Length;
            Post(clean);

            if (_truncated)
            {
                _done = true;
                return false;
            }

            return true;
        }

        private void Post(string clean)
        {
            var line = $"[c/FF69B4:[{_cfg.AIName}]] {clean}";
            var delay = TimeSpan.FromMilliseconds(Math.Max(0, _cfg.BubbleDelayMs) * _bubbleIndex);
            _bubbleIndex++;

            PluginState.MainThread.PostDelayed(() =>
            {
                try
                {
                    if (_cfg.DebugLog) PluginLog.Info($"[发送 {DateTime.Now:HH:mm:ss.fff}] {clean}");
                    if (_mode == ChatReplyMode.Public)
                    {
                        TSPlayer.All.SendMessage(line, 255, 105, 180);
                    }
                    else if (_player.Active)
                    {
                        _player.SendInfoMessage(line);
                    }
                }
                catch
                {
                    // 玩家已离线
                }
            }, delay);
        }
    }
}
