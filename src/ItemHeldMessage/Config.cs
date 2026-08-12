using Newtonsoft.Json;

namespace ItemHeldMessage;

// 插件主配置类
public sealed class PluginConfig
{
    [JsonProperty("全局设置", Order = 1)]
    public GlobalSettings Global { get; set; } = new();

    [JsonProperty("物品配置", Order = 2)]
    public Dictionary<string, ItemDefinition> Items { get; set; } = new();

    // 创建默认配置
    public static PluginConfig CreateDefault() => new()
    {
        Global = new GlobalSettings
        {
            EnableFloatText = true,
            EnableChatText = true,
            EnableCommand = true,
            SwitchCooldown = 1.5,
            FloatTextCooldown = 3.0,
            ChatTextCooldown = 2.0,
            CommandCooldown = 5.0,
            DefaultYOffset = 50f,
            SkipCommandPermissionCheck = false // 默认不跳过权限检测
        },
        Items = new()
        {
            ["29"] = new ItemDefinition
            {
                Name = "生命水晶",
                FloatMessages =
                [
                    new MessageData { Text = "关住星梦喵，关住星梦谢谢喵！", Color = [0, 100, 255] },
                    new MessageData { Text = "生命水晶: 右键使用增加20点生命上限", Color = [0, 200, 100] }
                ],
                ChatMessages =
                [
                    new MessageData { Text = "你将生命水晶贴近了你的耳朵，你听到了微弱的声音", Color = [0, 255, 150] },
                    new MessageData { Text = "关住星梦喵，关住星梦谢谢喵！", Color = [0, 255, 150] }
                ],
                Command = new CommandSettings
                {
                    Enabled = true,
                    Cmd = "/heal 20",
                    AllowedGroups = ["admin", "vip"],
                    Description = "恢复20点生命值",
                    SkipPermissionCheck = false // 默认不跳过
                },
                OverrideSettings = new()
                {
                    YOffset = 60,
                    FloatTextCooldown = 5,
                    ChatTextCooldown = 3
                }
            },
            ["74"] = new ItemDefinition
            {
                Name = "铂金币",
                FloatMessages = [new() { Text = "铂金币: 价值连城！", Color = [255, 215, 0] }],
                ChatMessages = [new() { Text = "价值可是高达100个金币呢（）", Color = [255, 223, 0] }],
                Command = new CommandSettings
                {
                    Enabled = true,
                    Cmd = "/give {player} 74 1",
                    AllowedGroups = ["admin"],
                    Description = "复制一个铂金币",
                    SkipPermissionCheck = false
                }
            }
        }
    };
}

// 全局设置类
public sealed class GlobalSettings
{
    [JsonProperty("启用浮动文本")]
    public bool EnableFloatText { get; set; } = true;

    [JsonProperty("启用信息栏文本")]
    public bool EnableChatText { get; set; } = true;

    [JsonProperty("启用自动命令")]
    public bool EnableCommand { get; set; } = true;

    [JsonProperty("手持切换冷却(秒)")]
    public double SwitchCooldown { get; set; } = 1.5;

    [JsonProperty("默认浮动文本冷却(秒)")]
    public double FloatTextCooldown { get; set; } = 3.0;

    [JsonProperty("默认信息栏冷却(秒)")]
    public double ChatTextCooldown { get; set; } = 2.0;

    [JsonProperty("默认命令冷却(秒)")]
    public double CommandCooldown { get; set; } = 5.0;

    [JsonProperty("默认Y轴偏移(像素)")]
    public float DefaultYOffset { get; set; } = 50f;

    // 新增：全局跳过权限检测开关
    [JsonProperty("全局跳过命令权限检测")]
    public bool SkipCommandPermissionCheck { get; set; } = false;
}

// 单个物品配置定义
public sealed class ItemDefinition
{
    [JsonProperty("物品名称")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("浮动消息列表")]
    public List<MessageData> FloatMessages { get; set; } = new();

    [JsonProperty("信息栏消息列表")]
    public List<MessageData> ChatMessages { get; set; } = new();

    [JsonProperty("命令配置")]
    public CommandSettings Command { get; set; } = new();

    [JsonProperty("自定义覆盖设置")]
    public OverrideSettings? OverrideSettings { get; set; }
}

// 消息数据结构
public sealed class MessageData
{
    [JsonProperty("文本")]
    public string Text { get; set; } = string.Empty;

    [JsonProperty("颜色")]
    public int[] Color { get; set; } = [255, 255, 255];
}

// 命令设置类 - 新增跳过权限开关
public sealed class CommandSettings
{
    [JsonProperty("启用")]
    public bool Enabled { get; set; } = false;

    [JsonProperty("命令")]
    public string Cmd { get; set; } = string.Empty;

    [JsonProperty("允许的权限组")]
    public List<string> AllowedGroups { get; set; } = new();

    [JsonProperty("描述")]
    public string Description { get; set; } = string.Empty;

    // 新增：单物品跳过权限检测开关
    [JsonProperty("跳过权限检测")]
    public bool SkipPermissionCheck { get; set; } = false;
}

// 覆盖设置类 - 用于覆盖全局默认值
public sealed class OverrideSettings
{
    [JsonProperty("Y轴偏移")]
    public float YOffset { get; set; }

    [JsonProperty("浮动文本冷却")]
    public double FloatTextCooldown { get; set; }

    [JsonProperty("信息栏冷却")]
    public double ChatTextCooldown { get; set; }

    [JsonProperty("命令冷却")]
    public double CommandCooldown { get; set; }
}
