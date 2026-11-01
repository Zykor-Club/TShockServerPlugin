using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.GameContent.Events;
using TShockAPI;
using static FakePlayer.Plugin;

namespace FakePlayer;

internal class Utils
{
    #if DEBUG
    public static void Log(string text)
    {
        TSPlayer.All.SendMessage(TextGrad(text),color);
        TShock.Log.ConsoleInfo("{0}", text);  // 使用占位符避免解析
    }
    #endif

    #region 根据真实玩家发送消息
    public static void SendMess(TSPlayer plr, string help)
    {
        if (plr.RealPlayer)
            plr.SendMessage(TextGrad(help), color);
        else
            plr.SendMessage(help, color);
    }
    #endregion

    #region 单色与随机色
    public static Color color => new(240, 250, 150); // 奶黄色
    public static Color c2 => new(Main.rand.Next(180, 250), // 单行随机色
                              Main.rand.Next(180, 250),
                              Main.rand.Next(180, 250));
    #endregion

    #region 渐变色方法
    public static string TextGrad(string text, TSPlayer? plr = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 检查是否已包含颜色标签
        if (text.Contains("[c/") || text.Contains("[i:") || text.Contains("[i/s"))
        {
            // 如果有颜色标签，保留它们并处理其他部分
            return MixedText(text);
        }
        else
        {
            // 如果没有颜色标签，直接应用渐变
            return Grad(text);
        }
    }
    #endregion

    #region 混合文本
    // 匹配颜色标签 [color/颜色:文本] 或 物品图标标签 [i:物品ID] 或 [i/s数量:物品ID]
    private static readonly Regex regex = new Regex(@"(\[c/([0-9a-fA-F]+):([^\]]+)\]|\[i(?:/s\d+)?:\d+\])");
    private static string MixedText(string text)
    {
        var res = new StringBuilder();
        var mats = regex.Matches(text);
        if (mats.Count == 0) return Grad(text);

        int idx = 0;
        foreach (Match m in mats.Cast<Match>())
        {
            // 添加标签前的普通文本（应用渐变）
            if (m.Index > idx) res.Append(Grad(text.Substring(idx, m.Index - idx)));

            // 添加标签本身
            res.Append(m.Value);
            idx = m.Index + m.Length;
        }

        // 添加最后一个标签后的普通文本
        if (idx < text.Length) res.Append(Grad(text.Substring(idx)));

        return res.ToString();
    }
    #endregion

    #region 返回物品图标方法
    // 根据物品ID返回物品图标
    public static string ItemIcon(int itemID) => $"[i:{itemID}]";
    // 返回带数量的物品图标
    public static string ItemIcon(int itemID, int stack = 1) => $"[i/s{stack}:{itemID}]";
    #endregion

    #region 文本渐变方法
    private static string Grad(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var res = new StringBuilder();
        var start = new Microsoft.Xna.Framework.Color(165, 210, 235);
        var end = new Microsoft.Xna.Framework.Color(245, 250, 175);

        // 计算有效字符数（排除换行符）
        int cnt = 0;

        foreach (char c in text)
            if (c != '\n' && c != '\r') cnt++;

        // 如果没有有效字符，直接返回
        if (cnt == 0) return text;

        int idx = 0;

        foreach (char c in text)
        {
            if (c == '\n' || c == '\r')
            {
                res.Append(c);
                continue;
            }

            // 计算渐变比例
            float ratio = (float)idx / (cnt - 1);
            var clr = Microsoft.Xna.Framework.Color.Lerp(start, end, ratio);

            // 添加到结果
            res.Append($"[c/{clr.Hex3()}:{c}]");
            idx++;
        }

        return res.ToString();
    }
    #endregion

    #region 获取破坏图格的物品属性
    public static Item GetItem(int x, int y)
    {
        var noPrefix = false;
        WorldGen.KillTile_GetItemDrops(x, y, Main.tile[x, y], out int type, out int stack, out _, out _, out noPrefix);

        Item item = ContentSamples.ItemsByType[type];
        item.stack = stack;
        return item;
    }
    #endregion

    #region 获取物品的弹幕与图格属性信息
    public static void ItemInfo(Player plr, Item item)
    {
        if (item == null || item.IsAir) return;

        var proj = 0;
        var speed = 0f;
        var canShoot = false;
        var damage = 0;
        var knockBack = 0f;
        var useItemID = 0;
        var tileType = item.createTile > 0 ? item.createTile : item.createWall > 0 ? item.createWall : -1;
        plr.PickAmmo(item, ref proj, ref speed, ref canShoot, ref damage, ref knockBack, out useItemID);

        var speedInfo = speed > 0 ? $"速度:{speed}" : string.Empty;
        var tileInfo = tileType > -1 ? $"图格ID:{tileType}" : string.Empty;
        var kbInfo = knockBack > 0 ? $"击退:{knockBack}" : string.Empty;
        SendMess(TShock.Players[plr.whoAmI], $"物品:{ItemIcon(item.type, item.stack)}({item.type}) " +
                                             $"弹幕:{Lang.GetProjectileName(proj).Value}({proj}) " +
                                             $"{tileInfo} {kbInfo} {speedInfo}");
    }
    #endregion


    #region 进度条件
    // 检查条件组中的所有条件是否都满足
    public static bool CheckConds(List<string> conds, Player? p = null)
    {
        foreach (var c in conds)
        {
            if (!CheckCond(c, p))
                return false;
        }
        return true;
    }

    // 检查单个条件是否满足 - 直接匹配中文
    public static bool CheckCond(string cond, Player? p = null)
    {
        switch (cond)
        {
            case "0":
            case "无":
                return true;
            case "1":
            case "克眼":
            case "克苏鲁之眼":
                return NPC.downedBoss1;
            case "2":
            case "史莱姆王":
            case "史王":
                return NPC.downedSlimeKing;
            case "3":
            case "世吞":
            case "黑长直":
            case "世界吞噬者":
            case "世界吞噬怪":
                return NPC.downedBoss2 &&
                       (IsDefeated(NPCID.EaterofWorldsHead) ||
                        IsDefeated(NPCID.EaterofWorldsBody) ||
                        IsDefeated(NPCID.EaterofWorldsTail));
            case "4":
            case "克脑":
            case "脑子":
            case "克苏鲁之脑":
                return NPC.downedBoss2 && IsDefeated(NPCID.BrainofCthulhu);
            case "5":
            case "邪恶boss2":
            case "世吞或克脑":
            case "击败世吞克脑任意一个":
                return NPC.downedBoss2;
            case "6":
            case "巨鹿":
            case "鹿角怪":
                return NPC.downedDeerclops;
            case "7":
            case "蜂王":
                return NPC.downedQueenBee;
            case "8":
            case "骷髅王前":
                return !NPC.downedBoss3;
            case "9":
            case "吴克":
            case "骷髅王":
            case "骷髅王后":
                return NPC.downedBoss3;
            case "10":
            case "肉前":
                return !Main.hardMode;
            case "11":
            case "困难模式":
            case "肉山":
            case "肉后":
            case "血肉墙":
                return Main.hardMode;
            case "12":
            case "毁灭者":
            case "铁长直":
                return NPC.downedMechBoss1;
            case "13":
            case "双子眼":
            case "双子魔眼":
                return NPC.downedMechBoss2;
            case "14":
            case "铁吴克":
            case "机械吴克":
            case "机械骷髅王":
                return NPC.downedMechBoss3;
            case "15":
            case "世纪之花":
            case "花后":
            case "世花":
                return NPC.downedPlantBoss;
            case "16":
            case "石后":
            case "石巨人":
                return NPC.downedGolemBoss;
            case "17":
            case "史后":
            case "史莱姆皇后":
                return NPC.downedQueenSlime;
            case "18":
            case "光之女皇":
            case "光女":
                return NPC.downedEmpressOfLight;
            case "19":
            case "猪鲨":
            case "猪龙鱼公爵":
                return NPC.downedFishron;
            case "20":
            case "拜月":
            case "拜月教":
            case "教徒":
            case "拜月教邪教徒":
                return NPC.downedAncientCultist;
            case "21":
            case "月总":
            case "月亮领主":
                return NPC.downedMoonlord;
            case "22":
            case "哀木":
                return NPC.downedHalloweenTree;
            case "23":
            case "南瓜王":
                return NPC.downedHalloweenKing;
            case "24":
            case "常绿尖叫怪":
                return NPC.downedChristmasTree;
            case "25":
            case "冰雪女王":
                return NPC.downedChristmasIceQueen;
            case "26":
            case "圣诞坦克":
                return NPC.downedChristmasSantank;
            case "27":
            case "火星飞碟":
                return NPC.downedMartians;
            case "28":
            case "小丑":
                return NPC.downedClown;
            case "29":
            case "日耀柱":
                return NPC.downedTowerSolar;
            case "30":
            case "星旋柱":
                return NPC.downedTowerVortex;
            case "31":
            case "星云柱":
                return NPC.downedTowerNebula;
            case "32":
            case "星尘柱":
                return NPC.downedTowerStardust;
            case "33":
            case "一王后":
            case "任意机械boss":
                return NPC.downedMechBossAny;
            case "34":
            case "三王后":
                return NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3;
            case "35":
            case "一柱后":
                return NPC.downedTowerNebula || NPC.downedTowerSolar || NPC.downedTowerStardust || NPC.downedTowerVortex;
            case "36":
            case "四柱后":
                return NPC.downedTowerNebula && NPC.downedTowerSolar && NPC.downedTowerStardust && NPC.downedTowerVortex;
            case "37":
            case "哥布林入侵":
                return NPC.downedGoblins;
            case "38":
            case "海盗入侵":
                return NPC.downedPirates;
            case "39":
            case "霜月":
                return NPC.downedFrost;
            case "40":
            case "血月":
                return Main.bloodMoon;
            case "41":
            case "雨天":
                return Main.raining;
            case "42":
            case "白天":
                return Main.dayTime;
            case "43":
            case "晚上":
                return !Main.dayTime;
            case "44":
            case "大风天":
                return Main.IsItAHappyWindyDay;
            case "45":
            case "万圣节":
                return Main.halloween;
            case "46":
            case "圣诞节":
                return Main.xMas;
            case "47":
            case "派对":
                return BirthdayParty.PartyIsUp;
            case "48":
            case "旧日一":
            case "黑暗法师":
            case "撒旦一":
                return DD2Event._downedDarkMageT1;
            case "49":
            case "旧日二":
            case "巨魔":
            case "食人魔":
            case "撒旦二":
                return DD2Event._downedOgreT2;
            case "50":
            case "旧日三":
            case "贝蒂斯":
            case "双足翼龙":
            case "撒旦三":
                return DD2Event._spawnedBetsyT3;
            case "51":
            case "2020":
            case "醉酒":
            case "醉酒种子":
            case "醉酒世界":
                return Main.drunkWorld;
            case "52":
            case "2021":
            case "十周年":
            case "十周年种子":
                return Main.tenthAnniversaryWorld;
            case "53":
            case "ftw":
            case "真实世界":
            case "真实世界种子":
                return Main.getGoodWorld;
            case "54":
            case "ntb":
            case "蜜蜂世界":
            case "蜜蜂世界种子":
                return Main.notTheBeesWorld;
            case "55":
            case "dst":
            case "饥荒":
            case "永恒领域":
                return Main.dontStarveWorld;
            case "56":
            case "remix":
            case "颠倒":
            case "颠倒世界":
            case "颠倒种子":
                return Main.remixWorld;
            case "57":
            case "noTrap":
            case "陷阱种子":
            case "陷阱世界":
                return Main.noTrapsWorld;
            case "58":
            case "天顶":
            case "天顶种子":
            case "缝合种子":
            case "天顶世界":
            case "缝合世界":
                return Main.zenithWorld;
            case "59":
            case "森林":
                if (p != null)
                    return p.ShoppingZone_Forest;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:森林");
                    return false;
                }
            case "60":
            case "丛林":
                if (p != null)
                    return p.ZoneJungle;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:丛林");
                    return false;
                }
            case "61":
            case "沙漠":
                if (p != null)
                    return p.ZoneDesert;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:沙漠");
                    return false;
                }
            case "62":
            case "雪原":
                if (p != null)
                    return p.ZoneSnow;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:雪原");
                    return false;
                }
            case "63":
            case "洞穴":
                if (p != null)
                    return p.ZoneRockLayerHeight;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:洞穴");
                    return false;
                }
            case "64":
            case "海洋":
                if (p != null)
                    return p.ZoneBeach;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:海洋");
                    return false;
                }
            case "65":
            case "地表":
                if (p != null)
                    return (p.position.Y / 16) <= Main.worldSurface;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地表");
                    return false;
                }
            case "66":
            case "太空":
                if (p != null)
                    return (p.position.Y / 16) <= (Main.worldSurface * 0.35);
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:太空");
                    return false;
                }
            case "67":
            case "地狱":
                if (p != null)
                    return (p.position.Y / 16) >= Main.UnderworldLayer;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地狱");
                    return false;
                }
            case "68":
            case "神圣":
                if (p != null)
                    return p.ZoneHallow;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:神圣");
                    return false;
                }
            case "69":
            case "蘑菇":
                if (p != null)
                    return p.ZoneGlowshroom;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:蘑菇地");
                    return false;
                }
            case "70":
            case "腐化":
            case "腐化地":
            case "腐化环境":
                if (p != null)
                    return p.ZoneCorrupt;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:腐化");
                    return false;
                }
            case "71":
            case "猩红":
            case "猩红地":
            case "猩红环境":
                if (p != null)
                    return p.ZoneCrimson;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:猩红");
                    return false;
                }
            case "72":
            case "邪恶":
            case "邪恶环境":
                if (p != null)
                    return p.ZoneCrimson || p.ZoneCorrupt;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:邪恶");
                    return false;
                }
            case "73":
            case "地牢":
                if (p != null)
                    return p.ZoneDungeon;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地牢");
                    return false;
                }
            case "74":
            case "墓地":
                if (p != null)
                    return p.ZoneGraveyard;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:墓地");
                    return false;
                }
            case "75":
            case "蜂巢":
                if (p != null)
                    return p.ZoneHive;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:蜂巢");
                    return false;
                }
            case "76":
            case "神庙":
                if (p != null)
                    return p.ZoneLihzhardTemple;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:神庙");
                    return false;
                }
            case "77":
            case "沙尘暴":
                if (p != null)
                    return p.ZoneSandstorm;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:沙尘暴");
                    return false;
                }
            case "78":
            case "天空":
                if (p != null)
                    return p.ZoneSkyHeight;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:天空");
                    return false;
                }
            case "79":
            case "微光":
            case "以太":
                if (p != null)
                    return p.ZoneShimmer;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:微光");
                    return false;
                }
            case "80":
            case "满月":
                return Main.moonPhase == 0;
            case "81":
            case "亏凸月":
                return Main.moonPhase == 1;
            case "82":
            case "下弦月":
                return Main.moonPhase == 2;
            case "83":
            case "残月":
                return Main.moonPhase == 3;
            case "84":
            case "新月":
                return Main.moonPhase == 4;
            case "85":
            case "娥眉月":
                return Main.moonPhase == 5;
            case "86":
            case "上弦月":
                return Main.moonPhase == 6;
            case "87":
            case "盈凸月":
                return Main.moonPhase == 7;
            default:
                TShock.Log.ConsoleInfo($"[{PluginName}] 未知条件: {cond}");
                return false;
        }
    }

    // 是否解锁怪物图鉴以达到解锁物品掉落的程度（用于独立判断克脑、世吞）
    private static bool IsDefeated(int type)
    {
        var unlockState = Main.BestiaryDB.FindEntryByNPCID(type).UIInfoProvider.GetEntryUICollectionInfo().UnlockState;
        return unlockState == Terraria.GameContent.Bestiary.BestiaryEntryUnlockState.CanShowDropsWithDropRates_4;
    }
    #endregion

}
