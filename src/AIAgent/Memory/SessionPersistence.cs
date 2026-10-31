using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIAgent.Core;

namespace AIAgent.Memory;

public sealed class SessionSnapshot
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("display")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("turns")]
    public List<ChatTurn> Turns { get; set; } = new();
}

/// <summary>
/// 会话落盘：进程退出/崩溃后重启，未抽取的最近对话还在，
/// 不会因为"只存在内存里"而丢掉最后一段记忆。
/// </summary>
public static class SessionPersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static string PathFor(string dataDirectory) => Path.Combine(dataDirectory, "sessions.json");

    public static void Save(string dataDirectory)
    {
        try
        {
            var snapshots = PluginState.Sessions.SnapshotAll()
                .Select(item => new SessionSnapshot
                {
                    Key = item.Key,
                    DisplayName = item.DisplayName,
                    Summary = item.Summary,
                    Turns = item.Turns,
                })
                .ToList();

            var path = PathFor(dataDirectory);
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshots, Options), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            PluginLog.Warn("会话落盘失败: " + ex.Message);
        }
    }

    public static void Load(string dataDirectory)
    {
        try
        {
            var path = PathFor(dataDirectory);
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path, Encoding.UTF8);
            var snapshots = JsonSerializer.Deserialize<List<SessionSnapshot>>(json, Options);
            if (snapshots is null) return;

            var restored = 0;
            foreach (var snapshot in snapshots)
            {
                if (string.IsNullOrWhiteSpace(snapshot.Key)) continue;
                PluginState.Sessions.Restore(snapshot.Key, snapshot.DisplayName, snapshot.Summary, snapshot.Turns);
                restored++;
            }

            PluginLog.Info($"已恢复 {restored} 段未完成的会话");
        }
        catch (Exception ex)
        {
            PluginLog.Warn("会话恢复失败: " + ex.Message);
        }
    }
}
