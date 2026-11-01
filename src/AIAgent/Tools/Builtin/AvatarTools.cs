using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terraria;
using TShockAPI;
using AIAgent.Avatar;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

internal static class AvatarToolHelper
{
    public const string Permission = "aiagent.avatar";

    public static bool Ready(out string error)
    {
        if (!AvatarBridge.Available)
        {
            error = "没有检测到分身插件（AIAgentAvatar），先让管理员安装并创建分身。";
            return false;
        }

        if (AvatarBridge.Status() is null)
        {
            error = "分身还没创建，让管理员执行 /fake add 或打开自动创建。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static object? Snapshot()
    {
        var s = AvatarBridge.Status();
        if (s is null) return null;

        return new
        {
            slot = s.Slot,
            name = s.Name,
            state = s.State,
            position = new { x = Math.Round(s.X / 16f, 1), y = Math.Round(s.Y / 16f, 1) },
            life = s.Life,
            lifeMax = s.LifeMax,
            speaking = s.Speaking,
            follow = s.Follow,
            holding = s.Holding,
        };
    }
}

public sealed class AvatarStatusTool : ITool
{
    public string Name => "avatar_status";
    public string Description => "查看你的游戏分身（假人）的状态：是否在线、在做什么、位置、血量、在跟谁。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { exists = false, note = error }));

        return new ToolResult(true, ToolArgs.Ok(new { exists = true, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarSayTool : ITool
{
    public string Name => "avatar_say";
    public string Description => "让你的分身（假人）在公屏说一句话。这是分身说话的唯一方式，不要用聊天消息冒充它。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject { ["type"] = "string", ["description"] = "要说的话，一句话，别太长", ["minLength"] = 1, ["maxLength"] = 120 },
        },
        ["required"] = new JsonArray("text"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var text = ToolArgs.Str(args, "text");
        if (string.IsNullOrWhiteSpace(text)) return new ToolValidation(false, "缺少 text");
        if (text.Length > 120) return new ToolValidation(false, "太长了，压到 120 字以内");
        return new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var text = ToolArgs.Str(args, "text")!.Trim();
        var ok = AvatarBridge.Say(text);
        return new ToolResult(true, ToolArgs.Ok(new { ok, said = ok ? text : null }));
    }
}

public sealed class AvatarFollowTool : ITool
{
    public string Name => "avatar_follow";
    public string Description => "让分身跟着某个玩家（默认跟你）。分身会自动帮忙打附近的怪。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["target"] = new JsonObject { ["type"] = "string", ["description"] = "玩家名，留空表示跟着说话的人", ["maxLength"] = 24 },
        },
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var name = ToolArgs.Str(args, "target");
        var target = string.IsNullOrWhiteSpace(name) ? caller : ToolArgs.FindOnline(name);
        if (target is null)
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = "找不到这个在线玩家" }));

        var ok = AvatarBridge.Follow(target);
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = target.Name }));
    }
}

public sealed class AvatarGotoTool : ITool
{
    public string Name => "avatar_goto";
    public string Description => "让分身走到指定坐标（单位是格，不是像素）。到达后会停在原地，除非再下指令。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["x"] = new JsonObject { ["type"] = "integer", ["description"] = "目标 X（格）" },
            ["y"] = new JsonObject { ["type"] = "integer", ["description"] = "目标 Y（格）" },
        },
        ["required"] = new JsonArray("x", "y"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var x = ToolArgs.Int(args, "x") ?? 0;
        var y = ToolArgs.Int(args, "y") ?? 0;
        if (Math.Abs(x) > 9000 || Math.Abs(y) > 9000)
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = "坐标超出世界范围" }));

        var ok = AvatarBridge.Goto(x * 16f, y * 16f);
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = new { x, y } }));
    }
}

public sealed class AvatarAttackTool : ITool
{
    public string Name => "avatar_attack";
    public string Description => "让分身去打附近的一只怪（默认最近的敌对 NPC）。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var avatar = AvatarBridge.Status()!;
        var center = new Microsoft.Xna.Framework.Vector2(avatar.X, avatar.Y);

        var target = Main.npc
            .Where(n => n is { active: true, friendly: false, townNPC: false } && n.life > 0)
            .OrderBy(n => n.Center.Distance(center))
            .FirstOrDefault();

        if (target is null)
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = "附近没有可攻击的怪" }));

        var ok = AvatarBridge.Attack(target.whoAmI);
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = Lang.GetNPCNameValue(target.type) }));
    }
}

public sealed class AvatarStopTool : ITool
{
    public string Name => "avatar_stop";
    public string Description => "让分身停下来：不再跟随、不再攻击、不再乱走。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        return new ToolResult(true, ToolArgs.Ok(new { ok = AvatarBridge.Stop() }));
    }
}
