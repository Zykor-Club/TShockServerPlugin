using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIAgent.Config;

/// <summary>
/// 把配置写成带注释的 JSONC，给服主看。
/// 读取端（ConfigStore）允许注释和尾逗号，所以写出去的注释不会破坏解析。
/// </summary>
public static class ConfigText
{
    private static readonly JsonSerializerOptions NodeOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private const string Header = """
// ============================================================
//  AIAgent 配置文件
// ------------------------------------------------------------
//  · 支持 // 注释与尾逗号；改完执行 /aig admin reload 生效
//  · 「人设」留空 = 读取同目录 persona.md（推荐，适合写长文）
//    只有 persona.md 不存在时，才会用「人设」里填的文字
//  · 时间一律用服务器本地时间，24 小时制（如 01:00-05:00）
//  · 密钥以明文保存在本文件，注意备份与文件权限
// ============================================================
""";

    private static readonly Dictionary<string, string> Comments = new()
    {
        // ---------- 基础 ----------
        ["AI名称"] = "聊天里显示的名字，也是提示词里的自称",
        ["人设"] = "留空 = 用 persona.md。只有文件不存在时才读这里，一般别填",
        ["模型名称"] = "主对话模型，例如 deepseek-chat / gpt-4o-mini / gemini-2.5-flash",
        ["抽取模型"] = "记忆抽取用的模型；留空跟随主模型，可以填便宜的小模型",
        ["温度"] = "0~2，越高越随机；陪伴型 0.8 比较合适",
        ["供应商"] = "模型服务商。用哪个看「当前」，密钥填在对应条目里",
        ["当前"] = "当前使用的供应商键名（deepseek / openai / gemini）",
        ["列表"] = "供应商条目，键名可以自定义，新增的一起填密钥和地址",
        ["名称"] = "显示用名称",
        ["地址"] = "OpenAI 兼容接口的 Base URL",
        ["最大Token参数"] = "max_tokens；OpenAI 新模型用 max_completion_tokens",
        ["支持温度"] = "该接口是否接受 temperature 参数",
        ["支持流式用量"] = "流式响应里是否返回 usage",
        ["支持工具调用"] = "是否支持 function calling",
        ["密钥"] = "API Key（明文保存在本文件）",
        ["默认模型"] = "该供应商的默认模型",

        // ---------- 聊天 ----------
        ["聊天"] = "聊天栏接话规则",
        ["聊天.启用"] = "总开关；关掉后不读聊天、不回复",
        ["回复模式"] = "智能 = 打分决定；全部 = 每条都回；提及 = 叫到名字才回",
        ["抢话判定"] = "关掉后每条都回，但更慢、更吵",
        ["判定关闭延迟秒"] = "抢话判定关掉后，每条回复前的等待秒数",
        ["智能阈值"] = "智能模式的开口分数线，越高越沉默",
        ["对话窗口秒"] = "和同一个人连续对话的有效期",
        ["全局冷却秒"] = "刚说过话的几秒内不接话，避免刷屏",
        ["每日回复上限"] = "每天主动接话的上限，超过后只回点名",
        ["公屏回复"] = "true = 公开回复；false = 只私聊说话的人",
        ["最短字数"] = "短于此字数的消息不接",
        ["死亡回应"] = "玩家死亡时说一句",
        ["事件播报"] = "Boss 出现/击杀、血月、日食、下雨的播报",
        ["死亡冷却秒"] = "同一玩家两次死亡回应的最小间隔",

        // ---------- 记忆 ----------
        ["记忆"] = "长期记忆与关系度",
        ["记忆.启用"] = "总开关；关掉后不读也不写记忆",
        ["玩家可删除"] = "允许玩家用 /aig memory forget 删除自己的记忆",
        ["记忆预算Token"] = "每次对话注入记忆的长度上限",
        ["空闲摘要分钟"] = "玩家多久不说话后整理一次记忆",
        ["摘要触发轮数"] = "对话超过多少轮就压缩成摘要",
        ["保留最近轮数"] = "压缩后保留的最近对话轮数",

        // ---------- 问候 ----------
        ["问候"] = "玩家进服时的主动欢迎",
        ["问候.启用"] = "是否欢迎进服的玩家",
        ["最小间隔小时"] = "同一个人多久欢迎一次",
        ["静默时段"] = "该时段内不欢迎（本地时间）",
        ["最长字数"] = "欢迎语最长字数",
        ["延迟秒"] = "进服后等多久再发，给客户端加载时间",
        ["问候.公屏"] = "true = 全服可见；false = 只给本人",

        // ---------- 关怀 ----------
        ["关怀"] = "深夜与连续死亡时的关心",
        ["深夜关怀"] = "真实时间深夜时提醒休息",
        ["深夜时段"] = "关怀生效的时间段",
        ["深夜间隔小时"] = "同一个人多久提醒一次",
        ["连死次数"] = "连续死亡多少次触发安慰",
        ["连死窗口分钟"] = "连续死亡的判定窗口",

        // ---------- 主动消息 ----------
        ["主动消息"] = "服务器安静时自己嘀咕一句",
        ["主动消息.启用"] = "是否允许主动开口",
        ["间隔分钟"] = "两次主动消息的最小间隔",
        ["安静分钟"] = "公屏安静多久后才开口",
        ["每日上限"] = "每天最多主动说几次",
        ["主动消息.公屏"] = "true = 对全服说；false = 只私聊触发者",

        // ---------- 指令 ----------
        ["指令"] = "允许 AI 调用 TShock 指令（仍受玩家自身权限约束）",
        ["指令.启用"] = "是否把 run_command / list_commands 交给模型",
        ["禁止指令"] = "无论玩家权限如何都不给 AI 的指令",

        // ---------- 知识库 ----------
        ["知识库"] = "离线 Wiki 的存储方式",
        ["引擎"] = "json = 全内存（默认）；sqlite = FTS5；auto = 有库用库，没库用 json",
        ["数据库文件"] = "SQLite 文件名，放在 tshock/AIAgent/ 下",

        // ---------- 工具 ----------
        ["工具"] = "单个工具的开关与每日次数；键名是工具名",

        // ---------- 限额与输出 ----------
        ["冷却秒"] = "同一玩家两次对话的最小间隔",
        ["每日Token预算"] = "单个玩家每天可用的 token 上限",
        ["最大输入字数"] = "超长消息直接拒绝",
        ["回复最大字数"] = "单次回复的总字数上限",
        ["单条气泡字数"] = "每行聊天气泡的字数上限",
        ["气泡间隔毫秒"] = "多行气泡之间的发送间隔",
        ["启用流式"] = "边生成边显示；关掉则整段生成后一次发出",
        ["启用工具"] = "允许模型调用工具（查配方、给物品等）",
        ["调试日志"] = "记录仲裁分数、工具调用等细节；排查问题时开",
    };

    public static string Write(PluginConfig cfg)
    {
        var node = JsonSerializer.SerializeToNode(cfg, NodeOptions) ?? new JsonObject();
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        WriteValue(sb, node, 0, string.Empty);
        sb.AppendLine();
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, JsonNode? node, int indent, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                WriteObject(sb, obj, indent, path);
                break;
            case JsonArray arr:
                WriteArray(sb, arr, indent);
                break;
            default:
                sb.Append(node?.ToJsonString(NodeOptions) ?? "null");
                break;
        }
    }

    private static void WriteObject(StringBuilder sb, JsonObject obj, int indent, string path)
    {
        sb.AppendLine("{");
        var pad = new string(' ', (indent + 1) * 2);
        var total = obj.Count;
        var index = 0;

        foreach (var pair in obj)
        {
            var childPath = path.Length == 0 ? pair.Key : path + "." + pair.Key;
            var comment = CommentFor(childPath);

            if (comment is not null)
            {
                foreach (var line in comment.Split('\n'))
                {
                    sb.Append(pad).Append("// ").AppendLine(line.Trim());
                }
            }

            sb.Append(pad).Append('"').Append(pair.Key).Append("\": ");
            WriteValue(sb, pair.Value, indent + 1, childPath);

            if (++index < total) sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(new string(' ', indent * 2)).Append('}');
    }

    private static void WriteArray(StringBuilder sb, JsonArray arr, int indent)
    {
        if (arr.All(item => item is JsonValue))
        {
            sb.Append('[');
            for (var i = 0; i < arr.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(arr[i]?.ToJsonString(NodeOptions) ?? "null");
            }

            sb.Append(']');
            return;
        }

        sb.AppendLine("[");
        var pad = new string(' ', (indent + 1) * 2);
        for (var i = 0; i < arr.Count; i++)
        {
            sb.Append(pad);
            WriteValue(sb, arr[i], indent + 1, string.Empty);
            if (i < arr.Count - 1) sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(new string(' ', indent * 2)).Append(']');
    }

    private static string? CommentFor(string path)
    {
        if (Comments.TryGetValue(path, out var exact)) return exact;

        if (path.StartsWith("工具.", System.StringComparison.Ordinal))
        {
            if (path.EndsWith(".启用", System.StringComparison.Ordinal)) return "该工具是否对模型开放";
            if (path.EndsWith(".每日次数", System.StringComparison.Ordinal)) return "每天最多调用几次（0 = 不限制）";
        }

        var name = path[(path.LastIndexOf('.') + 1)..];
        return Comments.TryGetValue(name, out var byName) ? byName : null;
    }
}
