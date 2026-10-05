using Terraria;
using TShockAPI;
using ZSEBot.Models;

namespace ZSEBot.Common;

/// <summary>
/// 世界更换 / 重置守卫：把 <see cref="Main.worldID"/> 与配置里持久化的「上次世界ID」比对，
/// 不一致即视为换了世界（AutoResetPlus 重置、手动换图、重新生成世界都会换 worldID），
/// 此时清零排行统计（zse_statistic + boss_kill 两张表）。
///
/// 为什么要清：排行卡（死亡 / 在线 / Boss 击杀）读的就是这两张表，语义上属于「本世界」的数据，
/// 世界重置后必须归零；此前只有游戏内手动 /zse reset 才会清。
///
/// 两个要点：
///   1. worldID 写进 tshock/starZSEbot.json 持久化 —— 重置后服务器重启也能判定；
///   2. 在线玩家的内存统计要换成新实例，否则他们下次落盘会把旧数据又写回去。
///
/// 邮件（Mail）故意**不**清：那是商店发货记录，重置不应吃掉未领取的物品。
/// 由 GamePostUpdate（有玩家时）与 Netplay.UpdateInMainThread（空服也在跑）双驱动。
/// </summary>
internal static class WorldResetGuard
{
    internal static void Tick()
    {
        try
        {
            var worldId = Main.worldID;
            if (worldId == 0)
            {
                return; // 世界尚未加载，无法判定
            }

            var cfg = Config.Settings;
            if (cfg.LastWorldId == worldId)
            {
                return;
            }

            var firstSeen = cfg.LastWorldId == 0;
            if (firstSeen)
            {
                // 升级到本版本后首次记录：只记 ID，不清理历史统计
                cfg.LastWorldId = worldId;
                cfg.Write();
                return;
            }

            // 先清理、成功后再落盘：若先落盘而清理抛异常，这个世界 ID 就再也判定不到 → 永久漏清
            ZSECharacterInfo.CleanAll();
            ResetActivePlayers();
            cfg.LastWorldId = worldId;
            cfg.Write();
            TShock.Log.ConsoleInfo("[starZSEbot]检测到世界已更换/重置，排行统计（死亡/在线/Boss击杀）已清零");
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]世界重置守卫异常: {ex}");
        }
    }

    /// <summary>在线玩家的统计换成全新实例（在线时长从 0 重新累计，避免旧数据回写）</summary>
    private static void ResetActivePlayers()
    {
        foreach (var player in TShock.Players.Where(x => x is { Active: true } && x.Account != null))
        {
            player.RemoveData(StarZSEBot.CharacterInfoKey);
            player.SetData(StarZSEBot.CharacterInfoKey,
                new ZSECharacterInfo { AccountName = player.Account.Name });
        }
    }
}
