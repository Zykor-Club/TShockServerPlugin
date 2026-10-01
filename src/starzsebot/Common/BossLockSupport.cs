using System.Collections;
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

        var bossIdToName = new Dictionary<int, string>
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

        foreach (DictionaryEntry lockedBoss in lockedBosses)
        {
            if (lockedBoss.Key is not int bossId || lockedBoss.Value is not string lockReason)
            {
                continue;
            }

            if (!bossIdToName.TryGetValue(bossId, out var bossName))
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
}