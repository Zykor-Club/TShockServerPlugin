using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace BossCommandExecutor
{
    [ApiVersion(2, 1)]
    public class BossCommandPlugin : TerrariaPlugin
    {
        public override string Name => "BossCommandExecutor";
        public override string Author => "星梦XM";
        public override Version Version => new(2, 0, 0);
        public override string Description => "Boss击杀后自动执行预设命令";

        // 只保留必要的服务
        private readonly BossTracker _bossTracker;
        private readonly CommandProcessor _commandProcessor;
        private readonly DamageRankBroadcaster _damageRanker;
        private readonly FloatingTextService _floatingTextService;
        
        internal static Configuration Config { get; private set; } = new();

        public BossCommandPlugin(Main game) : base(game)
        {
            _bossTracker = new BossTracker();
            _commandProcessor = new CommandProcessor();
            _damageRanker = new DamageRankBroadcaster();
            _floatingTextService = new FloatingTextService();
        }

        public override void Initialize()
        {
            LoadConfig();
            GeneralHooks.ReloadEvent += OnConfigReload;
            
            // 只需要这两个事件：生成和死亡
            ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
            
            // 伤害排行钩子
            On.Terraria.GameContent.BossDamageTracker.OnBossKilled += OnBossKilledHook;
            
            TShock.Log.ConsoleInfo($"[BossCommand] 插件已加载 v{Version} | 极简逻辑版");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GeneralHooks.ReloadEvent -= OnConfigReload;
                ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
                On.Terraria.GameContent.BossDamageTracker.OnBossKilled -= OnBossKilledHook;
                _bossTracker?.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void LoadConfig(ReloadEventArgs? args = null)
        {
            try
            {
                Config = Configuration.Read();
                TShock.Log.ConsoleInfo("[BossCommand] 配置加载完成");
                args?.Player?.SendSuccessMessage("[BossCommand] 配置已重载");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BossCommand] 配置加载失败: {ex.Message}");
            }
        }
        private void OnConfigReload(ReloadEventArgs args) => LoadConfig(args);

        /// <summary>
        /// 【核心逻辑】Boss生成时记录实例
        /// 不管怎么生成的（物品/自然/命令），只要生成了就记录
        /// </summary>
        private void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled || !Config.Enabled) return;
            
            var npc = Main.npc[args.NpcId];
            if (npc?.active != true) return;

            var bossConfig = FindBossConfig(npc.netID);
            if (bossConfig == null) return;

            // 记录这个实例：whoAmI是实例ID，netID是Boss类型
            _bossTracker.MarkAsAlive(npc.whoAmI, npc.netID, bossConfig.Name);
        }

        /// <summary>
        /// 【核心逻辑】Boss死亡时执行命令
        /// 使用whoAmI防止多体节Boss（毁灭者/世界吞噬者）重复触发
        /// </summary>
        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (args.npc?.active != true || !Config.Enabled) return;

            var npc = args.npc;
            var config = FindBossConfig(npc.netID);
            if (config == null) return;

            // 核心：检查这个实例是否被追踪过（生成时MarkAsAlive的）
            // 多体节Boss（134/135/136）只有第一个死亡的体节能通过检查，其他体节会返回false
            if (!_bossTracker.TryProcessDeath(npc.whoAmI, npc.netID))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 跳过重复或未追踪的死亡: {npc.FullName} (Idx:{npc.whoAmI})");
                return;
            }

            // 如果配置了RequireSummoned=false，也执行（兼容旧配置）
            // 实际上现在只要TryProcessDeath返回true，就是服务器里生成过的Boss
            if (config.RequireSummoned && !_bossTracker.WasEverSpawned(npc.netID))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 未记录生成，跳过: {npc.FullName}");
                return;
            }

            Task.Run(() => ProcessBossKill(config, npc));
        }

        private void OnBossKilledHook(
            On.Terraria.GameContent.BossDamageTracker.orig_OnBossKilled orig,
            Terraria.GameContent.BossDamageTracker self, NPC npc)
        {
            orig(self, npc);
            if (!Config.Enabled || !Config.AutoBroadcastDamageRanking) return;
            if (FindBossConfig(npc.netID) == null) return;
            _damageRanker.Broadcast(self, npc);
        }

        private async Task ProcessBossKill(Configuration.BossCommandConfig config, NPC npc)
        {
            try
            {
                TShock.Log.ConsoleInfo($"[BossCommand] Boss {config.Name} 被击败，开始执行命令...");
                
                var commands = BuildCommandList(config);
                if (commands.Count == 0) return;

                int successCount = 0;
                foreach (var cmd in commands)
                {
                    if (await _commandProcessor.ExecuteAsync(cmd, config, npc))
                        successCount++;
                    if (Config.CommandDelay > 0)
                        await Task.Delay(Config.CommandDelay);
                }

                if (config.RecordExecutionCount) config.ExecutionCount++;
                Config.Write();

                if (config.BroadcastResult && successCount > 0 && Config.BroadcastEnabled)
                {
                    string msg = Config.BroadcastFormat
                        .Replace("{boss}", config.Name)
                        .Replace("{count}", successCount.ToString());
                    TShock.Utils.Broadcast(msg, Config.BroadcastColor.R, Config.BroadcastColor.G, Config.BroadcastColor.B);
                }

                _floatingTextService.ShowForBoss(config, npc);
                TShock.Log.ConsoleInfo($"[BossCommand] {config.Name} 执行完成: {successCount}/{commands.Count}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BossCommand] 处理击杀失败: {ex}");
            }
        }

        private List<string> BuildCommandList(Configuration.BossCommandConfig config)
        {
            var commands = new List<string>();
            
            // 首次击杀
            if (!config.FirstKillDone && config.FirstKillCommands?.Count > 0)
            {
                commands.AddRange(config.FirstKillCommands);
                config.FirstKillDone = true;
                TShock.Log.ConsoleInfo($"[BossCommand] 触发首次击杀命令");
            }
            
            // 常规命令
            if (config.Commands?.Count > 0)
                commands.AddRange(config.Commands);
            
            return commands;
        }

        private Configuration.BossCommandConfig? FindBossConfig(int npcId)
        {
            return Config.BossCommands?.FirstOrDefault(b => b.BossIDs?.Contains(npcId) == true);
        }
    }
}
