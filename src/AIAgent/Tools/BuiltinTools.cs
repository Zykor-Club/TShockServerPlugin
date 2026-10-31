using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TShockAPI;
using Terraria;
using Terraria.ID;

namespace AIAgent.Tools.Builtin;

public sealed class StatusTool : ITool
{
    public string Name => "get_my_status";
    public string Description => "查看说话玩家自己的状态：血量、坐标、是否死亡。玩家问“我怎么样/我在哪/我还剩多少血”时调用。";
    public string Permission => "aiagent.use";
    public bool RequiresRealPlayer => true;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) =>
        caller.RealPlayer ? new ToolValidation(true, null) : new ToolValidation(false, "只能在游戏内使用");

    public ToolResult Execute(JsonElement args, TSPlayer caller) => new(true, ToolArgs.Ok(new
    {
        hp = caller.TPlayer.statLife,
        maxHp = caller.TPlayer.statLifeMax2,
        x = (int)(caller.TPlayer.position.X / 16),
        y = (int)(caller.TPlayer.position.Y / 16),
        dead = caller.TPlayer.dead,
    }));
}

public sealed class WorldInfoTool : ITool
{
    public string Name => "get_world_info";
    public string Description => "查看服务器状态：当前是白天还是夜晚、游戏内时间、在线人数、是否在打 Boss。玩家问“现在几点/服里几个人/天黑了吗”时调用。";
    public string Permission => "aiagent.use";
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var clock = Main.time / 3600.0 + 4.5;
        if (!Main.dayTime) clock += 15.0;
        clock %= 24.0;

        var bossFight = Main.npc.Any(n => n.active && n.boss);

        return new ToolResult(true, ToolArgs.Ok(new
        {
            phase = Main.dayTime ? "白天" : "夜晚",
            clock = $"{(int)Math.Floor(clock):00}:{(int)Math.Floor(clock % 1.0 * 60.0):00}",
            players = TShock.Players.Count(p => p is { Active: true }),
            bossFight,
            rain = Main.raining,
            windSpeed = (int)Main.windSpeedCurrent,
        }));
    }
}

public sealed class HealTool : ITool
{
    public string Name => "heal_player";
    public string Description => "治疗玩家。玩家说“给我回血/帮我奶一口”时调用；不填 target 就是治疗说话的人。";
    public string Permission => TShockAPI.Permissions.heal;
    public bool RequiresRealPlayer => true;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["target"] = new JsonObject { ["type"] = "string", ["description"] = "玩家名；省略表示自己" },
        },
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var target = ToolArgs.Str(args, "target");
        var player = string.IsNullOrWhiteSpace(target) ? caller : ToolArgs.FindOnline(target);

        if (player is null) return new ToolValidation(false, $"找不到在线玩家 {target}");
        if (player.Dead) return new ToolValidation(false, "死亡状态下不能治疗");

        return new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var target = ToolArgs.Str(args, "target");
        var player = string.IsNullOrWhiteSpace(target) ? caller : ToolArgs.FindOnline(target)!;

        player.Heal(player.TPlayer.statLifeMax2);
        player.SendSuccessMessage("[c/00FF00:你被治疗了]");

        return new ToolResult(true, ToolArgs.Ok(new { ok = true, target = player.Name }));
    }
}

public sealed class GiveItemTool : ITool
{
    public string Name => "give_item";
    public string Description => "给玩家发放物品。仅当玩家明确要求获得某件具体物品时调用；玩家只说“想要点好东西”就先反问要什么，不要猜。itemId 是泰拉瑞亚物品 ID。";
    public string Permission => TShockAPI.Permissions.give;
    public bool RequiresRealPlayer => true;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["itemId"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = ItemID.Count - 1,
                ["description"] = "物品 ID（1.4.5.8）",
            },
            ["stack"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 999 },
            ["target"] = new JsonObject { ["type"] = "string", ["description"] = "玩家名；省略表示自己" },
        },
        ["required"] = new JsonArray("itemId"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var id = ToolArgs.Int(args, "itemId");
        if (id is null) return new ToolValidation(false, "缺少 itemId");
        if (id is < 1 || id >= ItemID.Count) return new ToolValidation(false, "物品 ID 越界");

        var stack = ToolArgs.Int(args, "stack") ?? 1;
        if (stack is < 1 or > 999) return new ToolValidation(false, "数量越界");

        var target = ToolArgs.Str(args, "target");
        if (!string.IsNullOrWhiteSpace(target) && ToolArgs.FindOnline(target) is null)
        {
            return new ToolValidation(false, $"找不到在线玩家 {target}");
        }

        return new ToolValidation(true, null);
    }

    public string Describe(JsonElement args)
    {
        var id = ToolArgs.Int(args, "itemId") ?? 0;
        var stack = ToolArgs.Int(args, "stack") ?? 1;
        var target = ToolArgs.Str(args, "target");
        return string.IsNullOrWhiteSpace(target)
            ? $"给你 {stack} 个物品 #{id}"
            : $"给 {target} {stack} 个物品 #{id}";
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var id = ToolArgs.Int(args, "itemId")!.Value;
        var stack = ToolArgs.Int(args, "stack") ?? 1;
        var target = ToolArgs.Str(args, "target");
        var player = string.IsNullOrWhiteSpace(target) ? caller : ToolArgs.FindOnline(target)!;

        player.GiveItem(id, stack);

        return new ToolResult(true, ToolArgs.Ok(new { ok = true, itemId = id, stack, target = player.Name }));
    }
}

public sealed class TeleportTool : ITool
{
    public string Name => "teleport_to_player";
    public string Description => "把说话的人传送到另一个在线玩家身边。只在玩家明确要求“把我传到某人那儿”时调用。";
    public string Permission => TShockAPI.Permissions.tp;
    public bool RequiresRealPlayer => true;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["target"] = new JsonObject { ["type"] = "string", ["description"] = "目标玩家名" },
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
        if (found.Index == caller.Index) return new ToolValidation(false, "那就是你自己");

        // 对齐 TShock /tp：对方关闭了接收传送且自己没有 override 权限时不允许
        if (!found.TPAllow && !caller.HasPermission(TShockAPI.Permissions.tpoverride))
        {
            return new ToolValidation(false, $"{found.Name} 关闭了接收传送");
        }

        return new ToolValidation(true, null);
    }

    public string Describe(JsonElement args) => $"把你传送到 {ToolArgs.Str(args, "target")} 身边";

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var target = ToolArgs.FindOnline(ToolArgs.Str(args, "target"))!;

        // 和 TShock /tp 一样用 Bottom + useBottom
        if (!caller.Teleport(target.TPlayer.Bottom, true))
        {
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, reason = "传送失败（目标位置不可达）" }));
        }

        if (!caller.HasPermission(TShockAPI.Permissions.tpsilent))
        {
            target.SendInfoMessage($"[c/00FF00:{caller.Name} 传送到了你身边]");
        }

        return new ToolResult(true, ToolArgs.Ok(new { ok = true, target = target.Name }));
    }
}

public sealed class SetTimeTool : ITool
{
    public string Name => "set_time";
    public string Description => "把服务器时间切到指定时段。玩家说“天黑了/我想要白天/改成晚上”时调用。";
    public string Permission => TShockAPI.Permissions.time;
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["phase"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("白天", "正午", "夜晚", "午夜"),
                ["description"] = "目标时段",
            },
        },
        ["required"] = new JsonArray("phase"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        var phase = ToolArgs.Str(args, "phase");
        return phase is "白天" or "正午" or "夜晚" or "午夜"
            ? new ToolValidation(true, null)
            : new ToolValidation(false, "phase 只能是 白天/正午/夜晚/午夜");
    }

    public string Describe(JsonElement args) => $"把时间改成 {ToolArgs.Str(args, "phase")}";

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var phase = ToolArgs.Str(args, "phase")!;
        switch (phase)
        {
            case "白天":
                TSPlayer.Server.SetTime(true, 0.0);
                break;
            case "正午":
                TSPlayer.Server.SetTime(true, 27000.0);
                break;
            case "夜晚":
                TSPlayer.Server.SetTime(false, 0.0);
                break;
            default:
                TSPlayer.Server.SetTime(false, 16200.0);
                break;
        }

        return new ToolResult(true, ToolArgs.Ok(new { ok = true, phase }));
    }
}
