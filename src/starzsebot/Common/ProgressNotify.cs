using Terraria;
using Terraria.ID;
using TShockAPI;
using ZSEBot.Enums;

namespace ZSEBot.Common;

/// <summary>
/// Boss 首杀检测与播报（懒初始化 + 延迟聚合，不依赖世界加载 hook）：
///  - 每个 Tick 读取 Utils.GetProcessList() 得到当前「已击败」集合 defeated；
///  - 世界加载完成后的首个 Tick：announced := defeated（插件中途启动/重载时不补发历史击杀）；
///  - 世界变更检测：defeated 相比上一 Tick 收缩（唯一正常路径是世界重置 → 新世界进度清零）
///    → announced := defeated 并清空 pending，后续首杀重新播报；
///  - OnNpcKilled 只记录 pending（boss key → 击杀玩家并集 + 时间），下一 Tick 聚合判定；
///  - defeated 中「新增且未 announced」的 18 boss → 推送 progress_notify 包；
///    订阅过滤（哪些群要播报）由机器人侧完成，插件只负责「本世界首杀」语义。
/// </summary>
internal static class ProgressNotify
{
    /// <summary>NPC.type → 进度 key（与 Utils.GetProcessList() 的 18 boss 键一致；多段 boss 的组成部位冗余覆盖）</summary>
    private static readonly Dictionary<int, string> NpcKeyMap = new()
    {
        [NPCID.KingSlime] = "King Slime",
        [NPCID.EyeofCthulhu] = "Eye of Cthulhu",
        [NPCID.EaterofWorldsHead] = "Eater of Worlds",
        [NPCID.EaterofWorldsBody] = "Eater of Worlds",
        [NPCID.EaterofWorldsTail] = "Eater of Worlds",
        [NPCID.BrainofCthulhu] = "Brain of Cthulhu",
        [NPCID.QueenBee] = "Queen Bee",
        [NPCID.Deerclops] = "Deerclops",
        [NPCID.SkeletronHead] = "Skeletron",
        [NPCID.SkeletronHand] = "Skeletron",
        [NPCID.WallofFlesh] = "Wall of Flesh",
        [NPCID.WallofFleshEye] = "Wall of Flesh",
        [NPCID.QueenSlimeBoss] = "Queen Slime",
        [NPCID.TheDestroyer] = "The Destroyer",
        [NPCID.TheDestroyerBody] = "The Destroyer",
        [NPCID.TheDestroyerTail] = "The Destroyer",
        [NPCID.Retinazer] = "The Twins",
        [NPCID.Spazmatism] = "The Twins",
        [NPCID.SkeletronPrime] = "Skeletron Prime",
        [NPCID.Plantera] = "Plantera",
        [NPCID.Golem] = "Golem",
        [NPCID.GolemHead] = "Golem",
        [NPCID.GolemFistLeft] = "Golem",
        [NPCID.GolemFistRight] = "Golem",
        [NPCID.DukeFishron] = "Duke Fishron",
        [NPCID.HallowBoss] = "Empress of Light",
        [NPCID.CultistBoss] = "Lunatic Cultist",
        [NPCID.MoonLordCore] = "Moon Lord",
        [NPCID.MoonLordHead] = "Moon Lord",
        [NPCID.MoonLordHand] = "Moon Lord",
    };

    /// <summary>参与播报的 18 boss 键（入侵/事件类进度不参与，与机器人侧 /进度提醒 可选范围一致）</summary>
    private static readonly HashSet<string> NotifyKeys =
    [
        "King Slime", "Eye of Cthulhu", "Eater of Worlds", "Brain of Cthulhu", "Queen Bee",
        "Deerclops", "Skeletron", "Wall of Flesh", "Queen Slime", "The Destroyer", "The Twins",
        "Skeletron Prime", "Plantera", "Golem", "Duke Fishron", "Empress of Light",
        "Lunatic Cultist", "Moon Lord",
    ];

    private sealed class PendingKill
    {
        public readonly HashSet<string> Players = new ();
        public DateTime Time = DateTime.Now;
    }

    private static readonly Dictionary<string, PendingKill> Pending = new ();
    private static readonly object Gate = new ();
    private static HashSet<string>? _announced;

    /// <summary>OnNpcKilled 调用：记录一次 boss 击杀事件（玩家并集 + 时间），等待下一 Tick 聚合判定</summary>
    internal static void RecordKill(NPC npc)
    {
        if (!NpcKeyMap.TryGetValue(npc.type, out var key))
        {
            return;
        }

        var players = new List<string>();
        for (var i = 0; i < byte.MaxValue; i++)
        {
            if (!npc.playerInteraction[i])
            {
                continue;
            }

            var player = TShock.Players[i];
            if (player is { Active: true } && !string.IsNullOrEmpty(player.Name))
            {
                players.Add(player.Name);
            }
        }

        lock (Gate)
        {
            if (!Pending.TryGetValue(key, out var pending))
            {
                pending = new PendingKill();
                Pending[key] = pending;
            }

            pending.Time = DateTime.Now;
            foreach (var name in players)
            {
                pending.Players.Add(name);
            }
        }
    }

    /// <summary>OnGameUpdate 调用（约每 0.25 秒一次）：快照对比 + 首杀播报推送</summary>
    internal static void Tick()
    {
        // 世界未加载（服务器启动阶段）不做快照，避免把「载入前空进度」误判为世界变更
        if (Main.gameMenu || string.IsNullOrEmpty(Main.worldName))
        {
            return;
        }

        var defeated = new HashSet<string>();
        foreach (var (key, downed) in Utils.GetProcessList())
        {
            if (downed)
            {
                defeated.Add(key);
            }
        }

        List<(string Key, List<string> Players, string Time, string World)>? fires = null;
        lock (Gate)
        {
            if (_announced is null)
            {
                // 首次（插件启动/重载）：已有击杀一律视为已播报，不补发历史
                _announced = defeated;
                Pending.Clear();
                return;
            }

            if (defeated.Count < _announced.Count)
            {
                // 进度集合收缩：唯一正常路径是世界重置（新世界进度清零）→ 首杀播报重新武装
                _announced = defeated;
                Pending.Clear();
                TShock.Log.ConsoleInfo("[starZSEbot]检测到世界重置（进度清零），首杀播报已重新武装");
                return;
            }

            foreach (var key in defeated)
            {
                if (_announced.Contains(key) || !NotifyKeys.Contains(key))
                {
                    continue;
                }

                Pending.TryGetValue(key, out var pending);
                var players = pending is null ? new List<string>() : new List<string>(pending.Players);
                fires ??= new List<(string Key, List<string> Players, string Time, string World)>();
                fires.Add((key, players, (pending?.Time ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"), Main.worldName));
            }

            if (fires is not null)
            {
                _announced.UnionWith(defeated);
                Pending.Clear();
            }
        }

        if (fires is null)
        {
            return;
        }

        foreach (var (key, players, killTime, world) in fires)
        {
            TShock.Log.ConsoleInfo(
                $"[starZSEbot]首杀播报：{key}（玩家：{(players.Count == 0 ? "未知" : string.Join("、", players))}）");
            var writer = new PackageWriter(PackageType.ProgressNotify, false, null)
                .Write("boss_key", key)
                .Write("players", players)
                .Write("kill_time", killTime)
                .Write("world_name", world);
            // 首杀播报是低频关键包：不能因为正在发世界/地图/存档大包而等锁超时被静默丢弃
            writer.Critical = true;
            writer.Send();
        }
    }
}