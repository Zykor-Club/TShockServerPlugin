using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;

namespace AIAgent;

public static class AIHandler
{
    public static async Task HandleAI(TSPlayer plr, Session session, string content)
    {
        var id = Interlocked.Increment(ref PluginState.ReqId);
        var cfg = PluginState.Config;
        try
        {
            if (cfg.MaxInputLength > 0)
            {
                var inputLen = ChatUtils.CountCharacters(content);
                if (inputLen > cfg.MaxInputLength)
                {
                    plr.SendErrorMessage($"[AIAgent] 你的输入内容过长 ({inputLen}字)，超过了最大限制{cfg.MaxInputLength}字。");
                    if (!cfg.SimpleMode)
                        plr.SendInfoMessage("请缩短你的问题，或使用 /aig say 配合更精确的表达。");
                    return;
                }
            }

            if (!cfg.SimpleMode)
                plr.SendInfoMessage($"[c/00BFFF:[请求 #{id}]] {cfg.AIName} 正在思考中，请稍候...");

            await CompressContextIfNeeded(plr, session);

            lock (session.HistoryLock)
            {
                if (!session.History.Any())
                    session.History.Add(new ChatMessage { Role = "system", Content = ChatUtils.BuildSystemPrompt(plr.Name) });
                else
                    UpdatePersonaIfChanged(session);
                session.History.Add(new ChatMessage { Role = "user", Content = content });
            }

            bool useStream = cfg.EnableStream;
            var req = new ChatRequest
            {
                Model = cfg.Model,
                Messages = session.History.ToList(),
                Stream = useStream
            };

            if (cfg.ResponseLimitMode == "limit" && cfg.MaxResponseLength > 0)
                req.MaxTokens = ChatUtils.EstimateTokensFromChars(cfg.MaxResponseLength);

            if (cfg.EnableWebSearch && IsWebSearchSupported(cfg.Model))
                req.Tools = new List<ChatTool> { new() };

            var json = JsonSerializer.Serialize(req);
            var request = new HttpRequestMessage(HttpMethod.Post, ChatUtils.GetChatCompletionsUrl())
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            if (useStream)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            HttpResponseMessage resp;
            try
            {
                resp = await PluginState.Client.SendAsync(request,
                    useStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    PluginState.Cts.Token);
            }
            catch (OperationCanceledException) when (PluginState.Cts.IsCancellationRequested)
            { plr.SendErrorMessage("[AIAgent] 插件正在卸载，请求已取消。"); return; }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                TShock.Log.Error($"[AIAgent] 请求 #{id} 超时: {stopwatch.ElapsedMilliseconds}ms");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: API响应超时。");
                if (!cfg.SimpleMode)
                {
                    plr.SendInfoMessage($"响应时间: {stopwatch.ElapsedMilliseconds}ms，超过{PluginState.Client.Timeout.TotalSeconds}秒限制。");
                    plr.SendInfoMessage("建议: 1.检查网络连接 2.更换响应更快的模型 3.关闭流式传输: /aig set stream no");
                }
                return;
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                TShock.Log.Error($"[AIAgent] 请求 #{id} 网络错误: {ex.Message}");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: 网络连接错误。");
                if (!cfg.SimpleMode) { plr.SendInfoMessage($"错误信息: {ex.Message}"); }
                return;
            }
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            string respBody;
            try { respBody = await resp.Content.ReadAsStringAsync(); }
            catch (Exception ex)
            {
                TShock.Log.Error($"[AIAgent] 请求 #{id} 读取响应失败: {ex.Message} | 状态码: {resp.StatusCode}");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: 无法读取API响应内容。");
                return;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var apiError = respBody;
                try { using var doc = JsonDocument.Parse(respBody); if (doc.RootElement.TryGetProperty("error", out var err)) { if (err.TryGetProperty("message", out var msg)) apiError = msg.GetString() ?? apiError; } } catch { }
                TShock.Log.Error($"[AIAgent] 请求 #{id} API错误: {apiError} | 状态码: {(int)resp.StatusCode}");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: {apiError}");
                if (!cfg.SimpleMode && (apiError.Contains("no access to model") || apiError.Contains("not supported")))
                    plr.SendInfoMessage("你的令牌没有该模型的访问权限，或该模型已停用。");
                return;
            }

            string aiResp;
            if (useStream)
            {
                aiResp = await ParseStreamResponseAsync(resp, plr, id, elapsedMs);
                if (string.IsNullOrWhiteSpace(aiResp)) return;
            }
            else
            {
                aiResp = ParseNonStreamResponse(respBody, id, elapsedMs);
                if (string.IsNullOrWhiteSpace(aiResp))
                {
                    TShock.Log.Error($"[AIAgent] 请求 #{id} 非流式响应解析为空");
                    plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: 无法解析AI响应。");
                    return;
                }
            }

            bool emailSent = false;
            if (cfg.EnableMailFeature && aiResp.Contains(">>>SEND_EMAIL<<<"))
            {
                var (email, mailContent) = ChatUtils.ExtractEmailInfo(aiResp);
                if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(mailContent))
                {
                    if (ChatUtils.SendEmail(plr, email, mailContent))
                    { emailSent = true; plr.SendSuccessMessage($"[AIAgent] 邮件已成功发送至 {email}"); }
                }
            }

            string displayResp = aiResp;
            bool wasTruncated = false;
            if (cfg.ResponseLimitMode == "truncate" && cfg.MaxResponseLength > 0)
            {
                if (ChatUtils.CountCharacters(displayResp) > cfg.MaxResponseLength)
                {
                    displayResp = ChatUtils.TruncateToChars(displayResp, cfg.MaxResponseLength);
                    wasTruncated = true;
                }
            }

            var cleanDisplay = displayResp;
            if (cleanDisplay.Contains(">>>SEND_EMAIL<<<"))
            {
                var s = cleanDisplay.IndexOf(">>>SEND_EMAIL<<<");
                var e = cleanDisplay.IndexOf(">>>END_EMAIL<<<");
                if (e > s) cleanDisplay = cleanDisplay.Substring(0, s).Trim() + cleanDisplay.Substring(e + 15).Trim();
            }

            lock (session.HistoryLock)
            {
                if (cfg.EnableContextMemory)
                    session.History.Add(new ChatMessage { Role = "assistant", Content = aiResp });
                else
                    session.History.Clear();
            }

            var promptTokens = ChatUtils.EstimateTokens(string.Join("\n",
                session.History.Take(session.History.Count - 1).Select(m => m.Content)));
            var completionTokens = ChatUtils.EstimateTokens(aiResp);
            ConfigManager.RecordTokenUsage(plr.Name, promptTokens, completionTokens);
            session.TotalTokens += promptTokens + completionTokens;
            session.RequestCount++;

            SendResponse(plr, id, cleanDisplay, wasTruncated, promptTokens + completionTokens, session, emailSent);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[AIAgent] 请求 #{id} 未捕获异常: {ex.Message}");
            plr.SendErrorMessage($"[AIAgent] 请求 #{id} 处理时发生错误: {ex.Message}");
        }
    }

private static async Task<string> ParseStreamResponseAsync(HttpResponseMessage resp, TSPlayer plr, int id, long elapsedMs)
    {
        var sb = new StringBuilder();
        var allRawLines = new List<string>();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) continue;
            if (allRawLines.Count < 50) allRawLines.Add(line);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith(":")) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errorProp)) { continue; }
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;

                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
                    && delta.TryGetProperty("content", out var dc) && dc.ValueKind == JsonValueKind.String)
                { sb.Append(dc.GetString()); }
                else if (choice.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
                    && msg.TryGetProperty("content", out var mc) && mc.ValueKind == JsonValueKind.String)
                { sb.Append(mc.GetString()); }
                else if (choice.TryGetProperty("text", out var te) && te.ValueKind == JsonValueKind.String)
                { sb.Append(te.GetString()); }
            }
            catch { }
        }

        var aiResp = sb.ToString();
        if (string.IsNullOrWhiteSpace(aiResp))
        {
            var rawText = string.Join("\n", allRawLines);
            if (rawText.Contains("<!doctype html>") || rawText.Contains("<html") || rawText.Contains("<body>"))
            {
                TShock.Log.Error($"[AIAgent] 请求 #{id} API返回HTML页面");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: API返回了网页而不是数据。");
            }
            else
            {
                TShock.Log.Error($"[AIAgent] 请求 #{id} 流式响应为空 | 原始行数: {allRawLines.Count}");
                plr.SendErrorMessage($"[AIAgent] 请求 #{id} 失败: AI返回了空内容。");
            }
            return "";
        }
        return aiResp;
    }

    private static string ParseNonStreamResponse(string respBody, int id, long elapsedMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(respBody);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    return c.GetString() ?? "";
                if (choice.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString() ?? "";
            }
            return "";
        }
        catch { return ""; }
    }

    private static void SendResponse(TSPlayer plr, int id, string displayText, bool wasTruncated,
        long totalTokens, Session session, bool emailSent = false)
    {
        var cfg = PluginState.Config;
        if (cfg.SimpleMode)
            plr.SendSuccessMessage($"[c/FF69B4:[{cfg.AIName}]] {displayText}");
        else
            plr.SendSuccessMessage($"[c/FF69B4:[{cfg.AIName} #{id}]] {displayText}");
        if (wasTruncated && !cfg.SimpleMode)
            plr.SendInfoMessage($"[c/808080:AI回答已按字数限制截断展示 (限制{cfg.MaxResponseLength}字)]");
        if (cfg.EnableTokenStats && !cfg.SimpleMode)
            plr.SendInfoMessage($"[c/808080:[Token统计] 本次约{totalTokens:N0} tokens | 会话累计约{session.TotalTokens:N0} tokens]");
        if (!cfg.EnableContextMemory && !cfg.SimpleMode)
            plr.SendInfoMessage("[c/808080:[上下文记忆已关闭] AI不会记住本次对话内容]");
        if (cfg.EnableWebSearch && !cfg.SimpleMode && IsWebSearchSupported(cfg.Model))
            plr.SendInfoMessage("[c/808080:[联网搜索已开启] AI可能使用了互联网信息]");
        if (emailSent && !cfg.SimpleMode)
            plr.SendInfoMessage("[c/808080:[邮件已发送] AI已自动调用邮件插件发送邮件]");
    }

    private static bool IsWebSearchSupported(string model)
    {
        var m = model.ToLower();
        return m.Contains("gpt-4") || m.Contains("claude");
    }

public static async Task TestModel(TSPlayer plr, bool fast)
    {
        var cfg = PluginState.Config;
        var models = fast ? new[] { cfg.Model, "moonshot-v1-8k", "deepseek-chat" } : new[] { cfg.Model };
        if (!cfg.SimpleMode)
            plr.SendInfoMessage("[AIAgent] 正在测试AI模型连接，请稍候...");
        foreach (var m in models)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var req = new ChatRequest { Model = m, Messages = new() { new ChatMessage { Role = "user", Content = "你好" } }, Stream = false };
                var json = JsonSerializer.Serialize(req);
                var request = new HttpRequestMessage(HttpMethod.Post, ChatUtils.GetChatCompletionsUrl()) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
                var resp = await PluginState.Client.SendAsync(request, PluginState.Cts.Token);
                sw.Stop();
                var body = "";
                try { body = await resp.Content.ReadAsStringAsync(); } catch { }
                var err = "";
                try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var e)) err = e.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : ""; } catch { }
                if (resp.IsSuccessStatusCode && string.IsNullOrEmpty(err))
                    plr.SendSuccessMessage($"[AIAgent] [{m}] 连接成功，响应时间{sw.ElapsedMilliseconds}ms");
                else if (!string.IsNullOrEmpty(err))
                {
                    plr.SendErrorMessage($"[AIAgent] [{m}] API错误: {err}");
                    if (err.Contains("no access to model")) plr.SendInfoMessage("  该令牌没有此模型的访问权限。");
                }
                else plr.SendErrorMessage($"[AIAgent] [{m}] HTTP错误: {resp.StatusCode}");
            }
            catch (OperationCanceledException) when (PluginState.Cts.IsCancellationRequested) { return; }
            catch (TaskCanceledException)
            {
                plr.SendErrorMessage($"[AIAgent] [{m}] 测试超时: 超过 {PluginState.Client.Timeout.TotalSeconds}秒");
            }
            catch (Exception ex)
            {
                plr.SendErrorMessage($"[AIAgent] [{m}] 连接异常: {ex.Message}");
            }
        }
    }

    public static async Task<string?> HandleAutoChat(Session session)
    {
        var cfg = PluginState.Config;
        try
        {
            var req = new ChatRequest { Model = cfg.Model, Messages = session.History.ToList(), Stream = false, Temperature = 0.8 };
            if (cfg.MaxResponseLength > 0) req.MaxTokens = ChatUtils.EstimateTokensFromChars(cfg.MaxResponseLength);
            var json = JsonSerializer.Serialize(req);
            var request = new HttpRequestMessage(HttpMethod.Post, ChatUtils.GetChatCompletionsUrl()) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            var resp = await PluginState.Client.SendAsync(request, PluginState.Cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return ParseNonStreamResponse(await resp.Content.ReadAsStringAsync(), 0, 0);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { TShock.Log.Error($"[AIAgent] 主动聊天请求异常: {ex.Message}"); return null; }
    }


    private static async Task CompressContextIfNeeded(TSPlayer plr, Session session)
    {
        var cfg = PluginState.Config;
        if (session.History.Count <= cfg.CompressionThreshold) return;
        if (cfg.CompressionThreshold <= 0) return;
        if (!cfg.SimpleMode) plr.SendInfoMessage("[c/808080:对话历史较长，正在压缩早期上下文以节省Token...]");
        try
        {
            using var compressCts = CancellationTokenSource.CreateLinkedTokenSource(PluginState.Cts.Token);
            compressCts.CancelAfter(TimeSpan.FromSeconds(30));
            var keepCount = Math.Max(cfg.CompressionKeepCount, 2);
            List<ChatMessage> messagesToCompress, recentMessages;
            ChatMessage? systemMsg;
            lock (session.HistoryLock)
            {
                messagesToCompress = session.History.Skip(1).Take(session.History.Count - keepCount - 1).ToList();
                recentMessages = session.History.TakeLast(keepCount).ToList();
                systemMsg = session.History.FirstOrDefault();
            }
            if (messagesToCompress.Count < 3) return;
            var compressPrompt = "请将以下对话历史压缩为简洁的摘要，保留关键信息和玩家的核心需求。用中文回答。\n\n" + string.Join("\n", messagesToCompress.Select(m => $"{m.Role}: {m.Content}"));
            var req = new ChatRequest
            {
                Model = cfg.Model,
                Messages = new List<ChatMessage> { new() { Role = "system", Content = "你是一个对话压缩助手。" }, new() { Role = "user", Content = compressPrompt } },
                Stream = false,
                Temperature = 0.3
            };
            var json = JsonSerializer.Serialize(req);
            var request = new HttpRequestMessage(HttpMethod.Post, ChatUtils.GetChatCompletionsUrl()) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            var resp = await PluginState.Client.SendAsync(request, compressCts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.ReadAsStringAsync();
                string? summary = null;
                try
                {
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var ce))
                        summary = ce.GetString();
                }
                catch { }
                if (!string.IsNullOrEmpty(summary))
                {
                    var newHistory = new List<ChatMessage>();
                    if (systemMsg != null) newHistory.Add(systemMsg);
                    newHistory.Add(new ChatMessage { Role = "system", Content = $"[历史对话摘要] {summary}" });
                    newHistory.AddRange(recentMessages);
                    lock (session.HistoryLock) session.History = newHistory;
                    if (!cfg.SimpleMode) plr.SendInfoMessage("[c/808080:上下文压缩完成，已保留关键信息。]");
                }
            }
        }
        catch (OperationCanceledException) { if (!cfg.SimpleMode) plr.SendInfoMessage("[c/808080:上下文压缩超时，跳过压缩直接处理。]"); }
        catch (Exception ex) { if (!cfg.SimpleMode) plr.SendInfoMessage($"[c/808080:上下文压缩失败: {ex.Message}]"); }
    }

    private static void UpdatePersonaIfChanged(Session session)
    {
        if (session.History.Count == 0) return;
        var firstMsg = session.History[0];
        if (firstMsg.Role != "system") return;
        var cfg = PluginState.Config;
        if (string.IsNullOrWhiteSpace(cfg.Persona)) return;
        if (!string.IsNullOrWhiteSpace(cfg.Persona) && firstMsg.Content.Contains(cfg.Persona.Substring(0, Math.Min(20, cfg.Persona.Length)))) return;
        var nameMarker = "当前与你对话的玩家是 \"";
        var nameStart = firstMsg.Content.IndexOf(nameMarker);
        var playerName = "玩家";
        if (nameStart > 0)
        {
            nameStart += nameMarker.Length;
            var nameEnd = firstMsg.Content.IndexOf('"', nameStart);
            if (nameEnd > nameStart) playerName = firstMsg.Content.Substring(nameStart, nameEnd - nameStart);
        }
        firstMsg.Content = ChatUtils.BuildSystemPrompt(playerName);
    }

}
