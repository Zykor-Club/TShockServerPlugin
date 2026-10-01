using ZSEBot.Enums;
using ZSEBot.Models;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using ZSEBotPlayer = ZSEBot.Models.ZSEBotPlayer;

namespace ZSEBot.Common;

internal static class ZSEBotApi
{
    internal static void HandleMessage(string receivedData)
    {
        var package = Package.Parse(receivedData);
        var packetWriter = new PackageWriter(package.Type, package.IsRequest, package.RequestId);
        try
        {
            switch (package.Type)
            {
                case PackageType.UnbindServer:
                    TShock.Log.ConsoleInfo("[starZSEbot]BOT发送解绑命令...");
                    var reason = package.Read<string>("reason");
                    TShock.Log.ConsoleInfo($"[starZSEbot]原因: {reason}");
                    Config.Settings.Token = string.Empty;
                    Config.Settings.Write();
                    StarZSEBot.GenBindCode(EventArgs.Empty);
                    WebsocketManager.WebSocket?.Dispose();
                    break;
                case PackageType.CallCommand:
                    var command = package.Read<string>("command");
                    // 兼容不带斜杠的指令：time 7:30 与 /time 7:30 均可
                    if (!command.TrimStart().StartsWith("/"))
                    {
                        command = "/" + command.TrimStart();
                    }

                    var userOpenId = package.Read<string>("user_open_id");
                    var groupOpenId = package.Read<string>("group_open_id");
                    ZSEBotPlayer tr = new ();
                    Commands.HandleCommand(tr, command);
                    TShock.Utils.SendLogs($"[starZSEbot] \"{userOpenId}\"来自群\"{groupOpenId}\"执行了: {command}", Color.PaleVioletRed);
                    packetWriter
                        .Write("output", tr.GetCommandOutput())
                        .Send();
                    break;
                case PackageType.Say:
                    var sayContent = package.Read<string>("content");
                    TShock.Log.ConsoleInfo($"[starZSEbot]BOT 远程广播: {sayContent}");
                    TShock.Utils.Broadcast(sayContent, Color.LimeGreen);
                    break;
                case PackageType.PlayerList:
                    packetWriter
                        .Write("server_name", string.IsNullOrEmpty(TShock.Config.Settings.ServerName) ? Main.worldName : TShock.Config.Settings.ServerName)
                        .Write("player_list", TShock.Players.Where(x => x is { Active: true }).Select(x => x.Name))
                        .Write("current_online", TShock.Utils.GetActivePlayerCount())
                        .Write("max_online", TShock.Config.Settings.MaxSlots)
                        .Write("process", Config.Settings.ShowProcessInPlayerList ? Utils.GetWorldProcess() : "")
                        .Send();
                    break;
                case PackageType.Progress:

                    var bossLock = new Dictionary<string, string>();


                    if (BossLockSupport.Support)
                    {
                        bossLock = BossLockSupport.GetLockBosses();
                    }

                    if (ProgressControlSupport.Support)
                    {
                        var progressControlBosses = ProgressControlSupport.GetLockBosses();
                        bossLock = bossLock.Count < progressControlBosses.Count ? progressControlBosses : bossLock;
                    }

                    packetWriter
                        .Write("is_text", false)
                        .Write("process", Utils.GetProcessList())
                        .Write("kill_counts", Utils.GetKillCountList())
                        .Write("boss_lock", bossLock)
                        .Write("world_name", Main.worldName)
                        .Write("drunk_world", Main.drunkWorld)
                        .Write("zenith_world", Main.zenithWorld)
                        .Write("world_icon", Utils.GetWorldIconName())
                        .Send();
                    break;
                case PackageType.Whitelist:
                    var name = package.Read<string>("player_name");
                    var whitelistResult = package.Read<WhiteListResult>("whitelist_result");

                    var player = TShock.Players.FirstOrDefault(x => x?.Name == name && x is { ConnectionAlive: true });
                    if (player == null)
                    {
                        TShock.Log.ConsoleInfo($"[starZSEbot]白名单回包 {name} -> {whitelistResult}, 玩家已不在线，忽略");
                        return;
                    }

                    // Accept 放行（CheckWhitelist 只记日志）；其余结果（未绑定/黑名单/未授权设备）踢出
                    LoginHelper.CheckWhitelist(player, whitelistResult);
                    break;
                case PackageType.SelfKick:
                    var selfKickName = package.Read<string>("name");
                    var kickPlr = TShock.Players.FirstOrDefault(x => x?.Name == selfKickName);
                    if (kickPlr == null)
                    {
                        return;
                    }

                    kickPlr.Kick("使用BOT自踢命令", true, saveSSI: true);
                    break;
                case PackageType.LookBag:
                    var lookBagName = package.Read<string>("player_name");

                    packetWriter
                        .Write("is_text", false)
                        .Write("name", lookBagName);

                    var lookPlr = TShock.Players.FirstOrDefault(x => x?.Name == lookBagName);
                    if (lookPlr != null)
                    {
                        var plr = lookPlr.TPlayer;
                        var lookOnlineResult = LookBag.LookOnline(plr);
                        packetWriter
                            .Write("exist", true)
                            .Write("life", $"{lookOnlineResult.Health}/{lookOnlineResult.MaxHealth}")
                            .Write("mana", $"{lookOnlineResult.Mana}/{lookOnlineResult.MaxMana}")
                            .Write("quests_completed", lookOnlineResult.QuestsCompleted)
                            .Write("inventory", lookOnlineResult.ItemList)
                            .Write("buffs", lookOnlineResult.Buffs)
                            .Write("enhances", lookOnlineResult.Enhances)
                            .Write("economic", EconomicData.GetEconomicData(lookOnlineResult.Name))
                            .Send();
                    }
                    else
                    {
                        // SSC 未启用：离线玩家没有角色数据可查，回包 ssc=false 让 BOT 精确提示"未启用 SSC"
                        if (!TShock.ServerSideCharacterConfig.Settings.Enabled)
                        {
                            packetWriter
                                .Write("exist", false)
                                .Write("ssc", false)
                                .Send();
                            return;
                        }

                        var acc = TShock.UserAccounts.GetUserAccountByName(lookBagName);
                        if (acc == null)
                        {
                            packetWriter
                                .Write("exist", 0)
                                .Write("ssc", true)
                                .Send();
                            return;
                        }

                        var data = TShock.CharacterDB.GetPlayerData(new TSPlayer(-1), acc.ID);
                        if (data == null)
                        {
                            packetWriter
                                .Write("exist", false)
                                .Write("ssc", true)
                                .Send();
                            return;
                        }

                        var lookOnlineResult = LookBag.LookOffline(acc, data);
                        packetWriter
                            .Write("exist", true)
                            .Write("life", $"{lookOnlineResult.Health}/{lookOnlineResult.MaxHealth}")
                            .Write("mana", $"{lookOnlineResult.Mana}/{lookOnlineResult.MaxMana}")
                            .Write("quests_completed", lookOnlineResult.QuestsCompleted)
                            .Write("inventory", lookOnlineResult.ItemList)
                            .Write("buffs", lookOnlineResult.Buffs)
                            .Write("enhances", lookOnlineResult.Enhances)
                            .Write("economic", EconomicData.GetEconomicData(lookOnlineResult.Name))
                            .Send();
                    }

                    break;
                case PackageType.MapImage:
                    var imageBytes = MapGeneratorSupport.CreatMapImgBytes();
                    packetWriter
                        .Write("base64", Utils.CompressBase64(Convert.ToBase64String(imageBytes)))
                        .Send();
                    

                    break;
                case PackageType.MapFile:
                    var mapFile = MapGeneratorSupport.CreateMapFile();
                    packetWriter
                        .Write("name", mapFile.Item2)
                        .Write("base64", Utils.CompressBase64(Convert.ToBase64String(mapFile.Item1)))
                        .Send();

                    break;
                case PackageType.WorldFile:
                    packetWriter
                        .Write("name", Path.GetFileName(Main.worldPathName))
                        .Write("base64", Utils.CompressBase64(Utils.FileToBase64String(Main.worldPathName)))
                        .Send();

                    break;
                case PackageType.PluginList:
                    var pluginList = ServerApi.Plugins.Select(p => new PluginInfo(p.Plugin.Name, p.Plugin.Description, p.Plugin.Author, p.Plugin.Version)).ToList();
                    packetWriter
                        .Write("is_mod", false)
                        .Write("plugins", pluginList)
                        .Send();
                    break;
                case PackageType.RankData:
                    var rankType = package.Read<string>("rank_type");
                    var arg = package.Read<string>("arg");

                    var rankTypeEnum = Rank.GetRankTypeByName(rankType);

                    switch (rankTypeEnum)
                    {
                        case RankTypes.Boss:
                            var bosses = Rank.GetBossByIdOrName(arg);
                            switch (bosses.Count)
                            {
                                case 0:
                                    packetWriter
                                        .Write("rank_type_support", true)
                                        .Write("need_arg", true)
                                        .Write("arg_support", false)
                                        .Write("message", "没有找到任何相关的BOSS呢~")
                                        .Write("support_args", Array.Empty<string>())
                                        .Send();
                                    break;
                                case > 1:
                                    packetWriter
                                        .Write("rank_type_support", true)
                                        .Write("need_arg", true)
                                        .Write("arg_support", false)
                                        .Write("message", "找到多个匹配的BOSS:\n")
                                        .Write("support_args", bosses.Select(x => $"{x.TypeName} ({x.type})"))
                                        .Send();
                                    break;
                                case 1:
                                    var boss = bosses.First();
                                    packetWriter
                                        .Write("rank_type_support", true)
                                        .Write("need_arg", true)
                                        .Write("arg_support", true)
                                        .Write("rank", Rank.GetBossRank(boss))
                                        .Send();
                                    break;
                            }

                            break;
                        case RankTypes.EconomicCoin:
                            if (string.IsNullOrEmpty(arg))
                            {
                                packetWriter
                                    .Write("rank_type_support", true)
                                    .Write("need_arg", true)
                                    .Write("arg_support", false)
                                    .Write("message", $"需要参数[货币], 以下是支持的货币:\n")
                                    .Write("support_args", EconomicSupport.SupportCoins)
                                    .Send();
                                return;
                            }

                            if (!EconomicSupport.SupportCoins.Contains(arg))
                            {
                                packetWriter
                                    .Write("rank_type_support", true)
                                    .Write("need_arg", true)
                                    .Write("arg_support", false)
                                    .Write("message", $"没有找到{arg}, 以下是支持的货币:\n")
                                    .Write("support_args", EconomicSupport.SupportCoins)
                                    .Send();
                                return;
                            }

                            packetWriter
                                .Write("rank_type_support", true)
                                .Write("need_arg", true)
                                .Write("arg_support", true)
                                .Write("rank", EconomicSupport.GetCoinRank(arg))
                                .Send();


                            break;
                        case RankTypes.Death:
                            packetWriter
                                .Write("rank_type_support", true)
                                .Write("need_arg", false)
                                .Write("rank", Rank.GetDeathRank())
                                .Send();
                            break;
                        case RankTypes.Online:
                            packetWriter
                                .Write("rank_type_support", true)
                                .Write("need_arg", false)
                                .Write("rank", Rank.GetOnlineRank())
                                .Send();
                            break;
                        case RankTypes.Fishing:
                            packetWriter
                                .Write("rank_type_support", true)
                                .Write("need_arg", false)
                                .Write("rank", Rank.GetFishingRank())
                                .Send();
                            break;
                        case RankTypes.Unknown:
                            packetWriter
                                .Write("rank_type_support", false)
                                .Write("support_rank_types", Rank.SupportRankTypes)
                                .Send();
                            break;
                    }

                    break;
                case PackageType.ShopCondition:
                    var itemConditions = package.Read<List<ProgressType>>("item_conditions");
                    packetWriter
                        .Write("unmet_conditions", ProgressHelper.CheckProgresses(itemConditions))
                        .Send();
                    break;
                case PackageType.ShopBuy:
                    var shopPlayerName = package.Read<string>("player_name");
                    var isCommand = package.Read<bool>("is_command");
                    var mail = new Mail { AccountName = shopPlayerName };
                    if (isCommand)
                    {
                        mail.IsCommand = true;
                        mail.Commands = package.Read<List<string>>("commands");
                    }
                    else
                    {
                        mail.IsCommand = false;
                        mail.Items = package.Read<List<MailItem>>("commands");
                    }

                    mail.CreatOrUpdate();
                    break;
                case PackageType.Hello:
                case PackageType.Heartbeat:
                case PackageType.Unknown:
                case PackageType.Error:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot] 处理BOT数据包时出错:\n" +
                                    $"{ex}\n" +
                                    $"源数据包: {receivedData}");

            packetWriter.Package.Type = PackageType.Error;
            packetWriter.Write("error", ex.ToString())
                .Send();
        }
    }
}