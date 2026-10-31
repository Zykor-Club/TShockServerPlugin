using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AIAgent.Llm;

/// <summary>对玩家友好的模型调用异常：UserMessage 可直接展示，Detail 只进日志。</summary>
public sealed class LlmException : Exception
{
    public LlmException(string userMessage, string detail, Exception? inner = null)
        : base(detail, inner)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}

/// <summary>
/// OpenAI 兼容客户端。DeepSeek / OpenAI / Gemini(兼容层) / Ollama / 中转站共用一条代码路径，
/// 差异由 ChatOptions 里的开关控制（最大 token 参数名、是否支持温度、是否请求用量）。
/// </summary>
public sealed class OpenAiCompatClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _baseUrl;
    private readonly string _apiKey;

    public OpenAiCompatClient(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public static string BuildCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return url + "/chat/completions";
        return url + "/v1/chat/completions";
    }

    public async Task<ChatResult> SendAsync(
        ChatOptions options,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new LlmException("管理员还没给我配密钥。", "api key empty");
        }

        var body = BuildBody(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildCompletionsUrl(_baseUrl))
        {
            Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (options.Stream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(
                request,
                options.Stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmException("我想太久了，等会儿再问我一次。", "request timeout after 120s");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException("网线好像松了，稍后再试。", "network error: " + ex.Message, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await SafeReadAsync(response, timeout.Token).ConfigureAwait(false);
                throw MapHttpError((int)response.StatusCode, errorBody);
            }

            return options.Stream
                ? await ReadStreamAsync(response, onDelta, timeout.Token).ConfigureAwait(false)
                : await ReadWholeAsync(response, timeout.Token).ConfigureAwait(false);
        }
    }

    private static JsonObject BuildBody(ChatOptions options)
    {
        var messages = new JsonArray();
        foreach (var message in options.Messages)
        {
            var node = new JsonObject { ["role"] = message.Role };
            if (!string.IsNullOrEmpty(message.ToolCallId)) node["tool_call_id"] = message.ToolCallId;
            node["content"] = message.Content;

            if (message.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = call.Type,
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Function.Name,
                            ["arguments"] = call.Function.Arguments,
                        },
                    });
                }
                node["tool_calls"] = calls;
            }

            messages.Add(node);
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = options.Stream,
        };

        if (options.SupportsTemperature) body["temperature"] = options.Temperature;
        if (options.MaxTokens > 0) body[options.MaxTokensParam] = options.MaxTokens;
        if (options.Stream && options.SupportsStreamUsage)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (options.Tools is { Count: > 0 })
        {
            body["tools"] = options.Tools.DeepClone();
            body["tool_choice"] = "auto";
        }

        return body;
    }

    private static async Task<ChatResult> ReadStreamAsync(
        HttpResponseMessage response,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        var result = new ChatResult();
        var accumulator = new ToolCallAccumulator();
        var text = new StringBuilder();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (line.Length == 0) continue;
            if (line[0] == ':') continue;

            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line.Substring(5).Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                throw new LlmException("刚才没听清，再说一遍？", "stream error: " + DescribeError(error));
            }

            if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
            {
                result.Usage = ParseUsage(usageEl);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                result.FinishReason = finish.GetString();
            }

            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var piece = content.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    text.Append(piece);
                    onDelta?.Invoke(piece);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toolCalls.EnumerateArray())
                {
                    accumulator.Add(item);
                }
            }
        }

        result.Text = text.ToString();
        if (accumulator.Any) result.ToolCalls.AddRange(accumulator.Build());
        return result;
    }

    private static async Task<ChatResult> ReadWholeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            throw new LlmException("刚才没听清，再说一遍？", "api error: " + DescribeError(error));
        }

        var result = new ChatResult();
        if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            result.Usage = ParseUsage(usageEl);
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new LlmException("它没说话。", "no choices in response");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
        {
            result.FinishReason = finish.GetString();
        }

        if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                result.Text = content.GetString() ?? string.Empty;
            }

            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                var accumulator = new ToolCallAccumulator();
                foreach (var item in toolCalls.EnumerateArray())
                {
                    accumulator.Add(item);
                }
                result.ToolCalls.AddRange(accumulator.Build());
            }
        }

        return result;
    }

    private static ChatUsage ParseUsage(JsonElement usage)
    {
        var parsed = new ChatUsage();
        if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var prompt)) parsed.PromptTokens = prompt;
        if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var completion)) parsed.CompletionTokens = completion;
        if (usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out var total)) parsed.TotalTokens = total;
        if (parsed.TotalTokens == 0) parsed.TotalTokens = parsed.PromptTokens + parsed.CompletionTokens;
        return parsed;
    }

    private static LlmException MapHttpError(int status, string body)
    {
        var detail = $"HTTP {status}: {Truncate(body, 500)}";
        var message = status switch
        {
            401 or 403 => "管理员还没把我配好（密钥无效）。",
            404 => "这个模型我这儿没有，换一个试试。",
            429 => "问的人有点多，缓一下再来。",
            >= 500 => "它打了个盹，等会儿再试。",
            _ => "刚才没听清，再说一遍？",
        };
        return new LlmException(message, detail);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeError(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? string.Empty;
        }
        return error.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max) + "...";
}
