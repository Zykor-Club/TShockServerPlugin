using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIAgent;

public class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public class ChatTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public ChatToolFunction Function { get; set; } = new();
}

public class ChatToolFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "web_search";
    [JsonPropertyName("description")] public string Description { get; set; } = "Search the internet for current information";

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Parameters { get; set; } = new
    {
        type = "object",
        properties = new { },
        required = new string[] { }
    };
}

public class ChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("stream")] public bool Stream { get; set; } = true;
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChatTool>? Tools { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxTokens { get; set; } = 0;
}

public class StreamChoice
{
    [JsonPropertyName("delta")] public Delta Delta { get; set; } = new();
}

public class Delta
{
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public class StreamResponse
{
    [JsonPropertyName("choices")] public List<StreamChoice> Choices { get; set; } = new();
}

public class ChatUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}

public class ChatMessageResponse
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public class ChatChoice
{
    [JsonPropertyName("message")] public ChatMessageResponse Message { get; set; } = new();
}

public class ChatResponse
{
    [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; } = new();
    [JsonPropertyName("usage")] public ChatUsage Usage { get; set; } = new();
}

public class Session
{
    public List<ChatMessage> History { get; set; } = new();
    public object HistoryLock { get; } = new();
    public long TotalTokens { get; set; } = 0;
    public int RequestCount { get; set; } = 0;
}

public class PlayerTokenStats
{
    [JsonPropertyName("player_name")] public string PlayerName { get; set; } = "";
    [JsonPropertyName("prompt_tokens")] public long PromptTokens { get; set; } = 0;
    [JsonPropertyName("completion_tokens")] public long CompletionTokens { get; set; } = 0;
    [JsonPropertyName("total_tokens")] public long TotalTokens { get; set; } = 0;
    [JsonPropertyName("request_count")] public int RequestCount { get; set; } = 0;
    [JsonPropertyName("first_used")] public DateTime FirstUsed { get; set; }
    [JsonPropertyName("last_used")] public DateTime LastUsed { get; set; }
}

public class AutoChatEvent
{
    public string PlayerName { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Time { get; set; }
}
