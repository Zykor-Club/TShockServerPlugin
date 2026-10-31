using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AIAgent.Llm;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    public List<ToolCallPayload>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    public static ChatMessage System(string content) => new() { Role = "system", Content = content };

    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
}

public sealed class ToolCallPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionPayload Function { get; set; } = new();
}

public sealed class FunctionPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

public sealed class ChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    public bool IsEmpty => TotalTokens == 0 && PromptTokens == 0 && CompletionTokens == 0;
}

public sealed class ToolCall
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public override string ToString() => $"{Name}({Arguments})";
}

public sealed class ChatResult
{
    public string Text { get; set; } = string.Empty;

    public ChatUsage Usage { get; set; } = new();

    public string? FinishReason { get; set; }

    public List<ToolCall> ToolCalls { get; } = new();
}

public sealed record ChatOptions(
    string BaseUrl,
    string ApiKey,
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    double Temperature = 0.8,
    int MaxTokens = 0,
    bool Stream = true,
    string MaxTokensParam = "max_tokens",
    bool SupportsTemperature = true,
    bool SupportsStreamUsage = true,
    JsonArray? Tools = null);
