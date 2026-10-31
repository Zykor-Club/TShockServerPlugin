using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terraria;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

/// <summary>反查：这个材料能做什么。</summary>
public sealed class FindRecipeTool : ITool
{
    public string Name => "find_recipes_for";
    public string Description => "反查配方：某个材料可以用来合成哪些东西。玩家问“这个能做什么/这堆铁有啥用”时调用。material 传中文物品名。";
    public string Permission => "aiagent.use";
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["material"] = new JsonObject { ["type"] = "string", ["description"] = "材料名，例如 铁锭、木材、凝胶" },
        },
        ["required"] = new JsonArray("material"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var material = ToolArgs.Str(args, "material");
        return string.IsNullOrWhiteSpace(material)
            ? new ToolValidation(false, "缺少 material")
            : new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var material = ToolArgs.Str(args, "material")!.Trim();
        var index = PluginState.Recipes;

        if (!index.TryResolve(material, out var id, out var name))
        {
            return new ToolResult(true, ToolArgs.Ok(new { found = false, note = "游戏里没有这个物品名。" }));
        }

        var uses = index.FindUses(id, 8);
        return new ToolResult(true, ToolArgs.Ok(new
        {
            found = true,
            material = new { id, name },
            count = uses.Count,
            usedIn = uses.Select(u => new { u.ResultName, station = u.Station, u.ResultStack }).ToArray(),
        }));
    }
}

/// <summary>天气控制。</summary>
public sealed class WeatherTool : ITool
{
    public string Name => "set_weather";
    public string Description => "切换天气：下雨或放晴。玩家说“下雨了/把雨停了/能不能出太阳”时调用。";
    public string Permission => TShockAPI.Permissions.rain;
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["weather"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("下雨", "放晴"),
            },
        },
        ["required"] = new JsonArray("weather"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var weather = ToolArgs.Str(args, "weather");
        return weather is "下雨" or "放晴"
            ? new ToolValidation(true, null)
            : new ToolValidation(false, "weather 只能是 下雨/放晴");
    }

    public string Describe(JsonElement args) => $"把天气改成 {ToolArgs.Str(args, "weather")}";

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var weather = ToolArgs.Str(args, "weather")!;
        if (weather == "下雨") Main.StartRain();
        else Main.StopRain();

        return new ToolResult(true, ToolArgs.Ok(new { ok = true, weather }));
    }
}

/// <summary>把指定玩家传送到说话人身边。</summary>
public sealed class BringPlayerTool : ITool
{
    public string Name => "bring_player";
    public string Description => "把某个在线玩家传送到你身边（不是你去他那儿）。仅当说话人明确要求“把某某叫过来/拉过来”时调用。";
    public string Permission => TShockAPI.Permissions.tpothers;
    public bool RequiresRealPlayer => true;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["target"] = new JsonObject { ["type"] = "string", ["description"] = "要拉过来的玩家名" },
        },
        ["required"] = new JsonArray("target"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var target = ToolArgs.Str(args, "target");
        if (string.IsNullOrWhiteSpace(target)) return new ToolValidation(false, "缺少 target");

        var found = ToolArgs.FindOnline(target);
        if (found is null) return new ToolValidation(false, $"找不到在线玩家 {target}");
        if (found.Index == caller.Index) return new ToolValidation(false, "那就是他自己");

        if (!found.TPAllow && !caller.HasPermission(TShockAPI.Permissions.tpoverride))
        {
            return new ToolValidation(false, $"{found.Name} 关闭了接收传送");
        }

        return new ToolValidation(true, null);
    }

    public string Describe(JsonElement args) => $"把 {ToolArgs.Str(args, "target")} 传到你身边";

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var target = ToolArgs.FindOnline(ToolArgs.Str(args, "target"))!;

        if (!target.Teleport(caller.TPlayer.Bottom, true))
        {
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, reason = "传送失败（目标位置不可达）" }));
        }

        target.SendInfoMessage($"[c/00FF00:你被传送到 {caller.Name} 身边了]");
        return new ToolResult(true, ToolArgs.Ok(new { ok = true, target = target.Name }));
    }
}
