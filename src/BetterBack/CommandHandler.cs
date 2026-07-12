using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace BetterBack;

public sealed class CommandHandler
{
    private readonly DataManager _dataManager;
    private readonly ConcurrentDictionary<int, DateTime> _cooldowns;
    private readonly ConcurrentDictionary<int, DateTime> _godModePlayers;
    private readonly ConcurrentDictionary<int, DateTime> _autoReturnTimers;

    public CommandHandler(DataManager dataManager, 
        ConcurrentDictionary<int, DateTime> cooldowns,
        ConcurrentDictionary<int, DateTime> godModePlayers,
        ConcurrentDictionary<int, DateTime> autoReturnTimers)
    {
        _dataManager = dataManager;
        _cooldowns = cooldowns;
        _godModePlayers = godModePlayers;
        _autoReturnTimers = autoReturnTimers;
    }

    public void HandleBetCommand(CommandArgs args)
    {
        var player = args.Player;

        if (args.Parameters.Count == 0)
        {
            TeleportToDeathPoint(player, 0);
            return;
        }

        var subCmd = args.Parameters[0].ToLower();

        if (subCmd == "help")
        {
            ShowHelp(player);
            return;
        }

        if (int.TryParse(subCmd, out var index))
        {
            TeleportToDeathPoint(player, index);
            return;
        }

        switch (subCmd)
        {
            case "list":
                ShowDeathPointList(player);
                break;
            case "clear":
                ClearDeathPoints(player);
                break;
            case "auto":
                HandleAutoCommand(args);
                break;
            default:
                ShowHelp(player);
                break;
        }
    }

    public void ShowHelp(TSPlayer player)
    {
        player.SendInfoMessage("=== BetterBack 命令帮助 ===");
        player.SendInfoMessage("/bet - 传送至最新死亡点");
        player.SendInfoMessage("/bet [序号] - 传送至指定死亡点");
        player.SendInfoMessage("/bet list - 查看死亡点列表");
        player.SendInfoMessage("/bet clear - 清除所有死亡点");
        player.SendInfoMessage("/bet auto <秒/off> - 设置死亡后自动返回倒计时");
        player.SendInfoMessage("/bet help - 显示此帮助信息");

        if (player.HasPermission(BetterBackPlugin.PermissionBuff))
            player.SendInfoMessage("/betbuff add/remove <id> - 管理传送Buff");

        if (player.HasPermission(BetterBackPlugin.PermissionGod))
            player.SendInfoMessage("/betgod time <秒> - 设置无敌时间");
    }

    public void ShowDeathPointList(TSPlayer player)
    {
        var playerName = player.Account?.Name ?? player.Name;
        var points = _dataManager.GetPlayerDeathPoints(playerName);

        if (points.Count == 0)
        {
            player.SendInfoMessage("[BetterBack] 无死亡点记录。");
            return;
        }

        player.SendInfoMessage($"=== 死亡点列表 ({points.Count}/{BetterBackConfig.Instance.MaxDeathPointsPerPlayer}) ===");

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            player.SendMessage($"  [{i + 1}] {p.Name} ({p.X / 16}, {p.Y / 16}) {p.DeathTime:MM-dd HH:mm} - {p.DeathReason}", Color.LightGreen);
        }

        player.SendInfoMessage("提示: 输入 /bet 传送到最新点，或 /bet [序号] 传送到指定点");
    }

    public void TeleportToDeathPoint(TSPlayer player, int index = 0)
    {
        if (player.Dead || player.TPlayer.dead)
        {
            player.SendErrorMessage("[BetterBack] 您当前处于死亡状态，请等待复活后再使用 /bet。");
            return;
        }

        _autoReturnTimers.TryRemove(player.Index, out _);

        var playerName = player.Account?.Name ?? player.Name;

        if (IsOnCooldown(player))
        {
            player.SendErrorMessage(string.Format(BetterBackConfig.Instance.CooldownMessage, GetCooldownRemaining(player).TotalSeconds));
            return;
        }

        DeathPoint? target = index == 0
            ? _dataManager.GetLastDeathPoint(playerName)
            : _dataManager.GetDeathPointByIndex(playerName, index);

        if (target == null)
        {
            player.SendErrorMessage(index == 0
                ? "[BetterBack] 无死亡点记录。"
                : "[BetterBack] 无效的死亡点序号。");
            return;
        }

        player.Teleport(target.X, target.Y);

        ApplyBuffs(player);
        SetGodMode(player);
        SetCooldown(player, BetterBackConfig.Instance.TeleportCooldown);

        player.SendSuccessMessage(string.Format(BetterBackConfig.Instance.TeleportSuccessMessage, target.Name));
    }

    public void ClearDeathPoints(TSPlayer player)
    {
        var playerName = player.Account?.Name ?? player.Name;
        var count = _dataManager.ClearPlayerDeathPoints(playerName);
        player.SendSuccessMessage($"[BetterBack] 已清除 {count} 个死亡点。");
    }

    public void HandleAutoCommand(CommandArgs args)
    {
        var player = args.Player;
        var playerName = player.Account?.Name ?? player.Name;

        if (args.Parameters.Count == 1)
        {
            var delay = _dataManager.GetAutoReturnDelay(playerName);
            player.SendInfoMessage(delay > 0
                ? $"[BetterBack] 当前自动返回倒计时: {delay}秒"
                : "[BetterBack] 自动返回已关闭");
            return;
        }

        var param = args.Parameters[1].ToLower();
        if (param == "off")
        {
            _dataManager.SetAutoReturnDelay(playerName, 0);
            player.SendSuccessMessage("[BetterBack] 已关闭自动返回倒计时");
            return;
        }

        if (int.TryParse(param, out var sec) && sec >= 0)
        {
            _dataManager.SetAutoReturnDelay(playerName, sec);
            player.SendSuccessMessage(sec > 0
                ? $"[BetterBack] 自动返回倒计时已设为 {sec} 秒"
                : "[BetterBack] 已关闭自动返回倒计时");
        }
        else
        {
            player.SendErrorMessage("[BetterBack] 用法: /bet auto <秒数> | /bet auto off");
        }
    }

    public void HandleBuffCommand(CommandArgs args)
    {
        var player = args.Player;
        var playerName = player.Account?.Name ?? player.Name;

        if (args.Parameters.Count < 1)
        {
            player.SendInfoMessage("用法: /betbuff add <id> | /betbuff remove <id> | /betbuff list");
            return;
        }

        var cmd = args.Parameters[0].ToLower();
        switch (cmd)
        {
            case "add" when args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out var addId):
                _dataManager.AddCustomBuff(playerName, addId);
                player.SendSuccessMessage($"[BetterBack] 已添加Buff {addId}");
                break;
            case "remove" when args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out var remId):
                _dataManager.RemoveCustomBuff(playerName, remId);
                player.SendSuccessMessage($"[BetterBack] 已移除Buff {remId}");
                break;
            case "list":
                var buffs = _dataManager.GetCustomBuffs(playerName);
                player.SendInfoMessage($"[BetterBack] 自定义Buff: {(buffs.Count > 0 ? string.Join(", ", buffs) : "无")}");
                break;
            default:
                player.SendInfoMessage("用法: /betbuff add <id> | /betbuff remove <id> | /betbuff list");
                break;
        }
    }

    public void HandleGodCommand(CommandArgs args)
    {
        var player = args.Player;
        var playerName = player.Account?.Name ?? player.Name;

        if (args.Parameters.Count < 1)
        {
            player.SendInfoMessage("用法: /betgod time <秒> | /betgod info");
            return;
        }

        var cmd = args.Parameters[0].ToLower();
        switch (cmd)
        {
            case "time" when args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out var sec):
                _dataManager.SetCustomGodModeDuration(playerName, sec);
                player.SendSuccessMessage($"[BetterBack] 无敌时间设为 {sec} 秒");
                break;
            case "info":
                var custom = _dataManager.GetCustomGodModeDuration(playerName);
                var def = BetterBackConfig.Instance.GodModeDuration;
                player.SendInfoMessage($"[BetterBack] 当前无敌时间: {(custom > 0 ? custom : def)} 秒");
                break;
            default:
                player.SendInfoMessage("用法: /betgod time <秒> | /betgod info");
                break;
        }
    }

    private void ApplyBuffs(TSPlayer player)
    {
        var playerName = player.Account?.Name ?? player.Name;
        var buffs = BetterBackConfig.Instance.DefaultBuffIDs
            .Concat(_dataManager.GetCustomBuffs(playerName))
            .Distinct()
            .Where(b => b > 0);

        var duration = BetterBackConfig.Instance.BuffDuration * 60;

        foreach (var buff in buffs)
            player.SetBuff(buff, duration, false);
    }

    private void SetGodMode(TSPlayer player)
    {
        var playerName = player.Account?.Name ?? player.Name;
        var custom = _dataManager.GetCustomGodModeDuration(playerName);
        var duration = custom > 0 ? custom : BetterBackConfig.Instance.GodModeDuration;

        if (duration <= 0) return;

        player.GodMode = true;
        _godModePlayers[player.Index] = DateTime.Now.AddSeconds(duration);
        player.SendInfoMessage(string.Format(BetterBackConfig.Instance.GodModeMessage, duration));
    }

    private bool IsOnCooldown(TSPlayer player) =>
        _cooldowns.TryGetValue(player.Index, out var end) && DateTime.Now < end;

    private TimeSpan GetCooldownRemaining(TSPlayer player)
    {
        if (_cooldowns.TryGetValue(player.Index, out var end))
        {
            var rem = end - DateTime.Now;
            return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
        }
        return TimeSpan.Zero;
    }

    private void SetCooldown(TSPlayer player, int seconds) =>
        _cooldowns[player.Index] = DateTime.Now.AddSeconds(seconds);
}
