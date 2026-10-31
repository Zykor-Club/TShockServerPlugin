using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

/// <summary>查询游戏内合成配方（权威数据，来自 Main.recipe）。</summary>
public sealed class RecipeTool : ITool
{
    public string Name => "get_recipe";

    public string Description =>
        "查询《泰拉瑞亚》合成配方（直接读取游戏数据，绝对准确）。" +
        "玩家问某个物品怎么做、需要什么材料、在哪个工作站合成时调用。" +
        "item 传中文物品名（例如 天顶剑、泰拉刃、铂金币），也可以传数字 ID。";

    public string Permission => "aiagent.use";


    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["item"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "物品中文名或数字 ID",
                ["minLength"] = 1,
                ["maxLength"] = 30,
            },
        },
        ["required"] = new JsonArray("item"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var item = ToolArgs.Str(args, "item");
        if (string.IsNullOrWhiteSpace(item)) return new ToolValidation(false, "缺少 item");
        if (item.Length > 30) return new ToolValidation(false, "名字太长");
        return new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var item = ToolArgs.Str(args, "item")!.Trim();
        var index = PluginState.Recipes;

        if (!index.TryResolve(item, out var id, out var name))
        {
            return new ToolResult(true, ToolArgs.Ok(new
            {
                found = false,
                note = "游戏里没有这个物品名，换个说法或查 Wiki。",
            }));
        }

        var recipes = index.RecipesFor(id);
        if (recipes.Count == 0)
        {
            return new ToolResult(true, ToolArgs.Ok(new
            {
                found = true,
                item = new { id, name },
                craftable = false,
                note = "这个物品没有合成配方（可能是掉落、购买或自然生成）。",
            }));
        }

        var payload = new
        {
            found = true,
            item = new { id, name },
            craftable = true,
            recipes = recipes.Take(3).Select(r => new
            {
                resultStack = r.ResultStack,
                stations = r.Stations,
                ingredients = r.Ingredients.Take(12).Select(i => new { i.Id, i.Name, i.Stack }).ToArray(),
            }).ToArray(),
        };

        return new ToolResult(true, ToolArgs.Ok(payload));
    }
}
