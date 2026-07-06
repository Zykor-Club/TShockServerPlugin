using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TShockAPI;

namespace PlayerMail
{
    public static class CommandHandler
    {
        private static readonly Dictionary<string, DateTime> Cooldowns = new Dictionary<string, DateTime>();

        public static void RegisterCommands()
        {
            Commands.ChatCommands.Add(new Command("playermail.use", MailCmd, "ml")
            {
                HelpText = "邮件系统: /ml b <邮箱>, /ml v <验证码>, /ml s <玩家> <内容>, /ml ib [页码], /ml i, /ml u"
            });
            Commands.ChatCommands.Add(new Command("playermail.console", ConsoleSend, "mlc")
            {
                HelpText = "控制台邮件: /mlc <玩家> <内容>"
            });
        }

        private static void MailCmd(CommandArgs args)
        {
            var plr = args.Player;
            var plugin = PlayerMailPlugin.Instance;

            if (args.Parameters.Count == 0)
            {
                ShowHelp(plr);
                return;
            }

            var sub = args.Parameters[0].ToLower();
            switch (sub)
            {
                case "b": case "bind":
                    HandleBind(plr, args, plugin);
                    break;
                case "v": case "verify":
                    HandleVerify(plr, args, plugin);
                    break;
                case "u": case "unbind":
                    HandleUnbind(plr, args, plugin);
                    break;
                case "i": case "info":
                    HandleInfo(plr, plugin);
                    break;
                case "s": case "send":
                    HandleSend(plr, args, plugin);
                    break;
                case "bc": case "broadcast":
                    HandleBroadcast(plr, args, plugin);
                    break;
                case "ib": case "inbox":
                    HandleInbox(plr, args, plugin);
                    break;
                case "bl": case "blacklist":
                    HandleBlacklist(plr, args, plugin);
                    break;
                case "ca": case "clearall":
                    HandleClearAll(plr, plugin);
                    break;
                default:
                    plr.SendErrorMessage("未知子命令。使用 /ml 查看帮助。");
                    break;
            }
        }

        private static void ShowHelp(TSPlayer plr)
        {
            plr.SendInfoMessage("邮件系统帮助");
            plr.SendInfoMessage("/ml b <邮箱>       绑定邮箱，需验证码确认");
            plr.SendInfoMessage("/ml v <验证码>     输入验证码完成绑定");
            plr.SendInfoMessage("/ml s <玩家> <内容> 向玩家发送邮件(需双方绑定邮箱)");
            plr.SendInfoMessage("/ml ib [页码]      查看邮件收件箱(每页10封)");
            plr.SendInfoMessage("/ml i              查看绑定信息");
            plr.SendInfoMessage("/ml u              解绑邮箱");
            if (plr.HasPermission("playermail.admin"))
            {
                plr.SendInfoMessage("/ml bc <内容>      向所有绑定玩家群发邮件");
                plr.SendInfoMessage("/ml bl add <玩家>  将玩家加入黑名单");
                plr.SendInfoMessage("/ml bl rm <玩家>   将玩家移出黑名单");
                plr.SendInfoMessage("/ml bl list        查看黑名单列表");
                plr.SendInfoMessage("/ml ca             一键解绑所有玩家邮箱");
                plr.SendInfoMessage("/ml u <玩家>       强制解绑指定玩家邮箱");
                plr.SendInfoMessage("/mlc <玩家> <内容> 控制台直接发送邮件");
            }
        }

        private static void HandleBind(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (args.Parameters.Count < 2) { plr.SendErrorMessage("用法: /ml b <邮箱>"); return; }
            var mail = args.Parameters[1];

            if (plugin.BlacklistMgr.IsBlacklisted(plr.Name))
            { plr.SendErrorMessage("你已被拉黑，无法使用邮箱功能，请联系管理员"); return; }

            if (!plugin.Sender.IsValidEmail(mail)) { plr.SendErrorMessage("邮箱格式不正确"); return; }

            if (plugin.DataStore.GetPlayerData(plr.Name) != null)
            { plr.SendErrorMessage("你已绑定邮箱，请先解绑"); return; }

            var attempts = plugin.VerifyMgr.GetAttemptCount(plr.Name);
            if (attempts >= plugin.Config.最大验证码申请次数)
            {
                plugin.BlacklistMgr.Add(plr.Name);
                plr.SendErrorMessage($"验证码申请次数超过{plugin.Config.最大验证码申请次数}次，你已被拉黑");
                return;
            }

            var code = plugin.VerifyMgr.Generate(plr.Name, mail, plugin.Config.VerifyCodeExpireSeconds);
            plr.SendInfoMessage("正在发送验证码，请稍候...");

            Task.Run(() =>
            {
                try
                {
                    plugin.Sender.SendVerifyEmail(mail, plr.Name, code);
                    plr.SendSuccessMessage($"验证码已发送至 {mail}，请到邮箱查收验证码，并输入 /ml v 四位数验证码 完成绑定");
                    plr.SendInfoMessage($"验证码{plugin.Config.VerifyCodeExpireSeconds}秒内有效，剩余尝试次数: {plugin.Config.最大验证码申请次数 - attempts - 1}");
                }
                catch (Exception ex)
                {
                    plr.SendErrorMessage("验证码发送失败: " + ex.Message);
                    plugin.VerifyMgr.Clear(plr.Name);
                }
            });
        }

        private static void HandleVerify(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (args.Parameters.Count < 2) { plr.SendErrorMessage("用法: /ml v <验证码>"); return; }
            var inputCode = args.Parameters[1];

            if (plugin.BlacklistMgr.IsBlacklisted(plr.Name))
            { plr.SendErrorMessage("你已被拉黑，无法使用邮箱功能"); return; }

            if (!plugin.VerifyMgr.TryVerify(plr.Name, inputCode, out var mail))
            { plr.SendErrorMessage("验证码错误或已过期，请重新使用 /ml b <邮箱> 获取"); return; }

            plugin.DataStore.SetPlayerData(plr.Name, new PlayerMailData { Email = mail, BindTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            plr.SendSuccessMessage($"邮箱绑定成功: {mail}");

            // 如果开启了进服验证，绑定成功后解除限制
            if (plugin.Config.CharmeleonStyle)
            {
                
            }
        }

        private static void HandleUnbind(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (args.Parameters.Count > 1 && plr.HasPermission("playermail.admin"))
            {
                var target = args.Parameters[1];
                if (plugin.DataStore.RemovePlayerData(target))
                {
                    plr.SendSuccessMessage($"已强制解绑玩家 {target} 的邮箱");
                    // 如果开启强制验证，施加限制
                    if (plugin.Config.CharmeleonStyle)
                    {
                        var targetP = TShock.Players.FirstOrDefault(p => p?.Name == target && p.Active);
                        if (targetP != null)
                        {
                            targetP.SilentKickInProgress = true;
                            targetP.Disconnect("你的邮箱已被解绑，请重新绑定邮箱后再进服。");
                        }
                    }
                }
                else
                    plr.SendErrorMessage($"玩家 {target} 未绑定邮箱");
                return;
            }

            if (plugin.DataStore.RemovePlayerData(plr.Name))
            {
                plr.SendSuccessMessage("已解绑邮箱");
                // 如果开启强制验证，施加限制
                if (plugin.Config.CharmeleonStyle)
                {
                    plr.SilentKickInProgress = true;
                    plr.Disconnect("你的邮箱已被解绑，请重新绑定邮箱后再进服。");
                }
            }
            else
                plr.SendErrorMessage("你尚未绑定邮箱");
        }

        private static void HandleInfo(TSPlayer plr, PlayerMailPlugin plugin)
        {
            var data = plugin.DataStore.GetPlayerData(plr.Name);
            if (data != null)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(data.BindTime).LocalDateTime;
                plr.SendInfoMessage($"绑定邮箱: {data.Email}");
                plr.SendInfoMessage($"绑定时间: {dt:yyyy-MM-dd HH:mm:ss}");
            }
            else plr.SendErrorMessage("你尚未绑定邮箱");
        }

        private static void HandleSend(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (args.Parameters.Count < 3) { plr.SendErrorMessage("用法: /ml s <玩家名> <内容>"); return; }

            var target = args.Parameters[1];
            var content = string.Join(" ", args.Parameters.Skip(2));

            var fromData = plugin.DataStore.GetPlayerData(plr.Name);
            if (fromData == null)
            { plr.SendErrorMessage("你尚未绑定邮箱，请先使用 /ml b <邮箱>"); return; }

            var toData = plugin.DataStore.GetPlayerData(target);

            if (content.Length > plugin.Config.最大内容长度)
            { plr.SendErrorMessage($"内容过长，限制{plugin.Config.最大内容长度}字符"); return; }

            if (Cooldowns.TryGetValue(plr.Name, out var last) && (DateTime.Now - last).TotalSeconds < plugin.Config.发送冷却秒数)
            { plr.SendErrorMessage($"发送冷却中，请等待{plugin.Config.发送冷却秒数 - (int)(DateTime.Now - last).TotalSeconds}秒"); return; }

            Cooldowns[plr.Name] = DateTime.Now;
            plr.SendInfoMessage("正在发送邮件，请稍候...");

            var group = plr.Group.Name;
            var sendTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Task.Run(() =>
            {
                try
                {
                    plugin.Sender.SendPlayerEmail(toData.Email, target, plr.Name, group, fromData.Email, content, sendTime);

                    plugin.InboxMgr.Add(new InboxMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        FromPlayer = plr.Name,
                        ToPlayer = target,
                        Content = content,
                        SendTime = sendTime,
                        IsRead = false
                    });

                    plr.SendSuccessMessage($"邮件已成功发送至 {target}");
                }
                catch (Exception ex)
                {
                    plr.SendErrorMessage("邮件发送失败: " + ex.Message);
                }
            });
        }

        private static void HandleBroadcast(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (!plr.HasPermission("playermail.admin"))
            { plr.SendErrorMessage("你没有权限使用此命令"); return; }

            if (args.Parameters.Count < 2) { plr.SendErrorMessage("用法: /ml bc <内容>"); return; }
            var content = string.Join(" ", args.Parameters.Skip(1));

            if (content.Length > plugin.Config.最大内容长度)
            { plr.SendErrorMessage($"内容过长，限制{plugin.Config.最大内容长度}字符"); return; }

            var allPlayers = plugin.DataStore.GetAllPlayerData();
            if (allPlayers.Count == 0)
            { plr.SendErrorMessage("没有玩家绑定邮箱"); return; }

            plr.SendInfoMessage($"正在向 {allPlayers.Count} 位玩家发送广播邮件...");

            var group = plr.Group.Name;
            var sendTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var success = 0;
            var failed = 0;

            Task.Run(() =>
            {
                foreach (var kv in allPlayers)
                {
                    try
                    {
                        plugin.Sender.SendBroadcastEmail(kv.Value.Email, kv.Key, plr.Name, group, content, sendTime);

                        plugin.InboxMgr.Add(new InboxMessage
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            FromPlayer = plr.Name,
                            ToPlayer = kv.Key,
                            Content = content,
                            SendTime = sendTime,
                            IsRead = false
                        });
                        success++;
                    }
                    catch { failed++; }
                }
                plr.SendSuccessMessage($"广播完成: 成功{success}人，失败{failed}人");
            });
        }

        private static void HandleInbox(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            var myMessages = plugin.InboxMgr.GetForPlayer(plr.Name);
            if (myMessages.Count == 0)
            { plr.SendInfoMessage("你的收件箱为空"); return; }

            int page = 1;
            if (args.Parameters.Count > 1)
            {
                if (!int.TryParse(args.Parameters[1], out page) || page < 1)
                { plr.SendErrorMessage("无效的页码"); return; }
            }

            const int pageSize = 10;
            int totalPages = (myMessages.Count + pageSize - 1) / pageSize;
            if (page > totalPages) page = totalPages;

            int startIndex = (page - 1) * pageSize;
            var pageMessages = myMessages.Skip(startIndex).Take(pageSize).ToList();

            int unreadTotal = myMessages.Count(m => !m.IsRead);
            plr.SendInfoMessage($"收件箱 (共{myMessages.Count}封，未读{unreadTotal}封) 第{page}/{totalPages}页");
            plr.SendInfoMessage("使用 /ml ib <页码> 翻页，/ml ib <序号> 查看详情");

            for (int i = 0; i < pageMessages.Count; i++)
            {
                var m = pageMessages[i];
                var globalIndex = startIndex + i + 1;
                var status = m.IsRead ? "已读" : "未读";
                var preview = m.Content.Length > 20 ? m.Content.Substring(0, 20) + "..." : m.Content;
                plr.SendInfoMessage($"[{globalIndex:D2}] [{status}] {m.FromPlayer} {m.SendTime} {preview}");
            }
        }

        private static void HandleBlacklist(TSPlayer plr, CommandArgs args, PlayerMailPlugin plugin)
        {
            if (!plr.HasPermission("playermail.admin"))
            { plr.SendErrorMessage("你没有权限使用此命令"); return; }

            if (args.Parameters.Count < 2) { plr.SendErrorMessage("用法: /ml bl add/rm/list [玩家]"); return; }
            var action = args.Parameters[1].ToLower();

            switch (action)
            {
                case "add":
                    if (args.Parameters.Count < 3) { plr.SendErrorMessage("用法: /ml bl add <玩家>"); return; }
                    var targetAdd = args.Parameters[2];
                    if (plugin.BlacklistMgr.Add(targetAdd))
                        plr.SendSuccessMessage($"已将 {targetAdd} 加入黑名单");
                    else
                        plr.SendErrorMessage($"{targetAdd} 已在黑名单中");
                    break;
                case "rm": case "remove":
                    if (args.Parameters.Count < 3) { plr.SendErrorMessage("用法: /ml bl rm <玩家>"); return; }
                    var targetRm = args.Parameters[2];
                    if (plugin.BlacklistMgr.Remove(targetRm))
                        plr.SendSuccessMessage($"已将 {targetRm} 移出黑名单");
                    else
                        plr.SendErrorMessage($"{targetRm} 不在黑名单中");
                    break;
                case "list":
                    var list = plugin.BlacklistMgr.GetList();
                    if (list.Count == 0)
                    { plr.SendInfoMessage("黑名单为空"); return; }
                    plr.SendInfoMessage("黑名单列表");
                    foreach (var name in list)
                        plr.SendInfoMessage($"- {name}");
                    break;
                default:
                    plr.SendErrorMessage("用法: /ml bl add/rm/list [玩家]");
                    break;
            }
        }

        private static void HandleClearAll(TSPlayer plr, PlayerMailPlugin plugin)
        {
            if (!plr.HasPermission("playermail.admin"))
            { plr.SendErrorMessage("你没有权限使用此命令"); return; }

            plugin.DataStore.ClearAllPlayerData();
            plr.SendSuccessMessage("已一键解绑所有玩家邮箱");
        }

        private static void ConsoleSend(CommandArgs args)
        {
            var plr = args.Player;
            if (args.Parameters.Count < 2)
            {
                plr.SendErrorMessage("用法: /mlc <玩家名> <内容>");
                return;
            }

            var target = args.Parameters[0];
            var content = string.Join(" ", args.Parameters.Skip(1));
            var plugin = PlayerMailPlugin.Instance;

            var toData = plugin.DataStore.GetPlayerData(target);
            if (toData == null)
            { plr.SendErrorMessage($"玩家 {target} 尚未绑定邮箱"); return; }

            plr.SendInfoMessage("正在发送邮件，请稍候...");
            var sendTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Task.Run(() =>
            {
                try
                {
                    plugin.Sender.SendPlayerEmail(toData.Email, target, "服务器", "Console", plugin.Config.发件人邮箱, content, sendTime);
                    plr.SendSuccessMessage($"邮件已成功发送至 {target}");
                }
                catch (Exception ex)
                {
                    plr.SendErrorMessage("邮件发送失败: " + ex.Message);
                }
            });
        }
    }
}