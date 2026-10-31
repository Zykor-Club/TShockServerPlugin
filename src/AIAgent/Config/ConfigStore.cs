using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIAgent.Core;

namespace AIAgent.Config;

/// <summary>
/// 配置读写。三条硬规则：
/// 1. 写入先落临时文件再替换，避免断电/崩溃留下半截 JSON；
/// 2. 每次替换保留 .bak，解析失败时回退；
/// 3. 解析失败绝不覆盖原文件（旧版最严重的坑：悄悄把密钥和人设清空）。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public ConfigStore(string directory)
    {
        Directory.CreateDirectory(directory);
        DirectoryPath = directory;
        Path = System.IO.Path.Combine(directory, "config.json");
        BackupPath = Path + ".bak";
    }

    public string DirectoryPath { get; }

    public string Path { get; }

    public string BackupPath { get; }

    public PluginConfig Load()
    {
        if (!File.Exists(Path))
        {
            var fresh = new PluginConfig();
            Save(fresh);
            PluginLog.Info($"已生成默认配置: {Path}");
            return fresh;
        }

        try
        {
            return Deserialize(File.ReadAllText(Path, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            PluginLog.Error($"配置解析失败，已保留原文件不覆盖: {Path}", ex);

            if (File.Exists(BackupPath))
            {
                try
                {
                    var restored = Deserialize(File.ReadAllText(BackupPath, Encoding.UTF8));
                    PluginLog.Warn($"已从备份恢复配置: {BackupPath}");
                    return restored;
                }
                catch (Exception backupEx)
                {
                    PluginLog.Error($"备份配置同样无法解析: {BackupPath}", backupEx);
                }
            }

            PluginLog.Warn("本次使用默认配置运行；修好 config.json 后执行 /aig admin reload 即可生效。");
            return new PluginConfig();
        }
    }

    public void Save(PluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = ConfigText.Write(config);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(true));

            if (File.Exists(Path))
            {
                File.Replace(temp, Path, BackupPath, true);
            }
            else
            {
                File.Move(temp, Path);
            }
        }
    }

    /// <summary>只读校验，不修改任何状态，供 /aig admin doctor 使用。</summary>
    public bool TryValidate(out string? error)
    {
        try
        {
            _ = Deserialize(File.ReadAllText(Path, Encoding.UTF8));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static PluginConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<PluginConfig>(json, Options)
        ?? throw new InvalidOperationException("配置内容为空");
}
