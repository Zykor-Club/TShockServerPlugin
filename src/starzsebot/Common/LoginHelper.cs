using ZSEBot.Enums;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace ZSEBot.Common;

internal static class LoginHelper
{
    /// <summary>
    /// 白名单进服判定（只负责"请求 → 放行/踢出"）：
    /// 1) 在 MessageBuffer 层拦截 ClientUUID 仅为读取设备UUID并发起白名单请求，
    ///    【永不拦截/改动报文】，原包必定继续交给 TShock 原生处理，
    ///    客户端握手与"关闭白名单"完全一致，避免卡"发现服务器"。
    /// 2) 回包由 CheckWhitelist 处理：Accept 放行（走 TShock 原生进服/自动登录），其余踢出。
    /// 3) 不做插件级自动注册/登录；设备绑定/登录批准逻辑在机器人侧。
    /// </summary>
    internal static void On_MessageBufferOnGetData(On.Terraria.MessageBuffer.orig_GetData orig, MessageBuffer self,
        int start, int length, out int messageType)
    {
        if (Config.Settings.WhiteList)
        {
            try
            {
                // 仅读取 ClientUUID（不改包、不位移影响原始解析），原包必走 orig
                var reader = new BinaryReader(self.readerStream);
                reader.BaseStream.Position = start;
                if (reader.ReadByte() == (byte) PacketTypes.ClientUUID)
                {
                    var uuid = reader.ReadString();
                    var player = TShock.Players[self.whoAmI];
                    if (player is { ConnectionAlive: true } && !string.IsNullOrEmpty(player.Name)
                        && WebsocketManager.IsWebsocketConnected)
                    {
                        var body = (
                            name: player.Name,
                            ip: player.IP,
                            uuid: uuid
                        );
                        // 绝不在游戏线程上触碰 WebSocket（并发 Send 会把主循环卡死），
                        // 改由后台线程发送，失败静默
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                new PackageWriter(PackageType.Whitelist, false, null)
                                    .Write("player_name", body.name)
                                    .Write("player_ip", body.ip)
                                    .Write("player_uuid", body.uuid)
                                    .Send();
                            }
                            catch (Exception ex)
                            {
                                TShock.Log.ConsoleError("[starZSEbot]后台发送白名单请求失败: " + ex);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[starZSEbot]拦截ClientUUID读取时出错: " + ex);
            }
        }

        // 无论白名单开/关、无论拦截成功与否，原始报文必须继续按原路径处理
        orig(self, start, length, out messageType);
    }

    internal static bool CheckWhitelist(TSPlayer player, WhiteListResult result)
    {
        var groupId = Config.Settings.GroupNumber.ToString();
        if (Config.Settings.GroupNumber == 0)
        {
            groupId = "";
        }

        if (string.IsNullOrEmpty(player.Name))
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})版本可能过低...");
            player.Disconnect("你的游戏版本可能过低,\n" +
                              "请使用Terraria1.4.4+游玩");
            return false;
        }

        try
        {
            switch (result)
            {
                case WhiteListResult.Accept:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})已通过白名单验证...");
                    break;
                }
                case WhiteListResult.NotInWhitelist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})没有添加白名单...");
                    player.SilentKickInProgress = true;
                    player.Disconnect($"[starZSEbot]没有添加白名单!\n" +
                                      $"请在群{groupId}内发送\"/添加白名单 角色名字'\"");
                    return false;
                }
                case WhiteListResult.Frozen:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})白名单已被冻结(已退群)...");
                    player.Disconnect($"[starZSEbot]你的白名单已被冻结!\n" +
                                      $"请重新加入群{groupId}即可自动解冻");
                    return false;
                }
                case WhiteListResult.InGroupBlacklist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})被屏蔽，处于群黑名单中...");
                    player.Disconnect("[starZSEbot]你已被服务器屏蔽\n" +
                                      "你处于本群黑名单中!");
                    return false;
                }
                case WhiteListResult.InBotBlacklist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})被屏蔽，处于全局黑名单中...");
                    player.Disconnect("[starZSEbot]你已被Bot屏蔽\n" +
                                      "你处于全局黑名单中!");
                    return false;
                }
                case WhiteListResult.NeedLogin:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})使用未授权的设备...");
                    player.Disconnect($"[starZSEbot]未授权设备!\n" +
                                      $"在群{groupId}内发送\"/登录\"\n" +
                                      $"以批准此设备登录");

                    return false;
                }
                case WhiteListResult.Unknown:
                default:
                {
                    TShock.Log.ConsoleInfo(
                        $"[starZSEbot]玩家[{player.Name}](IP: {player.IP})无效登录结果[{result}], 可能是适配插件版本过低...");
                    player.Disconnect($"[starZSEbot]登录出错!" +
                                      $"无法处理登录结果: {result}");

                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})验证白名单时出现错误...\n" +
                                   $"{ex}");
            player.SilentKickInProgress = true;
            player.Disconnect($"[starZSEbot]服务器发生错误无法处理该请求!\n" +
                              $"请尝试重新加入游戏或者联系服务器群{groupId}管理员");
            return false;
        }

        return true;
    }
}