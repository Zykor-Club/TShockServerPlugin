using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIAgent.Core;

namespace AIAgent.Memory;

/// <summary>
/// 每账号一个 JSON 文件：tshock/AIAgent/memory/&lt;account&gt;.json
/// 写入原子替换并保留 .bak；解析失败时回退备份，绝不覆盖原文件。
/// </summary>
public sealed class JsonMemoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _directory;
    private readonly object _gate = new();

    public JsonMemoryStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public string DirectoryPath => _directory;

    public string PathFor(string account) => System.IO.Path.Combine(_directory, SafeName(account) + ".json");

    public MemoryRecord Load(string account)
    {
        var path = PathFor(account);
        if (!File.Exists(path))
        {
            return new MemoryRecord { Account = account, FirstSeen = DateTime.Now, LastSeen = DateTime.Now };
        }

        try
        {
            var record = Deserialize(File.ReadAllText(path, Encoding.UTF8));
            record.Account = account;
            return record;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"记忆文件解析失败: {path}", ex);
            var backup = path + ".bak";
            if (File.Exists(backup))
            {
                try
                {
                    var record = Deserialize(File.ReadAllText(backup, Encoding.UTF8));
                    record.Account = account;
                    PluginLog.Warn($"已从备份恢复记忆: {backup}");
                    return record;
                }
                catch (Exception backupEx)
                {
                    PluginLog.Error($"记忆备份也无法解析: {backup}", backupEx);
                }
            }

            return new MemoryRecord { Account = account, FirstSeen = DateTime.Now, LastSeen = DateTime.Now };
        }
    }

    public void Save(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Normalize(record);

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(record.Account);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Options), new UTF8Encoding(false));

            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
            else File.Move(temp, path);
        }
    }

    public bool Delete(string account)
    {
        lock (_gate)
        {
            var path = PathFor(account);
            var deleted = false;
            if (File.Exists(path)) { File.Delete(path); deleted = true; }
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            return deleted;
        }
    }

    public bool Exists(string account) => File.Exists(PathFor(account));

    private static void Normalize(MemoryRecord record)
    {
        record.LastSeen = DateTime.Now;
        record.Profile.Affinity = Math.Clamp(record.Profile.Affinity, -100, 100);

        record.Profile.Preferences = record.Profile.Preferences
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Take(8)
            .ToList();

        record.Episodes = record.Episodes
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .GroupBy(e => e.Text.Trim())
            .Select(g => g.OrderByDescending(e => e.Importance).First())
            .OrderByDescending(Score)
            .Take(200)
            .ToList();

        record.Threads = record.Threads
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .GroupBy(t => t.Text.Trim())
            .Select(g => g.OrderByDescending(t => t.Updated).First())
            .OrderByDescending(t => t.IsOpen)
            .ThenByDescending(t => t.Updated)
            .Take(20)
            .ToList();

        record.Sessions = record.Sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.Summary))
            .OrderByDescending(s => s.End)
            .Take(30)
            .ToList();
    }

    private static double Score(MemoryEpisode episode)
    {
        var days = Math.Max(0, (DateTime.Now - episode.Created).TotalDays);
        var recency = Math.Exp(-days / 30.0);
        return episode.Importance / 10.0 * 0.6 + recency * 0.4;
    }

    private static string SafeName(string account)
    {
        var chars = (account ?? string.Empty).Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
        }

        var name = new string(chars);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    private static MemoryRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<MemoryRecord>(json, Options) ?? throw new InvalidOperationException("空记忆文件");
}
