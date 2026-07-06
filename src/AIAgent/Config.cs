using System.Collections.Generic;
using Newtonsoft.Json;

namespace AIAgent;

public class Config
{
    [JsonProperty("AI名称")]
    public string AIName { get; set; } = "AI助手";

    [JsonProperty("API地址")]
    public string BaseUrl { get; set; } = "https://api.moonshot.cn/v1";

    [JsonProperty("API密钥")]
    public string ApiKey { get; set; } = "";

    [JsonProperty("模型名称")]
    public string Model { get; set; } = "moonshot-v1-8k";

    [JsonProperty("允许玩家白名单")]
    public List<string> AllowedPlayers { get; set; } = new();

    [JsonProperty("启用Token统计")]
    public bool EnableTokenStats { get; set; } = true;

    [JsonProperty("上下文压缩阈值")]
    public int CompressionThreshold { get; set; } = 12;

    [JsonProperty("上下文压缩保留数")]
    public int CompressionKeepCount { get; set; } = 4;

    [JsonProperty("AI人设")]
    public string Persona { get; set; } = "";

    [JsonProperty("人设严格度")]
    public int PersonaStrictness { get; set; } = 5;

    [JsonProperty("最大回复字数")]
    public int MaxResponseLength { get; set; } = 500;

    [JsonProperty("回复限制模式")]
    public string ResponseLimitMode { get; set; } = "truncate";

    [JsonProperty("最大输入字数")]
    public int MaxInputLength { get; set; } = 300;

    [JsonProperty("启用流式传输")]
    public bool EnableStream { get; set; } = true;

    [JsonProperty("极简模式")]
    public bool SimpleMode { get; set; } = false;

    [JsonProperty("启用联网搜索")]
    public bool EnableWebSearch { get; set; } = false;

    [JsonProperty("启用上下文记忆")]
    public bool EnableContextMemory { get; set; } = true;

    [JsonProperty("启用主动聊天")]
    public bool EnableAutoChat { get; set; } = false;

    [JsonProperty("主动聊天间隔秒数")]
    public int AutoChatInterval { get; set; } = 60;

    [JsonProperty("主动聊天玩家冷却秒数")]
    public int AutoChatPlayerCooldown { get; set; } = 30;

    [JsonProperty("主动聊天最大队列")]
    public int AutoChatMaxQueue { get; set; } = 10;

    [JsonProperty("AI关键词提示词")]
    public string AIKeywords { get; set; } = "";

    [JsonProperty("启用邮件功能")]
    public bool EnableMailFeature { get; set; } = true;
}