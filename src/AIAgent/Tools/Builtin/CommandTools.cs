using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

/// <summary>
/// 通用指令执行：以玩家身份走 TShock 自己的权限检查，不做任何提权。
/// </summary>
public sealed class RunCommandTool : ITool
{
    public string Name => "run_command";

    public string Description =>
        "执行一条 TShock 指令（以说话玩家本人的身份和权限）。只在玩家明确要求执行指令时调用；" +
        "不要自己发明指令，不确定有哪些指令先调用 list_commands。";

    public string Permission => "aiagent.tool.command";


    public bool RequiresRealPlayer => false;

    public bool IsAvailable(Config.PluginConfig cfg) => cfg.Commands.Enabled;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "完整指令，不要带斜杠，例如：give 74 5 / time day / butcher",
                ["minLength"] = 2,
                ["maxLength"] = 200,
            },
        },
        ["required"] = new JsonArray("command"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var parsed = CommandSpec.Parse(ToolArgs.Str(args, "command"), PluginState.Config.Commands.Blocked);
        return parsed.Ok ? new ToolValidation(true, null) : new ToolValidation(false, parsed.Reason);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var cfg = PluginState.Config;
        var parsed = CommandSpec.Parse(ToolArgs.Str(args, "command"), cfg.Commands.Blocked);
        if (!parsed.Ok)
        {
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, reason = parsed.Reason }));
        }

        var command = TShockAPI.Commands.ChatCommands.FirstOrDefault(c => c.HasAlias(parsed.Name));
        if (command is null)
        {
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, reason = "没有这个指令" }));
        }

        if (!command.CanRun(caller))
        {
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, reason = "你没有这个指令的权限" }));
        }

        TShockAPI.Commands.HandleCommand(caller, TShockAPI.Commands.Specifier + parsed.Command);
        return new ToolResult(true, ToolArgs.Ok(new { ok = true, command = parsed.Command }));
    }
}

/// <summary>列出当前玩家能用的指令，供模型挑。</summary>
public sealed class ListCommandsTool : ITool
{
    private static readonly Regex ColorTag = new(@"\[c/[0-9A-Fa-f]{6}:([^\]]*)\]", RegexOptions.Compiled);

    public string Name => "list_commands";

    public string Description => "列出当前玩家有权限使用的 TShock 指令及简短说明。不确定能执行什么指令时先调用它。";

    public string Permission => "aiagent.tool.command";


    public bool RequiresRealPlayer => false;

    public bool IsAvailable(Config.PluginConfig cfg) => cfg.Commands.Enabled;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var blocked = PluginState.Config.Commands.Blocked;

        var commands = TShockAPI.Commands.ChatCommands
            .Where(c => c.CanRun(caller))
            .Where(c => !blocked.Any(b => c.HasAlias(b)))
            .Select(c => new
            {
                name = c.Names.FirstOrDefault() ?? string.Empty,
                help = Trim(c.HelpText),
            })
            .Where(c => c.name.Length > 0)
            .DistinctBy(c => c.name)
            .OrderBy(c => c.name)
            .Take(60)
            .ToArray();

        return new ToolResult(true, ToolArgs.Ok(new
        {
            count = commands.Length,
            commands,
            note = "用法：run_command(\"指令名 参数\")，不要带斜杠。",
        }));
    }

    private static string Trim(string? help)
    {
        if (string.IsNullOrWhiteSpace(help)) return string.Empty;

        var text = ColorTag.Replace(help, "$1").Replace("\n", " ").Trim();
        return text.Length <= 46 ? text : text.Substring(0, 46) + "…";
    }
}
