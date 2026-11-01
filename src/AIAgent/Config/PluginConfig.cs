using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AIAgent.Config;

/// <summary>
/// 插件配置。所有键名用中文，方便服主直接改文件。
/// 写入时由 ConfigText 生成带注释的 JSONC；读取时允许注释与尾逗号。
/// </summary>
public sealed class PluginConfig
{
    // ---------- 基础 ----------

    [JsonPropertyName("AI名称")]
    public string AIName { get; set; } = "阿鱼";

    /// <summary>内联人设。留空时读取同目录 persona.md；只有 persona.md 不存在才用这里的文字。</summary>
    [JsonPropertyName("人设")]
    public string Persona { get; set; } = string.Empty;

    [JsonPropertyName("模型名称")]
    public string Model { get; set; } = "deepseek-chat";

    /// <summary>记忆抽取用的模型，留空则跟主模型一致。</summary>
    [JsonPropertyName("抽取模型")]
    public string ExtractModel { get; set; } = string.Empty;

    [JsonPropertyName("温度")]
    public double Temperature { get; set; } = 0.8;

    [JsonPropertyName("供应商")]
    public ProviderSettings Providers { get; set; } = new();

    // ---------- 行为 ----------

    [JsonPropertyName("聊天")]
    public ChatSettings Chat { get; set; } = new();

    [JsonPropertyName("记忆")]
    public MemorySettings Memory { get; set; } = new();

    [JsonPropertyName("问候")]
    public GreetingSettings Greeting { get; set; } = new();

    [JsonPropertyName("关怀")]
    public CareSettings Care { get; set; } = new();

    [JsonPropertyName("主动消息")]
    public ProactiveSettings Proactive { get; set; } = new();

    [JsonPropertyName("指令")]
    public CommandSettings Commands { get; set; } = new();

    [JsonPropertyName("知识库")]
    public WikiSettings Wiki { get; set; } = new();

    [JsonPropertyName("工具")]
    public Dictionary<string, ToolSettings> Tools { get; set; } = new()
    {
        ["give_item"] = new ToolSettings { Enabled = true, DailyLimit = 20 },
        ["set_time"] = new ToolSettings { Enabled = true, DailyLimit = 10 },
        ["teleport_to_player"] = new ToolSettings { Enabled = true, DailyLimit = 30 },
    };

    // ---------- 限额与输出 ----------

    [JsonPropertyName("冷却秒")]
    public int CooldownSeconds { get; set; } = 3;

    [JsonPropertyName("每日Token预算")]
    public int DailyTokenBudget { get; set; } = 50000;

    [JsonPropertyName("最大输入字数")]
    public int MaxInputLength { get; set; } = 300;

    [JsonPropertyName("回复最大字数")]
    public int MaxResponseLength { get; set; } = 600;

    [JsonPropertyName("单条气泡字数")]
    public int BubbleLength { get; set; } = 180;

    [JsonPropertyName("气泡间隔毫秒")]
    public int BubbleDelayMs { get; set; } = 300;

    [JsonPropertyName("启用流式")]
    public bool EnableStream { get; set; } = true;

    [JsonPropertyName("启用工具")]
    public bool EnableTools { get; set; } = true;

    [JsonPropertyName("调试日志")]
    public bool DebugLog { get; set; }
}

public sealed class ProviderSettings
{
    [JsonPropertyName("当前")]
    public string Current { get; set; } = "deepseek";

    [JsonPropertyName("列表")]
    public Dictionary<string, ProviderConfig> List { get; set; } = CreateDefault();

    [JsonIgnore]
    public ProviderConfig? CurrentProvider =>
        List.TryGetValue(Current, out var found) ? found : List.Values.FirstOrDefault();

    private static Dictionary<string, ProviderConfig> CreateDefault() => new()
    {
        ["deepseek"] = new ProviderConfig
        {
            Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            DefaultModel = "deepseek-chat",
        },
        ["openai"] = new ProviderConfig
        {
            Name = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            MaxTokensParam = "max_completion_tokens",
            SupportsTemperature = false,
            DefaultModel = "gpt-4o-mini",
        },
        ["gemini"] = new ProviderConfig
        {
            Name = "Gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            DefaultModel = "gemini-2.5-flash",
        },
    };
}

public sealed class ProviderConfig
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("地址")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("最大Token参数")]
    public string MaxTokensParam { get; set; } = "max_tokens";

    [JsonPropertyName("支持温度")]
    public bool SupportsTemperature { get; set; } = true;

    [JsonPropertyName("支持流式用量")]
    public bool SupportsStreamUsage { get; set; } = true;

    [JsonPropertyName("支持工具调用")]
    public bool SupportsTools { get; set; } = true;

    [JsonPropertyName("密钥")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("默认模型")]
    public string? DefaultModel { get; set; }
}

public sealed class ProactiveSettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; }

    [JsonPropertyName("间隔分钟")]
    public int IntervalMinutes { get; set; } = 30;

    [JsonPropertyName("安静分钟")]
    public int QuietMinutes { get; set; } = 10;

    [JsonPropertyName("每日上限")]
    public int DailyLimit { get; set; } = 8;

    [JsonPropertyName("静默时段")]
    public string QuietHours { get; set; } = "01:00-07:00";

    [JsonPropertyName("公屏")]
    public bool Public { get; set; } = true;
}

public sealed class CareSettings
{
    [JsonPropertyName("深夜关怀")]
    public bool LateNight { get; set; } = true;

    [JsonPropertyName("深夜时段")]
    public string LateNightWindow { get; set; } = "01:00-05:00";

    [JsonPropertyName("深夜间隔小时")]
    public int LateNightIntervalHours { get; set; } = 20;

    [JsonPropertyName("连死次数")]
    public int DeathStreak { get; set; } = 3;

    [JsonPropertyName("连死窗口分钟")]
    public int DeathStreakWindowMinutes { get; set; } = 10;
}

public sealed class CommandSettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; }

    [JsonPropertyName("禁止指令")]
    public List<string> Blocked { get; set; } = new()
    {
        "exit", "stop", "reload", "config", "user", "group", "ban", "kick", "sudo", "whitelist",
    };
}

public sealed class ChatSettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("回复模式")]
    public string Mode { get; set; } = "智能";

    [JsonPropertyName("抢话判定")]
    public bool ArbitrationEnabled { get; set; } = true;

    [JsonPropertyName("判定关闭延迟秒")]
    public int NoArbitrationDelaySeconds { get; set; } = 2;

    [JsonPropertyName("智能阈值")]
    public int SmartThreshold { get; set; } = 40;

    [JsonPropertyName("对话窗口秒")]
    public int ConversationWindowSeconds { get; set; } = 120;

    [JsonPropertyName("全局冷却秒")]
    public int GlobalCooldownSeconds { get; set; } = 4;

    [JsonPropertyName("每日回复上限")]
    public int DailyReplyLimit { get; set; } = 200;

    [JsonPropertyName("公屏回复")]
    public bool PublicReply { get; set; } = true;

    [JsonPropertyName("最短字数")]
    public int MinLength { get; set; } = 2;

    [JsonPropertyName("死亡回应")]
    public bool DeathReaction { get; set; } = true;

    [JsonPropertyName("事件播报")]
    public bool EventAnnouncements { get; set; } = true;

    [JsonPropertyName("忽略假人")]
    public bool IgnoreFakePlayers { get; set; } = true;

    [JsonPropertyName("假人前缀")]
    public string FakeNamePrefix { get; set; } = "假人";

    [JsonPropertyName("死亡冷却秒")]
    public int DeathCooldownSeconds { get; set; } = 180;
}

public sealed class MemorySettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("玩家可删除")]
    public bool PlayerCanDelete { get; set; } = true;

    [JsonPropertyName("记忆预算Token")]
    public int MemoryBudgetTokens { get; set; } = 600;

    [JsonPropertyName("空闲摘要分钟")]
    public int IdleMinutes { get; set; } = 10;

    [JsonPropertyName("摘要触发轮数")]
    public int SummaryTriggerTurns { get; set; } = 24;

    [JsonPropertyName("保留最近轮数")]
    public int KeepRecentTurns { get; set; } = 12;
}

public sealed class GreetingSettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("最小间隔小时")]
    public int MinIntervalHours { get; set; } = 12;

    [JsonPropertyName("静默时段")]
    public string QuietHours { get; set; } = "01:00-07:00";

    [JsonPropertyName("最长字数")]
    public int MaxLength { get; set; } = 40;

    [JsonPropertyName("延迟秒")]
    public int DelaySeconds { get; set; } = 8;

    [JsonPropertyName("公屏")]
    public bool Public { get; set; }
}

public sealed class WikiSettings
{
    /// <summary>json = 全内存；sqlite = FTS5 数据库；auto = 有库用库，没库用 json。</summary>
    [JsonPropertyName("引擎")]
    public string Engine { get; set; } = "json";

    [JsonPropertyName("数据库文件")]
    public string Database { get; set; } = "wiki.db";
}

public sealed class ToolSettings
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("每日次数")]
    public int DailyLimit { get; set; } = 10;
}
