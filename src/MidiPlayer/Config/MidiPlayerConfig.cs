using MidiPlayer.Core;
using Newtonsoft.Json;
using TShockAPI;

namespace MidiPlayer.Config;

public class MidiPlayerConfig
{
    [JsonProperty("音量校准")]
    public Dictionary<string, float> VolumeCalibration { get; set; } = new()
    {
        { "Harp", 1.33f },
        { "Bell", 1.20f },
        { "GuitarAxe", 1.13f },
        { "Drum", 0.50f },
        { "Guitar", 0.45f },
    };

    [JsonProperty("预发送提前量(毫秒)")]
    public int LookAheadMs { get; set; } = 5;

    [JsonProperty("Bell同奏开关")]
    public bool EnableBellHarmony { get; set; } = true;

    [JsonProperty("竖琴残响开关")]
    public bool EnableHarpReverb { get; set; } = true;

    [JsonProperty("配置版本")]
    public int? ConfigVersion { get; set; }

    [JsonProperty("每tick最大音符数")]
    public int MaxNotesPerTick { get; set; } = 10;

    [JsonProperty("同Style重触发间隔(毫秒)")]
    public int SameStyleRetriggerMs { get; set; } = 35;

    [JsonProperty("启用重复音符合并")]
    public bool EnableNoteMerge { get; set; } = true;

    [JsonProperty("启用音符限流")]
    public bool EnableNoteLimit { get; set; } = true;

    [JsonProperty("启用自动微调")]
    public bool EnableAutoTranspose { get; set; } = false;

    [JsonProperty("移调微调(半音)")]
    public int ManualTransposeOffset { get; set; } = 0;

    [JsonProperty("需微调歌曲")]
    public List<string> AutoTransposeSongs { get; set; } = new();

    [JsonProperty("屏蔽手弹乐器音(防MIDI音高污染)")]
    public bool BlockInstrumentSound { get; set; } = true;

    [JsonProperty("手弹乐器时给出提示(防止MIDI音高污染)")]
    public bool WarnOnManualPlay { get; set; } = true;

    private static readonly string BasePath =
        Path.Combine(TShock.SavePath, "MidiPlayer");

    private static readonly string ConfigPath =
        Path.Combine(BasePath, "MidiPlayerConfig.json");

    private static readonly string LegacyConfigPath1 =
        Path.Combine(TShock.SavePath, "MidiPlayerConfig.json");

    private static readonly string LegacyConfigPath2 =
        Path.Combine(TShock.SavePath, "MidiPlayerSurroundConfig.json");

    public static MidiPlayerConfig Load()
    {
        try
        {
            // 确保主目录存在
            if (!Directory.Exists(BasePath))
                Directory.CreateDirectory(BasePath);

            if (File.Exists(ConfigPath))
                return LoadAndMigrate(ConfigPath);

            // 从旧路径迁移配置
            if (File.Exists(LegacyConfigPath1))
            {
                var config = LoadAndMigrate(LegacyConfigPath1);
                try { File.Delete(LegacyConfigPath1); } catch { }
                return config;
            }

            if (File.Exists(LegacyConfigPath2))
            {
                var config = LoadAndMigrate(LegacyConfigPath2);
                try { File.Delete(LegacyConfigPath2); } catch { }
                return config;
            }
        }
        catch { }

        var defaultConfig = new MidiPlayerConfig();
        defaultConfig.ConfigVersion = 3;
        Save(defaultConfig);
        return defaultConfig;
    }

    private static MidiPlayerConfig LoadAndMigrate(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonConvert.DeserializeObject<MidiPlayerConfig>(json)
            ?? new MidiPlayerConfig();

        if (!config.ConfigVersion.HasValue || config.ConfigVersion.Value < 2)
        {
            config.ConfigVersion = 2;
            config.LookAheadMs = 5;
            config.VolumeCalibration["Guitar"] = 0.45f;
            config.VolumeCalibration["Drum"] = 0.70f;
        }

        if (config.ConfigVersion.Value < 3)
        {
            config.ConfigVersion = 3;
            config.EnableNoteMerge = true;
            config.EnableNoteLimit = true;
        }

        if (config.ConfigVersion.Value < 4)
            config.ConfigVersion = 4;

        // v5：手弹复位(58回发)无效且会多响一声，改为给弹奏者弹提示
        if (config.ConfigVersion.Value < 5)
        {
            config.ConfigVersion = 5;
            config.WarnOnManualPlay = true;
        }

        config.PruneMissingSongs();

        Save(config); // 写回补全缺失字段
        return config;
    }

    // 移除“需微调歌曲”列表中已被删除的 MIDI 文件（按文件名匹配并持久化）
    public void PruneMissingSongs()
    {
        if (AutoTransposeSongs.Count == 0)
            return;

        string songFolder = Path.Combine(BasePath, "MidiSongs");
        AutoTransposeSongs.RemoveAll(name =>
            string.IsNullOrEmpty(name) || !File.Exists(Path.Combine(songFolder, name)));
    }

    public static void Save(MidiPlayerConfig config)
    {
        if (!config.ConfigVersion.HasValue)
            config.ConfigVersion = 6;

        if (!Directory.Exists(BasePath))
            Directory.CreateDirectory(BasePath);

        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(ConfigPath, json);
    }

    // 将配置中的音量校准写入 SongConverter
    public void ApplyTo(SongConverter converter)
    {
        foreach (var kv in VolumeCalibration)
        {
            if (Enum.TryParse<Models.InstrumentType>(kv.Key, out var instrument))
                SongConverter.VolumeCalibration[instrument] = kv.Value;
        }

        converter.EnableNoteMerge = EnableNoteMerge;
        converter.EnableNoteLimit = EnableNoteLimit;
        converter.EnableAutoTranspose = EnableAutoTranspose;
        converter.ManualTransposeOffset = ManualTransposeOffset;
        converter.AutoTransposeSongs = AutoTransposeSongs;
    }
}
