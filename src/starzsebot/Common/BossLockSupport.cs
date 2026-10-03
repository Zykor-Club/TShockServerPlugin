using System.Collections;
using System.Globalization;
using System.Reflection;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace ZSEBot.Common;

/// <summary>
/// 通过反射桥接 BossLock 插件（不硬引用其程序集）。
/// 未安装 BossLock 时进度包里的锁定 BOSS 为空。
/// </summary>
public static class BossLockSupport
{
    public static bool Support { get; private set; }

    private static MethodInfo? _getAllLocked;
    private static MethodInfo? _loadBoss;

    private static readonly Dictionary<int, string> BossIdToName = new()
    {
        { NPCID.KingSlime, "King Slime" },
        { NPCID.EyeofCthulhu, "Eye of Cthulhu" },
        { NPCID.EaterofWorldsHead, "Eater of Worlds" },
        { NPCID.BrainofCthulhu, "Brain of Cthulhu" },
        { NPCID.QueenBee, "Queen Bee" },
        { NPCID.Deerclops, "Deerclops" },
        { NPCID.SkeletronHand, "Skeletron" },
        { NPCID.WallofFlesh, "Wall of Flesh" },
        { NPCID.QueenSlimeBoss, "Queen Slime" },
        { NPCID.Retinazer, "The Twins" },
        { NPCID.Spazmatism, "The Twins" },
        { NPCID.TheDestroyer, "The Destroyer" },
        { NPCID.SkeletronPrime, "Skeletron Prime" },
        { NPCID.Plantera, "Plantera" },
        { NPCID.Golem, "Golem" },
        { NPCID.DukeFishron, "Duke Fishron" },
        { NPCID.HallowBoss, "Empress of Light" },
        { NPCID.CultistBoss, "Lunatic Cultist" },
        { NPCID.MoonLordCore, "Moon Lord" }
    };

    public static void Init()
    {
        var pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "BossLock");
        if (pluginContainer is null)
        {
            return;
        }

        try
        {
            var assembly = pluginContainer.Plugin.GetType().Assembly;
            var databaseType = assembly.GetType("BossLock.Database");
            _getAllLocked = databaseType?.GetMethod("GetAllLocked", BindingFlags.Public | BindingFlags.Static);
            // 解锁提醒需要绝对解锁时间：LoadBoss(npcId) 返回数据库原始 "yyyy-MM-dd-HH:mm:ss"（老版本可能没有该方法）
            _loadBoss = databaseType?.GetMethod("LoadBoss", BindingFlags.Public | BindingFlags.Static);
            Support = _getAllLocked is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化BossLock反射失败:\n{ex}");
            Support = false;
        }

        if (!Support)
        {
            TShock.Log.ConsoleError("[starZSEbot]检测到BossLock插件但反射绑定失败，锁定BOSS信息不可用");
        }
    }

    public static Dictionary<string, string> GetLockBosses()
    {
        if (!Support)
        {
            throw new NotSupportedException("没有安装BossLock插件!");
        }

        var lockedBosses = _getAllLocked!.Invoke(null, null) as IDictionary; // 原始数据: Dictionary<int, string>
        var result = new Dictionary<string, string>();
        if (lockedBosses is null)
        {
            return result;
        }

        foreach (DictionaryEntry lockedBoss in lockedBosses)
        {
            if (lockedBoss.Key is not int bossId || lockedBoss.Value is not string lockReason)
            {
                continue;
            }

            if (!BossIdToName.TryGetValue(bossId, out var bossName))
            {
                continue;
            }

            if (bossId is NPCID.Retinazer or NPCID.Spazmatism)
            {
                if (!result.ContainsKey(bossName))
                {
                    result[bossName] = lockReason;
                }
            }
            else
            {
                result[bossName] = lockReason;
            }
        }

        return result;
    }

    /// <summary>
    /// 当前锁定 BOSS → 绝对解锁时间戳（Unix 秒）。
    /// GetAllLocked 只返回格式化显示文本，无法还原绝对时间；
    /// 这里用同程序集的 LoadBoss(npcId) 反射取数据库原始时间字符串再转换。
    /// 老版本 BossLock 没有 LoadBoss 时返回空表（仅影响解锁提醒功能）。
    /// </summary>
    public static Dictionary<string, long> GetLockBossesTs()
    {
        if (!Support)
        {
            throw new NotSupportedException("没有安装BossLock插件!");
        }

        var result = new Dictionary<string, long>();
        if (_loadBoss is null)
        {
            return result;
        }

        var lockedBosses = _getAllLocked!.Invoke(null, null) as IDictionary; // 已过滤为"当前锁定且未到期"
        if (lockedBosses is null)
        {
            return result;
        }

        foreach (DictionaryEntry lockedBoss in lockedBosses)
        {
            if (lockedBoss.Key is not int bossId)
            {
                continue;
            }

            if (!BossIdToName.TryGetValue(bossId, out var bossName))
            {
                continue;
            }

            string? rawTime;
            try
            {
                rawTime = _loadBoss.Invoke(null, new object[] { bossId }) as string;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[starZSEbot]读取BossLock解锁时间失败 npc={bossId}:\n{ex}");
                continue;
            }

            if (string.IsNullOrEmpty(rawTime) ||
                !DateTime.TryParseExact(rawTime, "yyyy-MM-dd-HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var unlockTime))
            {
                continue;
            }

            var ts = new DateTimeOffset(unlockTime).ToUnixTimeSeconds();
            if (bossId is NPCID.Retinazer or NPCID.Spazmatism)
            {
                if (!result.ContainsKey(bossName))
                {
                    result[bossName] = ts;
                }
            }
            else
            {
                result[bossName] = ts;
            }
        }

        return result;
    }
}