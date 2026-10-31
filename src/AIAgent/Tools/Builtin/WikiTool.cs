using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

/// <summary>离线 Wiki 检索。返回的是"资料"，不是指令。</summary>
public sealed class WikiTool : ITool
{
    public string Name => "search_wiki";

    public string Description =>
        "查询《泰拉瑞亚》中文 Wiki 离线资料库。玩家问 Boss 顺序、进度阶段、材料出处、事件触发、机制、物品用途时优先调用。" +
        "不要凭印象编造游戏数据；查不到就说查不到。";

    public string Permission => "aiagent.use";


    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "要查的关键词，例如：世纪之花、狱石、困难模式、日食",
                ["minLength"] = 2,
                ["maxLength"] = 30,
            },
        },
        ["required"] = new JsonArray("query"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var query = ToolArgs.Str(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return new ToolValidation(false, "缺少 query");
        if (query.Length < 2) return new ToolValidation(false, "关键词太短");
        if (query.Length > 30) return new ToolValidation(false, "关键词太长");
        return new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var query = ToolArgs.Str(args, "query")!.Trim();
        if (PluginState.Wiki is null)
        {
            return new ToolResult(true, ToolArgs.Ok(new { found = false, note = "知识库没有加载，请如实告诉玩家查不了。" }));
        }

        var hits = PluginState.Wiki.Search(query, limit: 2, excerptChars: 450);

        if (hits.Count == 0)
        {
            return new ToolResult(true, ToolArgs.Ok(new
            {
                found = false,
                note = "知识库里没有这条，请如实告诉玩家你没查到，不要编。",
            }));
        }

        return new ToolResult(true, ToolArgs.Ok(new
        {
            found = true,
            results = hits.Select(h => new { title = h.Title, url = h.Url, excerpt = h.Excerpt }).ToArray(),
        }));
    }
}
