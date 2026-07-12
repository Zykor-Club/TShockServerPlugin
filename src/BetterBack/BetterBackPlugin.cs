using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace BetterBack;

[ApiVersion(2, 1)]
public sealed class BetterBackPlugin : TerrariaPlugin
{
    public override string Author => "星梦XM";
    public override string Description => "增强版Back";
    public override string Name => "BetterBack";
    public override Version Version => new Version(2026, 5, 2, 0);

    public const string PermissionUse = "betterback.use";
    public const string PermissionBuff = "betterback.buff";
    public const string PermissionGod = "betterback.god";
    public const string PermissionAdmin = "betterback.admin";

    private readonly DataManager _dataManager = new();
    private readonly ConcurrentDictionary<int, DateTime> _cooldowns = new();
    private readonly ConcurrentDictionary<int, DateTime> _godModePlayers = new();
    private readonly ConcurrentDictionary<int, DateTime> _autoReturnTimers = new();
    private readonly ConcurrentDictionary<int, bool> _wasDead = new();
    private System.Timers.Timer? _updateTimer;
    private readonly List<Command> _registeredCommands = new();
    private CommandHandler? _commandHandler;

    public BetterBackPlugin(Main game) : base(game) 
    {
        BetterBackConfig.Instance.Load();
    }

    public override void Initialize()
    {
        GetDataHandlers.KillMe += OnPlayerDeath;
        ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnPlayerGreet);
        GeneralHooks.ReloadEvent += OnReload;

        _updateTimer = new System.Timers.Timer(1000);
        _updateTimer.Elapsed += OnTimerElapsed;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();

        _commandHandler = new CommandHandler(_dataManager, _cooldowns, _godModePlayers, _autoReturnTimers);
        RegisterCommands();
        TShock.Log.ConsoleInfo($"[BetterBack] 插件已加载 v{Version}");
    }

    private void RegisterCommands()
    {
        RegisterCommand(new Command(PermissionUse, args => _commandHandler?.HandleBetCommand(args), "bet")
        { HelpText = "/bet [序号] - 传送至死亡点(无序号则传最新)，/bet list - 列表，/bet clear - 清除，/bet auto - 自动返回" });

        RegisterCommand(new Command(PermissionBuff, args => _commandHandler?.HandleBuffCommand(args), "betbuff")
        { HelpText = "管理传送Buff: add <id>, remove <id>, list" });

        RegisterCommand(new Command(PermissionGod, args => _commandHandler?.HandleGodCommand(args), "betgod")
        { HelpText = "管理无敌时间: time <秒数>, info" });
    }

    private void RegisterCommand(Command cmd)
    {
        _registeredCommands.Add(cmd);
        Commands.ChatCommands.Add(cmd);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GetDataHandlers.KillMe -= OnPlayerDeath;
            ServerApi.Hooks.ServerLeave.Deregister(this, OnPlayerLeave);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnPlayerGreet);
            GeneralHooks.ReloadEvent -= OnReload;

            _updateTimer?.Stop();
            _updateTimer?.Dispose();

            foreach (var cmd in _registeredCommands)
                Commands.ChatCommands.Remove(cmd);
            _registeredCommands.Clear();
        }
        base.Dispose(disposing);
    }

    private void OnReload(ReloadEventArgs args)
    {
        BetterBackConfig.Instance.Load();
        args.Player?.SendSuccessMessage("[BetterBack] 配置已重载");
        TShock.Log.ConsoleInfo("[BetterBack] 配置已通过 /reload 重载");
    }

    #region 事件处理

    private void OnPlayerDeath(object? sender, GetDataHandlers.KillMeEventArgs args)
    {
        try
        {
            var player = args.Player;
            if (!player.HasPermission(PermissionUse))
                return;

            if (BetterBackConfig.Instance.BlockDungeonDeathBeforeSkeletron && 
                player.TPlayer.ZoneDungeon && !NPC.downedBoss3)
            {
                player.SendInfoMessage("[BetterBack] 未击败骷髅王，地牢死亡点未被记录。");
                return;
            }

            if (BetterBackConfig.Instance.BlockTempleDeathBeforePlantera && 
                player.TPlayer.ZoneLihzhardTemple && !NPC.downedPlantBoss)
            {
                player.SendInfoMessage("[BetterBack] 未击败世纪之花，神庙死亡点未被记录。");
                return;
            }

            var reason = ExtractDeathReason(args.PlayerDeathReason);
            var playerName = player.Account?.Name ?? player.Name;

            if (_dataManager.AddDeathPoint(playerName, (int)player.X, (int)player.Y, reason))
            {
                var count = _dataManager.GetPlayerDeathCount(playerName);
                var max = BetterBackConfig.Instance.MaxDeathPointsPerPlayer;
                player.SendInfoMessage(string.Format(BetterBackConfig.Instance.DeathPointRecordedMessage, count, max));
            }

            _wasDead[player.Index] = true;
            _autoReturnTimers.TryRemove(player.Index, out _);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[BetterBack] 记录死亡点失败: {ex}");
        }
    }

    private void OnPlayerLeave(LeaveEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player is { IsLoggedIn: true })
        {
            _godModePlayers.TryRemove(player.Index, out _);
            _cooldowns.TryRemove(player.Index, out _);
            _autoReturnTimers.TryRemove(player.Index, out _);
            _wasDead.TryRemove(player.Index, out _);
        }
    }

    private void OnPlayerGreet(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player is { IsLoggedIn: true })
        {
            var count = _dataManager.GetPlayerDeathCount(player.Account?.Name ?? player.Name);
            if (count > 0)
            {
                player.SendInfoMessage($"[BetterBack] 您有 {count} 个历史死亡点，使用 /bet 传送至最新，或 /bet list 查看列表。");
            }
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        var now = DateTime.Now;
        
        foreach (var (index, endTime) in _godModePlayers.ToList())
        {
            var remaining = endTime - now;
            if (remaining.TotalSeconds <= 0)
            {
                if (_godModePlayers.TryRemove(index, out _))
                {
                    var player = TShock.Players[index];
                    if (player is { Active: true })
                    {
                        player.GodMode = false;
                        player.SendInfoMessage("[BetterBack] 无敌时间已结束。");
                    }
                }
            }
            else if (remaining.TotalSeconds <= 3)
            {
                var player = TShock.Players[index];
                player?.SendInfoMessage($"[BetterBack] 无敌时间剩余: {remaining.TotalSeconds:F0}秒");
            }
        }

        foreach (var player in TShock.Players)
        {
            if (player?.Active != true || !player.IsLoggedIn) continue;
            
            bool isDead = player.Dead || player.TPlayer.dead;
            bool wasDead = _wasDead.GetValueOrDefault(player.Index);
            
            if (wasDead && !isDead)
            {
                var delay = _dataManager.GetAutoReturnDelay(player.Account?.Name ?? player.Name);
                if (delay > 0)
                    _autoReturnTimers[player.Index] = now.AddSeconds(delay);
            }
            _wasDead[player.Index] = isDead;
        }

        foreach (var (index, endTime) in _autoReturnTimers.ToList())
        {
            var player = TShock.Players[index];
            if (player?.Active != true)
            {
                _autoReturnTimers.TryRemove(index, out _);
                continue;
            }

            var remaining = endTime - now;
            if (remaining.TotalSeconds <= 0)
            {
                if (_autoReturnTimers.TryRemove(index, out _))
                {
                    player.SendInfoMessage("[BetterBack] 自动返回倒计时结束，正在传送...");
                    _commandHandler?.TeleportToDeathPoint(player, 0);
                }
            }
            else if (remaining.TotalSeconds <= 5)
            {
                player.SendInfoMessage($"[BetterBack] 自动返回剩余: {remaining.TotalSeconds:F0}秒");
            }
        }
    }

    #endregion

    #region 工具方法

    private static string ExtractDeathReason(PlayerDeathReason? reason)
    {
        if (reason == null) return "未知原因";
        
        if (reason._sourceNPCIndex >= 0 && reason._sourceNPCIndex < Main.npc.Length && Main.npc[reason._sourceNPCIndex].active)
            return $"被 {Main.npc[reason._sourceNPCIndex].FullName} 击杀";
        
        if (reason._sourceProjectileType > 0)
            return "被弹幕击杀";
        
        if (reason._sourcePlayerIndex >= 0 && reason._sourcePlayerIndex < TShock.Players.Length)
        {
            var attacker = TShock.Players[reason._sourcePlayerIndex];
            if (attacker != null) return $"被 {attacker.Name} 击杀";
        }

        return reason._sourceCustomReason ?? "未知原因";
    }

    #endregion
}
