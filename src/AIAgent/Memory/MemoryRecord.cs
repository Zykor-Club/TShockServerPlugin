using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIAgent.Memory;

/// <summary>
/// 一个玩家的长期记忆。故意只存"摘要 + 事实 + 待办"，不存聊天原文。
/// </summary>
public sealed class MemoryRecord
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("account")]
    public string Account { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("first_seen")]
    public DateTime FirstSeen { get; set; } = DateTime.Now;

    [JsonPropertyName("last_seen")]
    public DateTime LastSeen { get; set; } = DateTime.Now;

    [JsonPropertyName("profile")]
    public MemoryProfile Profile { get; set; } = new();

    [JsonPropertyName("episodes")]
    public List<MemoryEpisode> Episodes { get; set; } = new();

    [JsonPropertyName("threads")]
    public List<MemoryThread> Threads { get; set; } = new();

    [JsonPropertyName("sessions")]
    public List<MemorySession> Sessions { get; set; } = new();
}

public sealed class MemoryProfile
{
    [JsonPropertyName("称呼")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("身份")]
    public string Identity { get; set; } = string.Empty;

    [JsonPropertyName("偏好")]
    public List<string> Preferences { get; set; } = new();

    [JsonPropertyName("习惯")]
    public string Habits { get; set; } = string.Empty;

    [JsonPropertyName("好感度")]
    public int Affinity { get; set; } = 50;

    [JsonPropertyName("情绪")]
    public string Mood { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) &&
        string.IsNullOrWhiteSpace(Identity) &&
        string.IsNullOrWhiteSpace(Habits) &&
        Preferences.Count == 0;
}

public sealed class MemoryEpisode
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("importance")]
    public int Importance { get; set; } = 5;

    [JsonPropertyName("created")]
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonPropertyName("last_used")]
    public DateTime? LastUsed { get; set; }

    [JsonPropertyName("hits")]
    public int Hits { get; set; }

    [JsonPropertyName("superseded")]
    public bool Superseded { get; set; }
}

public sealed class MemoryThread
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    [JsonPropertyName("updated")]
    public DateTime Updated { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsOpen => string.Equals(Status, "open", StringComparison.OrdinalIgnoreCase);
}

public sealed class MemorySession
{
    [JsonPropertyName("start")]
    public DateTime Start { get; set; } = DateTime.Now;

    [JsonPropertyName("end")]
    public DateTime End { get; set; } = DateTime.Now;

    [JsonPropertyName("turns")]
    public int Turns { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

/// <summary>一轮对话（工作记忆用，不落盘）。</summary>
public sealed class ChatTurn
{
    public string Role { get; init; } = "user";

    public string Content { get; init; } = string.Empty;

    public DateTime At { get; init; } = DateTime.Now;
}
