using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terraria;
using Terraria.GameContent;
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
        public override Version Version => new(1, 5, 0);
        public override string Description => "Boss击杀后自动执行预设命令";

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
            
            ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
            
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

        private void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled || !Config.Enabled) return;
            
            var npc = Main.npc[args.NpcId];
            if (npc?.active != true) return;

            var bossConfig = FindBossConfig(npc.type);
            if (bossConfig == null) return;

            _bossTracker.MarkAsAlive(npc.whoAmI, npc.type, bossConfig.Name);
        }

        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (args.npc == null || args.npc.type <= 0 || !Config.Enabled) return;

            var npc = args.npc;
            var config = FindBossConfig(npc.type);
            if (config == null) return;

            if (BossTracker.MultiSegmentBossMap.ContainsKey(npc.type))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 多体节Boss {npc.FullName} 体节死亡，等待OnBossKilled钩子处理");
                return;
            }

            if (!_bossTracker.TryProcessDeath(npc.whoAmI, npc.type))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 跳过重复或未追踪的死亡: {npc.FullName} (Idx:{npc.whoAmI}, Type:{npc.type})");
                return;
            }

            if (config.RequireSummoned && !WasBossSpawned(npc.type))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 未记录生成，跳过: {npc.FullName}");
                return;
            }

            Task.Run(() => ProcessBossKill(config, npc));
        }

        private bool WasBossSpawned(int npcType)
        {
            if (_bossTracker.WasEverSpawned(npcType))
                return true;

            if (BossTracker.MultiSegmentBossMap.TryGetValue(npcType, out var segmentTypes))
            {
                foreach (var type in segmentTypes)
                {
                    if (_bossTracker.WasEverSpawned(type))
                        return true;
                }
            }

            var customDef = NPCDamageTracker.CustomBossDefinitions[npcType];
            if (customDef != null && customDef.NPCTypes != null)
            {
                foreach (var type in customDef.NPCTypes)
                {
                    if (_bossTracker.WasEverSpawned(type))
                        return true;
                }
            }

            return false;
        }

        private void OnBossKilledHook(
            On.Terraria.GameContent.BossDamageTracker.orig_OnBossKilled orig,
            Terraria.GameContent.BossDamageTracker self, NPC npc)
        {
            orig(self, npc);
            if (!Config.Enabled) return;

            var config = FindBossConfig(npc.type);
            if (config == null) return;

            if (config.RequireSummoned && !WasBossSpawned(npc.type))
            {
                TShock.Log.ConsoleDebug($"[BossCommand] OnBossKilled: 未记录生成，跳过: {config.Name}");
                return;
            }

            TShock.Log.ConsoleInfo($"[BossCommand] OnBossKilled钩子触发: {config.Name}");
            Task.Run(() => ProcessBossKill(config, npc));

            if (Config.AutoBroadcastDamageRanking)
            {
                _damageRanker.Broadcast(self, npc);
            }
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
                    foreach (var player in TShock.Players.Where(p => p?.Active == true))
                    {
                        player.SendMessage(msg, Config.BroadcastColor.R, Config.BroadcastColor.G, Config.BroadcastColor.B);
                    }
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
            
            if (!config.FirstKillDone && config.FirstKillCommands?.Count > 0)
            {
                commands.AddRange(config.FirstKillCommands);
                config.FirstKillDone = true;
                TShock.Log.ConsoleInfo($"[BossCommand] 触发首次击杀命令");
            }
            
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
