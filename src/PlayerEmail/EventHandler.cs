using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Streams;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace PlayerMail;

public static class EventHandler
{
    private static readonly ConcurrentDictionary<string, PendingBind> PendingBinds = new();

    public class PendingBind
    {
        public string Email = "";
        public string Code = "";
        public DateTime Expiry;
        public bool AwaitingCode = false;
    }

    public static void RegisterEvents(PlayerMailPlugin plugin)
    {
        ServerApi.Hooks.NetGetData.Register(plugin, OnGetData, int.MaxValue);
        GeneralHooks.ReloadEvent += OnReload;
        ServerApi.Hooks.ServerLeave.Register(plugin, OnServerLeave);
    }

    public static void UnregisterEvents(PlayerMailPlugin plugin)
    {
        ServerApi.Hooks.NetGetData.Deregister(plugin, OnGetData);
        GeneralHooks.ReloadEvent -= OnReload;
        ServerApi.Hooks.ServerLeave.Deregister(plugin, OnServerLeave);
    }

    private static void OnGetData(GetDataEventArgs args)
    {
        if (args.Handled) return;
        var type = args.MsgID;
        var player = TShock.Players[args.Msg.whoAmI];
        if (player == null) return;
        if (player.IsLoggedIn) return;
        if (player.RequiresPassword && type != PacketTypes.PasswordSend)
        {
            args.Handled = true;
            return;
        }
        if (type is PacketTypes.ContinueConnecting2 or PacketTypes.PasswordSend)
        {
            using var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length - 1);
            args.Handled = type == PacketTypes.ContinueConnecting2
                ? HandleConnecting(player)
                : HandlePassword(player, data.ReadString());
        }
    }

    private static readonly ConcurrentDictionary<string, DateTime> HintedPlayers = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    private const int MaxHinted = 50;
    private static readonly TimeSpan HintTimeout = TimeSpan.FromMinutes(1);

    private static bool HandleConnecting(TSPlayer player)
    {
        var plugin = PlayerMailPlugin.Instance;
        if (!plugin.Config.CharmeleonStyle) return false;
        var existing = plugin.DataStore.GetPlayerData(player.Name);
        if (existing != null) return false;
        if (!string.IsNullOrEmpty(Netplay.ServerPassword) || player.RequiresPassword) return false;

        // 第一次连接 → 用全屏断连提示告知玩家操作说明（Chameleon 式强制提示）
        if (!HintedPlayers.ContainsKey(player.Name) || (DateTime.Now - HintedPlayers[player.Name]).TotalMinutes >= 1)
        {
            if (HintedPlayers.Count >= MaxHinted) HintedPlayers.Clear();
            HintedPlayers[player.Name] = DateTime.Now;

            player.SilentKickInProgress = true;
            player.Disconnect(plugin.Config.DisconnectHint);

            return true;
        }

        // 1分钟内第二次连接 → 直接进入邮箱绑定流程
        player.SendInfoMessage("[PlayerMail] 请在下方密码框中输入你的邮箱地址。");
        PendingBinds[player.Name] = new PendingBind { AwaitingCode = false };
        NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
        return true;
    }

    private static bool HandlePassword(TSPlayer player, string input)
    {
        var plugin = PlayerMailPlugin.Instance;
        if (!PendingBinds.TryGetValue(player.Name, out var bind)) return false;
        if (!bind.AwaitingCode)
        {
            if (!plugin.Sender.IsValidEmail(input))
            {
                player.SendErrorMessage("[PlayerMail] 邮箱格式不正确，请重新输入。");
                NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                return true;
            }
            if (plugin.DataStore.GetAllPlayerData().Values.Any(d => d.Email.Equals(input, StringComparison.OrdinalIgnoreCase)))
            {
                player.SendErrorMessage("[PlayerMail] 该邮箱已被其他玩家绑定，请使用其他邮箱。");
                NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                return true;
            }
            var code = new Random().Next(1000, 9999).ToString();
            bind.Email = input;
            bind.Code = code;
            bind.Expiry = DateTime.Now.AddSeconds(plugin.Config.VerifyCodeExpireSeconds);
            bind.AwaitingCode = true;
            Task.Run(() =>
            {
                try { plugin.Sender.SendVerifyEmail(input, player.Name, code); }
                catch (Exception ex) { TShock.Log.Error("[PlayerMail] 验证码发送失败: " + ex.Message); }
            });
            player.SendInfoMessage("[PlayerMail] 验证码已发送至 " + input + "，请在密码框中输入验证码完成绑定。");
            NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
            return true;
        }
        else
        {
            if (DateTime.Now > bind.Expiry)
            {
                player.SendErrorMessage("[PlayerMail] 验证码已过期，请重新连接并重试。");
                PendingBinds.TryRemove(player.Name, out _);
                player.Disconnect("验证码已过期，请重新连接。");
                return true;
            }
            if (input != bind.Code)
            {
                player.SendErrorMessage("[PlayerMail] 验证码错误，请重新输入。");
                NetMessage.SendData((int)PacketTypes.PasswordRequired, player.Index);
                return true;
            }
            plugin.DataStore.SetPlayerData(player.Name, new PlayerMailData
            {
                Email = bind.Email,
                BindTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            TShock.Log.Info("[PlayerMail] 玩家 " + player.Name + " 通过连接验证绑定邮箱 " + bind.Email);
            player.SendSuccessMessage("[PlayerMail] 邮箱 " + bind.Email + " 绑定成功！");
            PendingBinds.TryRemove(player.Name, out _);
            return false;
        }
    }

    private static void OnServerLeave(LeaveEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player != null) PendingBinds.TryRemove(player.Name, out _);
    }

    private static void OnReload(ReloadEventArgs args)
    {
        ConfigLoader.Reload();
        args.Player?.SendSuccessMessage("[PlayerMail] 配置已重新加载");
    }
}
