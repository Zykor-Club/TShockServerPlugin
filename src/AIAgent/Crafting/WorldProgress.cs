using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Terraria;

namespace AIAgent.Crafting;

public sealed record WorldProgressSnapshot(
    string Difficulty,
    bool HardMode,
    string Stage,
    IReadOnlyList<string> Defeated,
    IReadOnlyList<string> OptionalMissing,
    IReadOnlyDictionary<string, bool> Pillars);

/// <summary>
/// 读取世界的 Boss 击败状态（NPC.downed* 静态标记），推断当前进度阶段。
/// 这些是游戏自己记的，不看玩家、不看物品，最可靠。
/// </summary>
public static class WorldProgress
{
    public static WorldProgressSnapshot Snapshot() => Snap(Advanced: false);

    public static WorldProgressSnapshot Advanced() => Snap(Advanced: true);

    private static WorldProgressSnapshot Snap(bool Advanced)
    {
        var defeated = new List<string>();
        var optionalMissing = new List<string>();

        void Mark(string name, bool downed, bool optional = false)
        {
            if (downed) defeated.Add(name);
            else if (optional) optionalMissing.Add(name);
        }

        Mark("史莱姆王", NPC.downedSlimeKing);
        Mark("克苏鲁之眼", NPC.downedBoss1);
        Mark("世界吞噬者 / 克苏鲁之脑", NPC.downedBoss2);
        Mark("骷髅王", NPC.downedBoss3);
        Mark("蜂王", NPC.downedQueenBee);
        Mark("独眼巨鹿", NPC.downedDeerclops, optional: true);
        Mark("血肉墙", Main.hardMode);
        Mark("史莱姆皇后", NPC.downedQueenSlime, optional: true);
        Mark("毁灭者", NPC.downedMechBoss1);
        Mark("双子魔眼", NPC.downedMechBoss2);
        Mark("机械骷髅王", NPC.downedMechBoss3);
        Mark("世纪之花", NPC.downedPlantBoss);
        Mark("石巨人", NPC.downedGolemBoss);
        Mark("猪龙鱼公爵", NPC.downedFishron, optional: true);
        Mark("光之女皇", NPC.downedEmpressOfLight, optional: true);
        Mark("拜月教邪教徒", NPC.downedAncientCultist);
        Mark("月亮领主", NPC.downedMoonlord);

        var pillars = new Dictionary<string, bool>
        {
            ["日耀"] = NPC.downedTowerSolar,
            ["星云"] = NPC.downedTowerNebula,
            ["星旋"] = NPC.downedTowerVortex,
            ["星尘"] = NPC.downedTowerStardust,
        };

        var stage = StageOf(pillars);
        var difficulty = Main.GameMode switch
        {
            1 => "专家",
            2 => "大师",
            3 => "旅程",
            _ => "经典",
        };

        return new WorldProgressSnapshot(
            difficulty,
            Main.hardMode,
            stage,
            defeated,
            optionalMissing,
            pillars);
    }

    private static string StageOf(IReadOnlyDictionary<string, bool> pillars)
    {
        if (NPC.downedMoonlord) return "已通关（月亮领主之后）";
        if (NPC.downedAncientCultist) return pillars.Values.All(v => v) ? "月亮领主" : "四柱（天界塔）";
        if (NPC.downedGolemBoss) return "拜月教邪教徒";
        if (NPC.downedPlantBoss) return "石巨人";
        if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3) return "世纪之花";
        if (!Main.hardMode) return "困难模式之前";
        return "机械三王";
    }

    private static string _cachedBrief = string.Empty;

    /// <summary>主线程定期刷新后的摘要，其他线程直接读这个字符串，不碰 Main/NPC。</summary>
    public static string CachedBrief => Volatile.Read(ref _cachedBrief);

    public static void RefreshCache()
    {
        try
        {
            Volatile.Write(ref _cachedBrief, Brief());
        }
        catch
        {
            // 世界还没准备好时保持上一次的值
        }
    }

    /// <summary>给提示词用的一行摘要，够短、不影响成本。只能在主线程调用。</summary>
    public static string Brief()
    {
        var snap = Snapshot();
        var optional = snap.OptionalMissing.Count > 0 ? "；可选未打：" + string.Join("、", snap.OptionalMissing) : string.Empty;
        return $"难度 {snap.Difficulty}，阶段：{snap.Stage}{optional}";
    }
}
