using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terraria;
using Terraria.ID;
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
            stuck = s.Stuck,
            stuckSeconds = s.StuckSeconds,
            attacking = s.Attacking,
            goal = s.Goal,
            goalIndex = s.GoalIndex,
            goalCount = s.GoalCount,
        };
    }
}

public sealed class AvatarStatusTool : ITool
{
    public string Name => "avatar_status";
    public string Description => "查看分身的实时状态（是否在线、在做什么、坐标、血量、跟随目标）。下 goto/attack 之前先用它确认位置。";
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
    public string Description => "让分身在公屏说一句话，会显示成它自己的名字。这是分身说话的唯一方式，不要用聊天消息冒充它。";
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
    public string Description => "让分身跟着某个玩家（默认跟你），并自动打附近的怪。会取消之前的 goto 和指定攻击目标。";
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
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = target.Name, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarGotoTool : ITool
{
    public string Name => "avatar_goto";
    public string Description => "让分身走到指定坐标（单位是格，不是像素）。距离超过 100 格会直接传送；到达后停在原地。会取消跟随。";
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
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = new { x, y }, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarAttackTool : ITool
{
    public string Name => "avatar_attack";
    public string Description => "让分身去打附近最近的一只敌对 NPC。附近没有怪时返回 ok=false；会取消跟随和定点。";
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
        return new ToolResult(true, ToolArgs.Ok(new { ok, target = Lang.GetNPCNameValue(target.type), avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarStopTool : ITool
{
    public string Name => "avatar_stop";
    public string Description => "让分身原地站住：取消跟随、指定攻击目标和定点，停在当前位置不再乱走。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var ok = AvatarBridge.Stop();
        return new ToolResult(true, ToolArgs.Ok(new { ok, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarScanTool : ITool
{
    public string Name => "avatar_scan";
    public string Description => "扫描分身周围：附近的怪、玩家、矿石。决定【守/撤/打/去挖】之前先看一眼。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["radius"] = new JsonObject { ["type"] = "integer", ["description"] = "扫描半径（格），10~60，默认 30", ["minimum"] = 10, ["maximum"] = 60 },
        },
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var s = AvatarBridge.Status()!;
        var radius = Math.Clamp(ToolArgs.Int(args, "radius") ?? 40, 10, 60);
        var cx = (int)(s.X / 16f);
        var cy = (int)(s.Y / 16f);

        var monsters = Main.npc
            .Where(n => n is { active: true, friendly: false, townNPC: false } && n.life > 0)
            .Select(n => new { npc = n, dist = Math.Abs((int)(n.Center.X / 16f) - cx) + Math.Abs((int)(n.Center.Y / 16f) - cy) })
            .Where(x => x.dist <= radius)
            .OrderBy(x => x.dist)
            .Take(5)
            .Select(x => new
            {
                name = Lang.GetNPCNameValue(x.npc.type),
                x = (int)(x.npc.Center.X / 16f),
                y = (int)(x.npc.Center.Y / 16f),
                life = x.npc.life,
                distance = x.dist,
            })
            .ToArray();

        var players = TShock.Players
            .Where(p => p is { Active: true } && !string.Equals(p.Name, s.Name, StringComparison.Ordinal))
            .Select(p => new { p, dist = Math.Abs((int)(p.TPlayer.position.X / 16f) - cx) + Math.Abs((int)(p.TPlayer.position.Y / 16f) - cy) })
            .Where(x => x.dist <= radius)
            .OrderBy(x => x.dist)
            .Take(5)
            .Select(x => new
            {
                name = x.p.Name,
                x = (int)(x.p.TPlayer.position.X / 16f),
                y = (int)(x.p.TPlayer.position.Y / 16f),
                life = x.p.TPlayer.statLife,
                distance = x.dist,
            })
            .ToArray();

        var ores = new Dictionary<int, (int Count, int X, int Y)>();
        for (var x = cx - radius; x <= cx + radius; x++)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
            {
                if (!WorldGen.InWorld(x, y, 1)) continue;
                var tile = Main.tile[x, y];
                if (!tile.active() || !TileID.Sets.Ore[tile.type]) continue;

                var type = (int)tile.type;
                if (!ores.TryGetValue(type, out var found))
                {
                    ores[type] = (1, x, y);
                }
                else
                {
                    ores[type] = (found.Count + 1, found.X, found.Y);
                }
            }
        }

        var oreList = ores
            .Select(kv => new
            {
                name = OreName(kv.Key),
                count = kv.Value.Count,
                nearest = new { x = kv.Value.X, y = kv.Value.Y },
            })
            .OrderByDescending(o => o.count)
            .Take(6)
            .ToArray();

        return new ToolResult(true, ToolArgs.Ok(new
        {
            ok = true,
            position = new { x = cx, y = cy },
            life = s.Life,
            lifeMax = s.LifeMax,
            stuck = s.Stuck,
            attacking = s.Attacking,
            radius,
            monsters,
            players,
            ores = oreList,
        }));
    }

    private static string OreName(int type) => type switch
    {
        TileID.Copper => "铜矿",
        TileID.Tin => "锡矿",
        TileID.Iron => "铁矿",
        TileID.Lead => "铅矿",
        TileID.Silver => "银矿",
        TileID.Tungsten => "钨矿",
        TileID.Gold => "金矿",
        TileID.Platinum => "铂金矿",
        TileID.Demonite => "魔矿",
        TileID.Crimtane => "猩红矿",
        TileID.Meteorite => "陨石",
        TileID.Hellstone => "狱石",
        TileID.Cobalt => "钴矿",
        TileID.Palladium => "钯金矿",
        TileID.Mythril => "秘银矿",
        TileID.Orichalcum => "山铜矿",
        TileID.Adamantite => "精金矿",
        TileID.Titanium => "钛金矿",
        TileID.Chlorophyte => "叶绿矿",
        TileID.LunarOre => "夜明矿",
        _ => TileID.Search.GetName(type),
    };
}

public sealed class AvatarGuardTool : ITool
{
    public string Name => "avatar_guard";
    public string Description => "让分身驻守一片区域：站在中心点，附近的怪会去打，追远了会回来。不填坐标就守当前位置。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["x"] = new JsonObject { ["type"] = "integer", ["description"] = "守点中心 X（格），省略则用分身处" },
            ["y"] = new JsonObject { ["type"] = "integer", ["description"] = "守点中心 Y（格）" },
            ["radius"] = new JsonObject { ["type"] = "integer", ["description"] = "守点半径（格），默认 15", ["minimum"] = 3, ["maximum"] = 60 },
        },
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var s = AvatarBridge.Status()!;
        var x = ToolArgs.Int(args, "x") ?? (int)(s.X / 16f);
        var y = ToolArgs.Int(args, "y") ?? (int)(s.Y / 16f);
        var radius = Math.Clamp(ToolArgs.Int(args, "radius") ?? 15, 3, 60);

        var ok = AvatarBridge.Guard(x * 16f, y * 16f, radius * 16f);
        return new ToolResult(true, ToolArgs.Ok(new { ok, center = new { x, y }, radius, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarPatrolTool : ITool
{
    public string Name => "avatar_patrol";
    public string Description => "让分身在几个点之间循环巡逻（2~6 个点，单位格）。路上遇到怪会先打，打完继续巡逻。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["points"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "巡逻点列表，2~6 个",
                ["minItems"] = 2,
                ["maxItems"] = 6,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["x"] = new JsonObject { ["type"] = "integer" },
                        ["y"] = new JsonObject { ["type"] = "integer" },
                    },
                    ["required"] = new JsonArray("x", "y"),
                },
            },
        },
        ["required"] = new JsonArray("points"),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller)
    {
        if (!args.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return new ToolValidation(false, "缺少 points 数组");
        if (points.GetArrayLength() < 2 || points.GetArrayLength() > 6)
            return new ToolValidation(false, "巡逻点需要 2~6 个");
        return new ToolValidation(true, null);
    }

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var flat = new List<float>();
        foreach (var point in args.GetProperty("points").EnumerateArray())
        {
            var x = point.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
            var y = point.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
            flat.Add(x * 16f);
            flat.Add(y * 16f);
        }

        var ok = AvatarBridge.Patrol(flat.ToArray());
        return new ToolResult(true, ToolArgs.Ok(new { ok, count = flat.Count / 2, avatar = AvatarToolHelper.Snapshot() }));
    }
}

public sealed class AvatarReturnTool : ITool
{
    public string Name => "avatar_return";
    public string Description => "让分身返回世界出生点，到了之后自动驻守。";
    public string Permission => AvatarToolHelper.Permission;
    public bool RequiresRealPlayer => false;
    public JsonObject BuildParameters() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        if (!AvatarToolHelper.Ready(out var error))
            return new ToolResult(true, ToolArgs.Ok(new { ok = false, note = error }));

        var ok = AvatarBridge.Return();
        return new ToolResult(true, ToolArgs.Ok(new { ok, avatar = AvatarToolHelper.Snapshot() }));
    }
}
