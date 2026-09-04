using MidiPlayer.Models;
using Newtonsoft.Json;
using TShockAPI;

namespace MidiPlayer.Config;

/// <summary>
/// GM乐器编号到泰拉瑞亚乐器的映射配置。
/// 首次加载时自动生成默认映射表，服主可修改后 /reload 热重载。
/// </summary>
public class InstrumentMapConfig
{
    [JsonProperty("说明")]
    public string Description { get; set; } =
        "GM乐器编号 0-127 到泰拉瑞亚乐器的映射表。修改后执行 /reload 生效。可选值: Harp(竖琴), Bell(铃铛), GuitarAxe(吉他斧), Guitar(吉他和弦), Drum(鼓组)";

    [JsonProperty("乐器映射表")]
    public List<InstrumentMapEntry> ProgramMap { get; set; } = new();

    [JsonProperty("鼓组映射表")]
    public DrumMapContainer DrumMap { get; set; } = new();

    [JsonProperty("吉他和弦定义")]
    public ChordMapContainer ChordMap { get; set; } = new();

    // === 运行时快速查找字典（不序列化到JSON） ===

    [JsonIgnore]
    public Dictionary<int, InstrumentType> ProgramLookup { get; private set; } = new();

    [JsonIgnore]
    public Dictionary<int, int> DrumStyleLookup { get; private set; } = new();

    [JsonIgnore]
    public List<(int Root, int[] Semitones, int Style)> ChordDefinitions { get; private set; } = new();

    /// <summary>
    /// 从JSON数组构建运行时查找字典，解析失败时回退到Harp/默认值。
    /// </summary>
    public void BuildLookups()
    {
        ProgramLookup = new Dictionary<int, InstrumentType>();
        foreach (var entry in ProgramMap)
        {
            if (Enum.TryParse<InstrumentType>(entry.InstrumentStr, out var inst))
                ProgramLookup[entry.Id] = inst;
            else
                ProgramLookup[entry.Id] = InstrumentType.Harp;
        }

        DrumStyleLookup = new Dictionary<int, int>();
        foreach (var entry in DrumMap.Entries)
        {
            DrumStyleLookup[entry.Note] = entry.Style;
        }

        ChordDefinitions = new List<(int, int[], int)>();
        foreach (var chord in ChordMap.Chords)
        {
            ChordDefinitions.Add((chord.Root, chord.Intervals, chord.Style));
        }
    }

    // === 文件路径 ===

    private static readonly string BasePath =
        Path.Combine(TShock.SavePath, "MidiPlayer");

    private static readonly string ConfigPath =
        Path.Combine(BasePath, "InstrumentMap.json");

    // === 加载/保存 ===

    public static InstrumentMapConfig Load()
    {
        try
        {
            if (!Directory.Exists(BasePath))
                Directory.CreateDirectory(BasePath);

            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<InstrumentMapConfig>(json);
                if (config != null)
                {
                    config.BuildLookups();
                    Console.WriteLine($"[MidiPlayer] 乐器映射表已加载: MidiPlayer/InstrumentMap.json");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MidiPlayer] 加载乐器映射表失败: {ex.Message}，使用默认配置");
        }

        var defaultConfig = CreateDefault();
        Save(defaultConfig);
        Console.WriteLine("[MidiPlayer] 已生成默认乐器映射表: MidiPlayer/InstrumentMap.json");
        return defaultConfig;
    }

    public static void Save(InstrumentMapConfig config)
    {
        if (!Directory.Exists(BasePath))
            Directory.CreateDirectory(BasePath);

        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// 使用当前硬编码的默认映射创建配置（与 InstrumentMapper 原有逻辑一致）。
    /// 翻译来源：DryWetMidi GeneralMidiProgram.cs / GeneralMidiPercussion.cs
    /// </summary>
    public static InstrumentMapConfig CreateDefault()
    {
        var config = new InstrumentMapConfig();

        // === 乐器映射表：128 个 GM 乐器编号 ===
        // 映射原则：Harp(清澈拨弦)、Bell(金属延音)、GuitarAxe(失真颗粒)、Guitar(扫弦)、Drum(打击乐)
        config.ProgramMap = new List<InstrumentMapEntry>
        {
            // 0-7: 钢琴类 → Harp
            new(0, "原声大钢琴", "Harp"),
            new(1, "明亮大钢琴", "Harp"),
            new(2, "电钢琴", "Harp"),
            new(3, "酒吧钢琴", "Harp"),
            new(4, "电钢琴1", "Harp"),
            new(5, "电钢琴2", "Harp"),
            new(6, "羽管键琴", "Harp"),
            new(7, "击弦古钢琴", "Harp"),

            // 8-11, 14: 金属敲击 → Bell
            new(8, "钢片琴", "Bell"),
            new(9, "钟琴", "Bell"),
            new(10, "八音盒", "Bell"),
            new(11, "颤音琴", "Bell"),

            // 12-13, 15: 木琴类 → Harp
            new(12, "马林巴", "Harp"),
            new(13, "木琴", "Harp"),

            new(14, "管钟", "Bell"),

            new(15, "扬琴", "Harp"),

            // 16: 风琴 → Harp
            new(16, "拉杆风琴", "Harp"),

            // 17-21: 风琴/手风琴类 → Guitar（扫弦感）
            new(17, "打击风琴", "Guitar"),
            new(18, "摇滚风琴", "Guitar"),
            new(19, "教堂管风琴", "Guitar"),
            new(20, "簧风琴", "Guitar"),
            new(21, "手风琴", "Guitar"),

            // 22: 口琴 → Harp
            new(22, "口琴", "Harp"),

            // 23-25: 手风琴/木吉他 → Guitar
            new(23, "探戈手风琴", "Guitar"),
            new(24, "尼龙弦吉他", "Guitar"),
            new(25, "钢弦吉他", "Guitar"),

            // 26-30: 电吉他类 → GuitarAxe（失真感）
            new(26, "爵士电吉他", "GuitarAxe"),
            new(27, "清音电吉他", "GuitarAxe"),
            new(28, "闷音电吉他", "GuitarAxe"),
            new(29, "过载吉他", "GuitarAxe"),
            new(30, "失真吉他", "GuitarAxe"),

            // 31: 吉他泛音 → Harp
            new(31, "吉他泛音", "Harp"),

            // 32-40: 贝斯+小提琴 → Harp
            new(32, "原声贝斯", "Harp"),
            new(33, "指弹电贝斯", "Harp"),
            new(34, "拨片电贝斯", "Harp"),
            new(35, "无品贝斯", "Harp"),
            new(36, "击弦贝斯1", "Harp"),
            new(37, "击弦贝斯2", "Harp"),
            new(38, "合成贝斯1", "Harp"),
            new(39, "合成贝斯2", "Harp"),
            new(40, "小提琴", "Harp"),

            // 41-45: 中提琴~拨奏弦乐 → Harp
            new(41, "中提琴", "Harp"),
            new(42, "大提琴", "Harp"),
            new(43, "低音提琴", "Harp"),
            new(44, "颤音弦乐", "Harp"),
            new(45, "拨奏弦乐", "Harp"),

            // 46: 管弦竖琴 → Harp
            new(46, "管弦竖琴", "Harp"),

            // 47: 定音鼓 → Drum
            new(47, "定音鼓", "Drum"),

            // 48: 弦乐合奏1 → Harp
            new(48, "弦乐合奏1", "Harp"),

            // 49-56: 合奏/铜管柔和类 → Harp
            new(49, "弦乐合奏2", "Harp"),
            new(50, "合成弦乐1", "Harp"),
            new(51, "合成弦乐2", "Harp"),
            new(52, "合唱人声", "Harp"),
            new(53, "呜声人声", "Harp"),
            new(54, "合成人声", "Harp"),
            new(55, "管弦乐齐奏", "Harp"),
            new(56, "小号", "Harp"),

            // 57, 61-63: 有力铜管 → GuitarAxe
            new(57, "长号", "GuitarAxe"),

            // 58, 60: 柔和铜管 → Harp
            new(58, "大号", "Harp"),

            // 59, 64: 闷音/高音萨克斯 → Bell
            new(59, "闷音小号", "Bell"),
            new(60, "圆号", "Harp"),
            new(61, "铜管合奏", "GuitarAxe"),
            new(62, "合成铜管1", "GuitarAxe"),
            new(63, "合成铜管2", "GuitarAxe"),
            new(64, "高音萨克斯", "Bell"),

            // 65-79: 萨克斯/木管/笛类 → Harp
            new(65, "中音萨克斯", "Harp"),
            new(66, "次中音萨克斯", "Harp"),
            new(67, "上低音萨克斯", "Harp"),
            new(68, "双簧管", "Harp"),
            new(69, "英国管", "Harp"),
            new(70, "巴松管", "Harp"),
            new(71, "单簧管", "Harp"),
            new(72, "短笛", "Harp"),
            new(73, "长笛", "Harp"),
            new(74, "竖笛", "Harp"),
            new(75, "排箫", "Harp"),
            new(76, "瓶笛", "Harp"),
            new(77, "尺八", "Harp"),
            new(78, "口哨", "Harp"),
            new(79, "陶笛", "Harp"),

            // 80-87: 合成主音 → Harp
            new(80, "主音1(方波)", "Harp"),
            new(81, "主音2(锯齿波)", "Harp"),
            new(82, "主音3(汽笛风琴)", "Harp"),
            new(83, "主音4(气声)", "Harp"),
            new(84, "主音5(查朗)", "Harp"),
            new(85, "主音6(人声)", "Harp"),
            new(86, "主音7(五度)", "Harp"),
            new(87, "主音8(贝斯+主音)", "Harp"),

            // 88-95: 合成垫（延音长） → Bell
            new(88, "合成垫1(新世纪)", "Bell"),
            new(89, "合成垫2(温暖)", "Bell"),
            new(90, "合成垫3(多音合成)", "Bell"),
            new(91, "合成垫4(合唱)", "Bell"),
            new(92, "合成垫5(弓弦)", "Bell"),
            new(93, "合成垫6(金属)", "Bell"),
            new(94, "合成垫7(光环)", "Bell"),
            new(95, "合成垫8(扫频)", "Bell"),

            // 96-111: 合成效果/民族 → Harp
            new(96, "效果1(雨)", "Harp"),
            new(97, "效果2(电影配乐)", "Harp"),
            new(98, "效果3(水晶)", "Harp"),
            new(99, "效果4(氛围)", "Harp"),
            new(100, "效果5(明亮)", "Harp"),
            new(101, "效果6(精灵)", "Harp"),
            new(102, "效果7(回声)", "Harp"),
            new(103, "效果8(科幻)", "Harp"),
            new(104, "西塔琴", "Harp"),
            new(105, "班卓琴", "Harp"),
            new(106, "三味线", "Harp"),
            new(107, "日本筝", "Harp"),
            new(108, "卡林巴琴", "Harp"),
            new(109, "风笛", "Harp"),
            new(110, "民间小提琴", "Harp"),
            new(111, "唢呐", "Harp"),

            // 112-114: 旋律金属打击乐 → Bell
            new(112, "叮当铃", "Bell"),
            new(113, "阿哥哥铃", "Bell"),
            new(114, "钢鼓", "Bell"),

            // 115-127: 打击乐/音效 → Harp
            new(115, "木鱼", "Harp"),
            new(116, "太鼓", "Harp"),
            new(117, "旋律桶鼓", "Harp"),
            new(118, "合成鼓", "Harp"),
            new(119, "反向镲", "Harp"),
            new(120, "吉他摩擦噪音", "Harp"),
            new(121, "呼吸噪音", "Harp"),
            new(122, "海浪", "Harp"),
            new(123, "鸟鸣", "Harp"),
            new(124, "电话铃", "Harp"),
            new(125, "直升机", "Harp"),
            new(126, "掌声", "Harp"),
            new(127, "枪声", "Harp"),
        };

        // === 鼓组映射表 ===
        config.DrumMap = new DrumMapContainer
        {
            Description = "GM鼓音符编号到泰拉瑞亚鼓Style的映射。Style范围: 139-148。修改后执行 /reload 生效。",
            Entries = new List<DrumMapEntry>
            {
                new(35, "原声底鼓", 146),
                new(36, "底鼓1", 146),
                new(38, "原声军鼓", 147),
                new(40, "电子军鼓", 147),
                new(42, "闭镲", 143),
                new(43, "高音落地桶鼓", 148),
                new(44, "踏板踩镲", 143),
                new(45, "低音桶鼓", 148),
                new(46, "开镲", 139),
                new(47, "中低音桶鼓", 141),
                new(48, "中高音桶鼓", 142),
                new(49, "碎镲1", 144),
                new(50, "高音桶鼓", 140),
                new(51, "叮叮镲1", 145),
                new(55, "水镲", 144),
                new(59, "叮叮镲2", 145),
            },
        };

        // === 吉他和弦定义 ===
        config.ChordMap = new ChordMapContainer
        {
            Description = "同时发声的多个音符被识别为和弦后，用对应的Style播放预录和弦音色。根音: 0=C,2=D,4=E,5=F,7=G,9=A,11=B。音程: [4,7]=大三和弦, [3,7]=小三和弦。Style: 133-138。修改后执行 /reload 生效。",
            Chords = new List<ChordMapEntry>
            {
                new(0, "C大调", new[] { 4, 7 }, 133),
                new(2, "D大调", new[] { 4, 7 }, 134),
                new(7, "G大调", new[] { 4, 7 }, 136),
                new(4, "E小调", new[] { 3, 7 }, 135),
                new(11, "B小调", new[] { 3, 7 }, 137),
                new(9, "A小调", new[] { 3, 7 }, 138),
            },
        };

        config.BuildLookups();
        return config;
    }
}

// === JSON 序列化用的数据类 ===

public class InstrumentMapEntry
{
    [JsonProperty("编号")]
    public int Id { get; set; }

    [JsonProperty("GM乐器")]
    public string GmName { get; set; } = "";

    [JsonProperty("映射")]
    public string InstrumentStr { get; set; } = "Harp";

    public InstrumentMapEntry() { }

    public InstrumentMapEntry(int id, string gmName, string instrument)
    {
        Id = id;
        GmName = gmName;
        InstrumentStr = instrument;
    }
}

public class DrumMapContainer
{
    [JsonProperty("说明")]
    public string Description { get; set; } = "";

    [JsonProperty("映射")]
    public List<DrumMapEntry> Entries { get; set; } = new();
}

public class DrumMapEntry
{
    [JsonProperty("音符")]
    public int Note { get; set; }

    [JsonProperty("GM名称")]
    public string GmName { get; set; } = "";

    [JsonProperty("Style")]
    public int Style { get; set; }

    public DrumMapEntry() { }

    public DrumMapEntry(int note, string gmName, int style)
    {
        Note = note;
        GmName = gmName;
        Style = style;
    }
}

public class ChordMapContainer
{
    [JsonProperty("说明")]
    public string Description { get; set; } = "";

    [JsonProperty("和弦")]
    public List<ChordMapEntry> Chords { get; set; } = new();
}

public class ChordMapEntry
{
    [JsonProperty("根音")]
    public int Root { get; set; }

    [JsonProperty("和弦名")]
    public string ChordName { get; set; } = "";

    [JsonProperty("音程")]
    public int[] Intervals { get; set; } = Array.Empty<int>();

    [JsonProperty("Style")]
    public int Style { get; set; }

    public ChordMapEntry() { }

    public ChordMapEntry(int root, string chordName, int[] intervals, int style)
    {
        Root = root;
        ChordName = chordName;
        Intervals = intervals;
        Style = style;
    }
}