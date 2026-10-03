using AutoResetPlus.Configs;

namespace AutoResetPlus;

public class ResetConfig : JsonConfigBase<ResetConfig>
{
    [LocalizedPropertyName(CultureType.Chinese, "替换文件", Order = 1)]
    [LocalizedPropertyName(CultureType.English, "ReplaceFiles")]
    public Dictionary<string, string>? Files;

    [LocalizedPropertyName(CultureType.Chinese, "击杀重置", Order = 2)]
    [LocalizedPropertyName(CultureType.English, "KillToReset")]
    public AutoReset KillToReset = new();

    [LocalizedPropertyName(CultureType.Chinese, "重置后指令", Order = 3)]
    [LocalizedPropertyName(CultureType.English, "AfterResetCommand")]
    public string[]? PostResetCommands;

    [LocalizedPropertyName(CultureType.Chinese, "重置前指令", Order = 4)]
    [LocalizedPropertyName(CultureType.English, "BeforeResetCommand")]
    public string[]? PreResetCommands;

    [LocalizedPropertyName(CultureType.Chinese, "地图预设", Order = 6)]
    [LocalizedPropertyName(CultureType.English, "WorldSetting")]
    public SetWorldConfig SetWorld = new();

    [LocalizedPropertyName(CultureType.Chinese, "随机种子配置", Order = 7)]
    [LocalizedPropertyName(CultureType.English, "RandomSeed")]
    public RandomSeedConfig RandomSeed = new();

    [LocalizedPropertyName(CultureType.Chinese, "重置后SQL命令", Order = 5)]
    [LocalizedPropertyName(CultureType.English, "AfterResetSQL")]
    public string[]? SqLs;

    protected override string Filename => Path.Combine(AutoResetPlugin.FolderName, "AutoReset");

    protected override void SetDefault()
    {
        KillToReset = new AutoReset();
        SetWorld = new SetWorldConfig();
        PreResetCommands = [];
        PostResetCommands = [];
        SqLs =
        [
            "DELETE FROM tsCharacter",
            "DELETE FROM Warps"
        ];
        // 默认留空，避免引用实际不存在的示例文件导致每次重置都提示“替换失败”。
        // 用法：键为目标文件路径（相对服务器运行目录，如 "tshock/原神.json"，也可写绝对路径，
        // 注意开头不能带斜杠，否则 Windows 下会被解析为盘符根目录）；
        // 值为 ReplaceFiles 目录中的源文件名，为空字符串时表示删除目标文件。示例见 README。
        Files = new Dictionary<string, string>();
        RandomSeed = new RandomSeedConfig
        {
            Enable = false,
            SeedList = BuildDefaultSeedList()
        };
    }

    public class SetWorldConfig
    {
        [LocalizedPropertyName(CultureType.Chinese, "地图名", Order = 0)]
        [LocalizedPropertyName(CultureType.English, "WorldName")]
        public string? Name;

        [LocalizedPropertyName(CultureType.Chinese, "地图种子", Order = 1)]
        [LocalizedPropertyName(CultureType.English, "WorldSeed")]
        public string? Seed;
    }

    public class RandomSeedConfig
    {
        [LocalizedPropertyName(CultureType.Chinese, "开启重置后自动设置种子", Order = 0)]
        [LocalizedPropertyName(CultureType.English, "Auto-set random secret seeds after reset")]
        public bool Enable;

        [LocalizedPropertyName(CultureType.Chinese, "最少数量", Order = 1)]
        [LocalizedPropertyName(CultureType.English, "Min")]
        public int Min = 2;

        [LocalizedPropertyName(CultureType.Chinese, "最多数量", Order = 2)]
        [LocalizedPropertyName(CultureType.English, "Max")]
        public int Max = 4;

        [LocalizedPropertyName(CultureType.Chinese, "种子列表", Order = 3)]
        [LocalizedPropertyName(CultureType.English, "SeedList")]
        public string[]? SeedList;
    }

    public class AutoReset
    {
        [LocalizedPropertyName(CultureType.Chinese, "击杀重置开关", Order = 0)]
        [LocalizedPropertyName(CultureType.English, "KillResetEnable")]
        public bool Enable;

        [LocalizedPropertyName(CultureType.Chinese, "已击杀次数", Order = 1)]
        [LocalizedPropertyName(CultureType.English, "CurrentKillCount")]
        public int KillCount;

        [LocalizedPropertyName(CultureType.Chinese, "需要击杀次数", Order = 3)]
        [LocalizedPropertyName(CultureType.English, "NeedKillCount")]
        public int NeedKillCount = 50;

        [LocalizedPropertyName(CultureType.Chinese, "生物ID", Order = 2)]
        [LocalizedPropertyName(CultureType.English, "NpcId")]
        public int NpcId = 50;
    }

    public static string[] BuildDefaultSeedList()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SeedConsts.NoTraps[0],
            SeedConsts.NotTheBees[0],
            SeedConsts.ForTheWorthy[0],
            SeedConsts.Remix[0],
            SeedConsts.CelebrationMk10[0],
            SeedConsts.TheConstant[0],
            SeedConsts.GetFixedBoi[0]
        };
        foreach (var name in SeedConsts.SecretSeedNames)
            set.Add(name);
        return set.ToArray();
    }
}