using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Xna.Framework;

using Microsoft.Data.Sqlite;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace CustomDeathMessages;

[ApiVersion(2, 1)]
public class CustomDeathPlugin : TerrariaPlugin
{
    public override string Name => "CustomDeathMessages";
    public override Version Version => new(1, 0, 1);
    public override string Author => "Eustia、星梦";
    public override string Description => "自定义死亡消息";

    // PlayerDeathReason 字段反射
    // 注意: OTAPI 将原版私有字段改为 public，因此需同时使用 Public 和 NonPublic
    private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? f_player = typeof(PlayerDeathReason).GetField(
        "_sourcePlayerIndex", FieldFlags);
    private static readonly FieldInfo? f_npc = typeof(PlayerDeathReason).GetField(
        "_sourceNPCIndex", FieldFlags);
    private static readonly FieldInfo? f_proj = typeof(PlayerDeathReason).GetField(
        "_sourceProjectileLocalIndex", FieldFlags);
    private static readonly FieldInfo? f_other = typeof(PlayerDeathReason).GetField(
        "_sourceOtherIndex", FieldFlags);
    private static readonly FieldInfo? f_projType = typeof(PlayerDeathReason).GetField(
        "_sourceProjectileType", FieldFlags);
    private static readonly FieldInfo? f_itemType = typeof(PlayerDeathReason).GetField(
        "_sourceItemType", FieldFlags);
    private static readonly FieldInfo? f_itemPrefix = typeof(PlayerDeathReason).GetField(
        "_sourceItemPrefix", FieldFlags);
    private static readonly FieldInfo? f_customReason = typeof(PlayerDeathReason).GetField(
        "_sourceCustomReason", FieldFlags);

    private Configuration _config = null!;
    private readonly ConcurrentDictionary<int, (string Message, Color Color)> _pending = new();
    private SqliteConnection _db = null!;

    public CustomDeathPlugin(Main game) : base(game) { }

    public override void Initialize()
    {
        _config = Configuration.Load();

        _db = CreateDatabase();

        GetDataHandlers.KillMe += OnKillMe;
        ServerApi.Hooks.ServerBroadcast.Register(this, OnBroadcast);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

        Commands.ChatCommands.Add(new Command("customdeathmessages.reload", CdmCommand, "cdm")
        {
            HelpText = "死亡消息插件命令。用法: /cdm reload | /cdm debug | /cdm reset <玩家名>"
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GetDataHandlers.KillMe -= OnKillMe;
            ServerApi.Hooks.ServerBroadcast.Deregister(this, OnBroadcast);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
        }
        _db?.Close();

        base.Dispose(disposing);
    }

    private void CdmCommand(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            args.Player.SendInfoMessage("用法:");
            args.Player.SendInfoMessage("/cdm reload - 重载死亡消息配置");
            args.Player.SendInfoMessage("/cdm debug - 调试死亡归因检测");
            return;
        }

        switch (args.Parameters[0].ToLowerInvariant())
        {
            case "reload":
            {
                _config = Configuration.Load();
                args.Player.SendSuccessMessage("[CustomDeathMessages] 配置已重载。");
                TShock.Log.ConsoleInfo($"[CustomDeathMessages] 配置已由 {args.Player.Name} 重载。");
                break;
            }
            case "debug":
            {
                DebugFields(args);
                break;
            }
            case "reset":
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendInfoMessage("用法: /cdm reset <玩家名> - 重置指定玩家的死亡计数");
                    break;
                }
                var targetName = string.Join(" ", args.Parameters.Skip(1));
                if (targetName.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = "DELETE FROM DeathCounts";
                    cmd.ExecuteNonQuery();
                    args.Player.SendSuccessMessage("[CustomDeathMessages] 已重置所有玩家的死亡计数。");
                }
                else
                {
                    ResetDeathCount(targetName);
                    args.Player.SendSuccessMessage($"[CustomDeathMessages] 已重置 {targetName} 的死亡计数。");
                }
                break;
            }
            default:
                args.Player.SendInfoMessage("未知子命令。可用: reload, debug");
                break;
        }
    }

    /// <summary>调试命令：显示 PlayerDeathReason 各字段的反射状态</summary>
    private static void DebugFields(CommandArgs args)
    {
        args.Player.SendInfoMessage("[CustomDeathMessages] 字段反射状态:");
        args.Player.SendInfoMessage($"  f_player (NonPublic|Public) = {(f_player != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_npc    (NonPublic|Public) = {(f_npc != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_proj   (NonPublic|Public) = {(f_proj != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_other  (NonPublic|Public) = {(f_other != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_projType (NonPublic|Public) = {(f_projType != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_itemType (NonPublic|Public) = {(f_itemType != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage($"  f_customReason (NonPublic|Public) = {(f_customReason != null ? "✓ 找到" : "✗ 未找到")}");
        args.Player.SendInfoMessage(string.Empty);

        // 尝试通过 TryGetCausingEntity 公共 API 检测
        args.Player.SendInfoMessage("[CustomDeathMessages] TryGetCausingEntity 可用性检查:");
        try
        {
            var testReason = PlayerDeathReason.ByNPC(0);
            var result = testReason.TryGetCausingEntity(out _);
            args.Player.SendInfoMessage($"  ByNPC(0).TryGetCausingEntity() = {result}");

            testReason = PlayerDeathReason.ByPlayer(0);
            result = testReason.TryGetCausingEntity(out _);
            args.Player.SendInfoMessage($"  ByPlayer(0).TryGetCausingEntity() = {result}");

            testReason = PlayerDeathReason.ByOther(0);
            result = testReason.TryGetCausingEntity(out _);
            args.Player.SendInfoMessage($"  ByOther(0).TryGetCausingEntity() = {result}");

            args.Player.SendSuccessMessage("[CustomDeathMessages] TryGetCausingEntity API 可用 ✓");
        }
        catch (Exception ex)
        {
            args.Player.SendErrorMessage($"[CustomDeathMessages] TryGetCausingEntity 异常: {ex.Message}");
        }
    }

    private void OnGameUpdate(EventArgs e) => _pending.Clear();

    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        var victim = TShock.Players[e.PlayerId];
        if (victim == null) return;

        // 死亡次数里程碑公告
        HandleDeathMilestone(victim.Name);

        var (msg, color) = Build(e.PlayerDeathReason, victim.Name, e.PlayerId);
        if (msg != null)
            _pending[e.PlayerId] = (msg, color);
    }

    /// <summary>处理死亡次数里程碑公告</summary>
    private void HandleDeathMilestone(string playerName)
    {
        if (_config.DeathMilestones.Count == 0)
            return;

        int newCount = IncrementDeathCount(playerName);

        if (_config.DeathMilestones.TryGetValue(newCount, out var milestoneMsg))
        {
            var msg = milestoneMsg
                .Replace("{Player}", playerName, StringComparison.OrdinalIgnoreCase)
                .Replace("{Count}", newCount.ToString(), StringComparison.OrdinalIgnoreCase);
            TSPlayer.All.SendMessage(msg, Color.LightBlue);
        }
    }

    private void OnBroadcast(ServerBroadcastEventArgs e)
    {
        var text = e.Message.ToString();
        foreach (var (id, pendingMsg) in _pending)
        {
            var player = TShock.Players[id];
            if (player != null && text.Contains(player.Name, StringComparison.Ordinal))
            {
                e.Message = NetworkText.FromLiteral(pendingMsg.Message);
                e.Color = pendingMsg.Color;
                _pending.TryRemove(id, out _);
                return;
            }
        }
    }

    private static readonly Color DefaultMessageColor = new(225, 100, 100);

    private static Color ParseColor(string rgb, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rgb))
                return fallback;

            var parts = rgb.Split(',');
            if (parts.Length == 3)
            {
                return new Color(
                    int.Parse(parts[0].Trim()),
                    int.Parse(parts[1].Trim()),
                    int.Parse(parts[2].Trim())
                );
            }
            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private (string? Message, Color Color) Build(PlayerDeathReason r, string playerName, int playerId = -1)
    {
        int plr = GetIntField(f_player, r);
        int npc = GetIntField(f_npc, r);
        int proj = GetIntField(f_proj, r);
        int other = GetIntField(f_other, r);
        int projType = GetIntField(f_projType, r);
        int itemType = GetIntField(f_itemType, r);
        string? customReason = GetStringField(f_customReason, r);

        // 构建基础占位符
        var ph = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{Player}"] = playerName,
            ["{Killer}"] = "???",
            ["{NPC}"] = "???",
            ["{Projectile}"] = "???",
            ["{Item}"] = "???",
            ["{CustomReason}"] = "",
            ["{Buff}"] = "",
        };

        string category;

        // 1. 自定义理由（其他插件通过 ByCustomReason 设置，优先级最高）
        if (!string.IsNullOrEmpty(customReason))
        {
            ph["{CustomReason}"] = customReason;
            category = "自定义";
        }
        // 2. PvP 击杀
        else if (plr >= 0 && plr < Main.maxPlayers)
        {
            ph["{Killer}"] = GetPlayerName(plr);
            ph["{Item}"] = GetItemName(itemType);

            if (proj >= 0)
            {
                ph["{Projectile}"] = GetProjectileName(projType);
                category = "PVP弹幕击杀";
            }
            else
            {
                category = "PVP击杀";
            }
        }
        // 3. NPC / Boss 击杀
        else if (npc >= 0 && npc < Main.maxNPCs)
        {
            var n = Main.npc[npc];
            ph["{NPC}"] = n.active ? n.GivenOrTypeName : "未知怪物";
            ph["{Killer}"] = ph["{NPC}"]; // NPC 也可用 {Killer} 占位符
            category = "NPC击杀";
        }
        // 4. 环境弹幕击杀（无玩家所属）
        else if (proj >= 0)
        {
            ph["{Projectile}"] = GetProjectileName(projType);
            category = "弹幕击杀";
        }
        // 5. 环境死亡（_sourceOtherIndex）
        else if (other >= 0)
        {
            category = other switch
            {
                0 => "摔死",
                1 => "溺水",
                2 => "岩浆",
                3 => "普通",
                4 or 255 => "击杀",
                5 => "石化",
                6 => "刺穿",
                7 => "窒息",
                8 => "烧死",
                9 => "中毒",
                10 => "触电",
                11 => "逃离血肉墙",
                12 => "被舔",
                >= 13 and <= 15 => "传送",
                16 => "炼狱之火",
                17 => "黑暗吞噬",
                18 => "饥饿",
                19 => "太空",
                20 => "挡刀",
                21 => "深渊",
                22 => "吸血鬼自燃",
                _ => "未知",
            };
        }
        // 6. 兜底：所有反射字段均为 -1 时，尝试通过公共 TryGetCausingEntity API 检测
        else
        {
            // 尝试公共 API 检测实体击杀
            if (r.TryGetCausingEntity(out var entity))
            {
                string fallbackCategory = entity switch
                {
                    Player => "PVP击杀",
                    NPC => "NPC击杀",
                    Projectile => "弹幕击杀",
                    _ => "未知",
                };
                category = fallbackCategory;

                if (entity != null)
                {
                    if (entity is Player p)
                    {
                        ph["{Killer}"] = p.name;
                    }
                    else if (entity is NPC nEnt)
                    {
                        ph["{NPC}"] = nEnt.GivenOrTypeName;
                        ph["{Killer}"] = ph["{NPC}"];
                    }
                   else if (entity is Projectile projEntity)
                    {
                        ph["{Projectile}"] = Lang.GetProjectileName(projEntity.type).Value;
                    }
                }
            }
            else
            {
                category = "未知";
            }
        }

        // Buff 细化检测：在环境死亡时检测玩家身上与死亡原因相关的具体 Debuff
        if (playerId >= 0)
        {
            var buffName = GetDeathBuffName(playerId, other);
            if (buffName != null)
                ph["{Buff}"] = buffName;
        }

        _config.Messages.TryGetValue(category, out var catConfig);
        var msg = FormatMessage(catConfig, ph);
        var color = ParseColor(catConfig?.Color ?? "", DefaultMessageColor);
        return (msg, color);
    }

    private static string? GetDeathBuffName(int playerId, int otherIndex)
    {
        var p = Main.player[playerId];
        if (p == null || !p.active)
            return null;

        // 遍历 buff 数组检测是否拥有指定 buff
        static bool HasBuff(Terraria.Player player, int buffId)
        {
            for (int i = 0; i < player.buffType.Length; i++)
            {
                if (player.buffTime[i] > 0 && player.buffType[i] == buffId)
                    return true;
            }
            return false;
        }

        static string? CheckBuffs(Terraria.Player player, int[] buffIds, string[] names)
        {
            for (int i = 0; i < buffIds.Length; i++)
            {
                if (HasBuff(player, buffIds[i]))
                    return names[i];
            }
            return null;
        }

        return otherIndex switch
        {
            // 烧死 — 检测各类火焰 debuff
            8 => CheckBuffs(p,
            [
                BuffID.OnFire, BuffID.CursedInferno, BuffID.Frostburn,
                BuffID.Burning, BuffID.OnFire3, BuffID.Frostburn2,
                BuffID.ShadowFlame, BuffID.Oiled,
            ],
            [
                "身上着火了", "被诅咒地狱火缠绕", "被冰焰灼烧",
                "被陨石灼伤", "被神圣之火吞噬", "被极寒冰焰吞噬",
                "被暗影焰缠身", "身上沾满了油",
            ]),

            // 中毒 — 检测中毒/毒液 debuff
            9 => CheckBuffs(p,
            [
                BuffID.Poisoned, BuffID.Venom,
            ],
            [
                "中毒了", "被毒液侵蚀",
            ]),

            // 触电
            10 => HasBuff(p, BuffID.Electrified) ? "触电了" : null,

            // 石化
            5 => HasBuff(p, BuffID.Stoned) ? "石化了" : null,

            // 窒息（沙/泥沙掩埋）
            7 => CheckBuffs(p,
            [
                BuffID.Suffocation, BuffID.WindPushed,
            ],
            [
                "被掩埋窒息", "被大风吹飞",
            ]),

            // 炼狱之火
            16 => CheckBuffs(p,
            [
                BuffID.Burning, BuffID.OnFire3,
            ],
            [
                "被陨石灼烧", "被炼狱火焰吞噬",
            ]),

            // 饥饿
            18 => HasBuff(p, BuffID.Starving) ? "饿晕了" : null,

            _ => null,
        };
    }

    private SqliteConnection CreateDatabase()
    {
        var dbPath = Path.Combine(TShock.SavePath, "tshock.sqlite");
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS DeathCounts (Name TEXT PRIMARY KEY, Count INTEGER NOT NULL DEFAULT 0)";
        cmd.ExecuteNonQuery();
        TShock.Log.ConsoleInfo("[CustomDeathMessages] 数据库已初始化: " + dbPath);
        return conn;
    }

    private int IncrementDeathCount(string playerName)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO DeathCounts (Name, Count) VALUES (@name, 1)
            ON CONFLICT(Name) DO UPDATE SET Count = Count + 1";
        cmd.Parameters.AddWithValue("@name", playerName);
        cmd.ExecuteNonQuery();
        using var selectCmd = _db.CreateCommand();
        selectCmd.CommandText = "SELECT Count FROM DeathCounts WHERE Name = @name";
        selectCmd.Parameters.AddWithValue("@name", playerName);
        return Convert.ToInt32(selectCmd.ExecuteScalar());
    }

    private void ResetDeathCount(string playerName)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM DeathCounts WHERE Name = @name";
        cmd.Parameters.AddWithValue("@name", playerName);
        cmd.ExecuteNonQuery();
    }

    private static string? FormatMessage(Configuration.DeathCategoryConfig? catConfig, Dictionary<string, string> placeholders)
    {
        if (catConfig == null || catConfig.Messages.Count == 0)
            return null;

        var template = catConfig.Messages[Random.Shared.Next(catConfig.Messages.Count)];

        foreach (var (key, value) in placeholders)
            template = template.Replace(key, value, StringComparison.OrdinalIgnoreCase);

        return template;
    }

    private static int GetIntField(FieldInfo? f, PlayerDeathReason obj)
        => f != null ? (int)(f.GetValue(obj) ?? -1) : -1;

    private static string? GetStringField(FieldInfo? f, PlayerDeathReason obj)
        => f?.GetValue(obj) as string;

    private static string GetPlayerName(int index)
    {
        if (index >= 0 && index < Main.player.Length)
        {
            var p = Main.player[index];
            if (p != null && p.active)
                return p.name;
        }
        var tsPlayer = TShock.Players[index];
        return tsPlayer?.Name ?? "未知玩家";
    }

    private static string GetItemName(int type)
    {
        if (type > 0 && type < ItemID.Count)
        {
            var name = Lang.GetItemNameValue(type);
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        return "未知武器";
    }

    private static string GetProjectileName(int type)
    {
        if (type > 0 && type < ProjectileID.Count)
        {
            var name = Lang.GetProjectileName(type).Value;
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        return "未知弹幕";
    }
}
