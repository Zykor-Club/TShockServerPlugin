using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace GroupBlacklistPlugin;

[ApiVersion(2, 1)]
public class GroupBlacklistPlugin : TerrariaPlugin
{
    public override string Name => "GroupBlacklist";
    public override string Author => "星梦";
    public override string Description => "禁止指定用户组进入服务器";
    public override Version Version => new(1, 1, 0);

    private static BlacklistConfig _config = null!;
    private DateTime _lastCheck = DateTime.MinValue;

    public GroupBlacklistPlugin(Main game) : base(game) { Order = 1; }

    public override void Initialize()
    {
        _config = BlacklistConfig.Load();

        PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        GeneralHooks.ReloadEvent += OnReload;

        Commands.ChatCommands.Add(new Command("groupblacklist.admin", GbCommand, "gb")
        {
            HelpText = "组黑名单管理，使用 /gb help 查看帮助"
        });

        TShock.Log.Info($"[组黑名单] 插件已加载 v{Version}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            GeneralHooks.ReloadEvent -= OnReload;
        }
        base.Dispose(disposing);
    }

    private void GbCommand(CommandArgs args)
    {
        var p = args.Parameters;
        var player = args.Player;

        if (p.Count == 0 || p[0].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp(player);
            return;
        }

        switch (p[0].ToLower())
        {
            case "on":
                _config.Settings.Enabled = true;
                _config.Save();
                player.SendSuccessMessage("[组黑名单] 已开启");
                if (_config.Settings.LogActions)
                    TShock.Log.Info($"[组黑名单] {player.Name} 开启了插件");
                break;

            case "off":
                _config.Settings.Enabled = false;
                _config.Save();
                player.SendSuccessMessage("[组黑名单] 已关闭");
                if (_config.Settings.LogActions)
                    TShock.Log.Info($"[组黑名单] {player.Name} 关闭了插件");
                break;

            case "add":
                if (p.Count < 2) { player.SendErrorMessage("用法: /gb add <组名>"); return; }
                AddGroup(player, p[1]);
                break;

            case "del":
                if (p.Count < 2) { player.SendErrorMessage("用法: /gb del <组名>"); return; }
                RemoveGroup(player, p[1]);
                break;

            case "padd":
                if (p.Count < 2) { player.SendErrorMessage("用法: /gb padd <玩家名>"); return; }
                AddExempt(player, p[1]);
                break;

            case "pdel":
                if (p.Count < 2) { player.SendErrorMessage("用法: /gb pdel <玩家名>"); return; }
                RemoveExempt(player, p[1]);
                break;

            case "list":
                ShowList(player);
                break;

            default:
                player.SendErrorMessage("未知子命令，使用 /gb help 查看帮助");
                break;
        }
    }

    private static void ShowHelp(TSPlayer player)
    {
        player.SendMessage(
            "[c/55CDFF:=== 组黑名单 ===]\n" +
            "[c/FFD700:/gb on/off] - 开启/关闭插件\n" +
            "[c/FFD700:/gb add <组名>] - 添加黑名单组\n" +
            "[c/FFD700:/gb del <组名>] - 移除黑名单组\n" +
            "[c/FFD700:/gb padd <玩家>] - 添加豁免玩家\n" +
            "[c/FFD700:/gb pdel <玩家>] - 移除豁免玩家\n" +
            "[c/FFD700:/gb list] - 查看当前列表\n" +
            "[c/FFD700:/reload] - 重载配置",
            Color.Cyan
        );
    }

    private void AddGroup(TSPlayer player, string groupName)
    {
        if (_config.BlacklistedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
        {
            player.SendErrorMessage($"组 {groupName} 已在黑名单中");
            return;
        }
        _config.BlacklistedGroups.Add(groupName);
        _config.Save();
        player.SendSuccessMessage($"已将组 {groupName} 加入黑名单");
        if (_config.Settings.LogActions)
            TShock.Log.Info($"[组黑名单] {player.Name} 添加黑名单组: {groupName}");
    }

    private void RemoveGroup(TSPlayer player, string groupName)
    {
        var removed = _config.BlacklistedGroups.RemoveAll(
            g => g.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            player.SendErrorMessage($"组 {groupName} 不在黑名单中");
            return;
        }
        _config.Save();
        player.SendSuccessMessage($"已将组 {groupName} 从黑名单移除");
        if (_config.Settings.LogActions)
            TShock.Log.Info($"[组黑名单] {player.Name} 移除黑名单组: {groupName}");
    }

    private void AddExempt(TSPlayer player, string playerName)
    {
        if (_config.ExemptPlayers.Contains(playerName, StringComparer.OrdinalIgnoreCase))
        {
            player.SendErrorMessage($"玩家 {playerName} 已在豁免名单中");
            return;
        }
        _config.ExemptPlayers.Add(playerName);
        _config.Save();
        player.SendSuccessMessage($"已将玩家 {playerName} 加入豁免名单");
        if (_config.Settings.LogActions)
            TShock.Log.Info($"[组黑名单] {player.Name} 添加豁免玩家: {playerName}");
    }

    private void RemoveExempt(TSPlayer player, string playerName)
    {
        var removed = _config.ExemptPlayers.RemoveAll(
            p => p.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            player.SendErrorMessage($"玩家 {playerName} 不在豁免名单中");
            return;
        }
        _config.Save();
        player.SendSuccessMessage($"已将玩家 {playerName} 从豁免名单移除");
        if (_config.Settings.LogActions)
            TShock.Log.Info($"[组黑名单] {player.Name} 移除豁免玩家: {playerName}");
    }

    private void ShowList(TSPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[c/55CDFF:=== 组黑名单状态 ===]");
        sb.AppendLine($"[c/CCCCCC:状态: {(_config.Settings.Enabled ? "[c/00FF00:开启]" : "[c/FF6B6B:关闭]")}]");
        sb.AppendLine($"[c/CCCCCC:黑名单组 ({_config.BlacklistedGroups.Count}):] {string.Join(", ", _config.BlacklistedGroups)}");
        sb.AppendLine($"[c/CCCCCC:豁免玩家 ({_config.ExemptPlayers.Count}):] {string.Join(", ", _config.ExemptPlayers)}");
        player.SendMessage(sb.ToString(), Color.Cyan);
    }

    private void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
    {
        if (!_config.Settings.Enabled) return;

        var player = e.Player;
        if (player == null || !player.IsLoggedIn) return;

        if (IsExempt(player.Name))
        {
            if (_config.Settings.LogActions)
                TShock.Log.Info($"[组黑名单] 豁免玩家 {player.Name} 跳过检查");
            return;
        }

        if (IsBlacklisted(player.Group.Name))
        {
            player.Disconnect(_config.Settings.KickMessage);
            if (_config.Settings.LogActions)
                TShock.Log.Warn($"[组黑名单] 阻止黑名单组玩家 {player.Name} (组: {player.Group.Name}) 进入");
        }
    }

    private void OnGameUpdate(EventArgs args)
    {
        if (!_config.Settings.Enabled || !_config.Settings.KickOnlineBlacklist) return;

        if ((DateTime.Now - _lastCheck).TotalSeconds < _config.Settings.CheckInterval) return;
        _lastCheck = DateTime.Now;

        foreach (var player in TShock.Players)
        {
            if (player?.Active != true || !player.IsLoggedIn) continue;
            if (IsExempt(player.Name)) continue;

            if (IsBlacklisted(player.Group.Name))
            {
                player.Kick(_config.Settings.InGameKickMessage, true, true);
                if (_config.Settings.LogActions)
                    TShock.Log.Warn($"[组黑名单] 踢出在线黑名单玩家 {player.Name} (组: {player.Group.Name})");
            }
        }
    }

    private void OnReload(ReloadEventArgs args)
    {
        _config = BlacklistConfig.Load();
        args.Player?.SendSuccessMessage("[组黑名单] 配置已重载");
        TShock.Log.Info("[组黑名单] 配置已重载");
    }

    private static bool IsBlacklisted(string groupName) =>
        _config.BlacklistedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase);

    private static bool IsExempt(string playerName) =>
        _config.ExemptPlayers.Contains(playerName, StringComparer.OrdinalIgnoreCase);
}