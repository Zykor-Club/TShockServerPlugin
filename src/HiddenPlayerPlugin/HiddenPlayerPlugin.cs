using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OTAPI;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace HiddenPlayerPlugin
{
    [ApiVersion(2, 1)]
    public class HiddenPlayerPlugin : TerrariaPlugin
    {
        public override Version Version => new Version(1, 3, 0);

        internal static readonly HashSet<int> InvisiblePlayerIndices = new HashSet<int>();
        public override string Name => "HiddenPlayerPlugin";
        public override string Author => "星梦";
        public override string Description => "隐藏特定玩家的加入广播、在线列表显示并使其对其它玩家隐身";

        private static readonly HashSet<string> HiddenPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "HiddenPlayerConfig.json");
        internal static HiddenPlayerConfig _config = new HiddenPlayerConfig();

        private Harmony _harmony;

        public HiddenPlayerPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            LoadConfig();
            RegisterCommands();
            RegisterHooks();
            ApplyHarmonyPatches();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                GeneralHooks.ReloadEvent -= OnReloadEvent;
                Hooks.NetMessage.SendData -= OnNetMessageSendData;
                RemoveHarmonyPatches();
                Commands.ChatCommands.RemoveAll(c => c.Names.Contains("hideplayer"));
            }
            base.Dispose(disposing);
        }

        private void RegisterHooks()
        {
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            GeneralHooks.ReloadEvent += OnReloadEvent;
            Hooks.NetMessage.SendData += OnNetMessageSendData;
        }

        private void RegisterCommands()
        {
            Commands.ChatCommands.Add(new Command("hiddenplayer.admin", HidePlayerCommand, "hideplayer")
            {
                HelpText = "管理隐藏玩家",
                HelpDesc = new[]
                {
                    "/hideplayer add <玩家名> - 添加隐藏玩家",
                    "/hideplayer remove <玩家名> - 移除隐藏玩家",
                    "/hideplayer list - 列出所有隐藏玩家",
                    "/hideplayer reload - 重新加载配置",
                    "/hideplayer save - 保存配置"
                }
            });
        }

        private void ApplyHarmonyPatches()
        {
            _harmony = new Harmony("com.hiddenplayer.plugin");
            
            var getActivePlayerCountMethod = typeof(TShockAPI.Utils).GetMethod("GetActivePlayerCount", BindingFlags.Public | BindingFlags.Instance);
            if (getActivePlayerCountMethod != null)
            {
                _harmony.Patch(getActivePlayerCountMethod, postfix: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.GetActivePlayerCount_Postfix)));
            }

            var listConnectedPlayersMethod = typeof(Commands).GetMethod("ListConnectedPlayers", BindingFlags.NonPublic | BindingFlags.Static);
            if (listConnectedPlayersMethod != null)
            {
                _harmony.Patch(listConnectedPlayersMethod, prefix: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.ListConnectedPlayers_Prefix)));
            }

            var sendFileTextAsMessageMethod = typeof(TSPlayer).GetMethod("SendFileTextAsMessage", BindingFlags.Public | BindingFlags.Instance);
            if (sendFileTextAsMessageMethod != null)
            {
                _harmony.Patch(sendFileTextAsMessageMethod, transpiler: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.SendFileTextAsMessage_Transpiler)));
            }

            var canSpectateMethod = typeof(Player).GetMethod("CanSpectate", BindingFlags.Public | BindingFlags.Instance);
            if (canSpectateMethod != null)
            {
                _harmony.Patch(canSpectateMethod, postfix: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.CanSpectate_Postfix)));
            }

            TShock.Log.ConsoleInfo("[HiddenPlayerPlugin] Harmony patches applied successfully");
        }

        private void RemoveHarmonyPatches()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll();
                TShock.Log.ConsoleInfo("[HiddenPlayerPlugin] Harmony patches removed");
            }
        }

        private void HidePlayerCommand(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                SendHelp(args.Player);
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "add":
                    AddHiddenPlayer(args);
                    break;
                case "remove":
                case "del":
                    RemoveHiddenPlayer(args);
                    break;
                case "list":
                    ListHiddenPlayers(args);
                    break;
                case "reload":
                    ReloadConfig(args);
                    break;
                case "save":
                    SaveConfig(args);
                    break;
                default:
                    SendHelp(args.Player);
                    break;
            }
        }

        private void SendHelp(TSPlayer player)
        {
            player.SendMessage("=== HiddenPlayerPlugin 命令帮助 ===", Color.Gold);
            player.SendMessage("/hideplayer add <玩家名> - 添加隐藏玩家", Color.White);
            player.SendMessage("/hideplayer remove <玩家名> - 移除隐藏玩家", Color.White);
            player.SendMessage("/hideplayer list - 列出所有隐藏玩家", Color.White);
            player.SendMessage("/hideplayer reload - 重新加载配置", Color.White);
            player.SendMessage("/hideplayer save - 保存配置", Color.White);
        }

        private void AddHiddenPlayer(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /hideplayer add <玩家名>");
                return;
            }

            string playerName = string.Join(" ", args.Parameters.Skip(1));
            if (HiddenPlayers.Contains(playerName))
            {
                args.Player.SendErrorMessage($"{playerName} 已经在隐藏列表中");
                return;
            }

            HiddenPlayers.Add(playerName);
            SaveConfig();

            TSPlayer onlinePlayer = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            if (onlinePlayer != null)
            {
                ApplyInvisibility(onlinePlayer);
            }

            args.Player.SendSuccessMessage($"{playerName} 已添加到隐藏列表");
        }

        private void RemoveHiddenPlayer(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("用法: /hideplayer remove <玩家名>");
                return;
            }

            string playerName = string.Join(" ", args.Parameters.Skip(1));
            if (!HiddenPlayers.Contains(playerName))
            {
                args.Player.SendErrorMessage($"{playerName} 不在隐藏列表中");
                return;
            }

            HiddenPlayers.Remove(playerName);
            SaveConfig();

            TSPlayer onlinePlayer = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            if (onlinePlayer != null)
            {
                DisableInvisibility(onlinePlayer);
            }

            args.Player.SendSuccessMessage($"{playerName} 已从隐藏列表移除");
        }

        private void ListHiddenPlayers(CommandArgs args)
        {
            if (HiddenPlayers.Count == 0)
            {
                args.Player.SendMessage("隐藏列表为空", Color.White);
                return;
            }

            args.Player.SendMessage($"=== 隐藏玩家列表 ({HiddenPlayers.Count}) ===", Color.Gold);
            foreach (string playerName in HiddenPlayers)
            {
                bool isOnline = TShock.Players.Any(p => p != null && p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                string status = isOnline ? "[在线]" : "[离线]";
                args.Player.SendMessage($"- {playerName} {status}", isOnline ? Color.Green : Color.Gray);
            }
        }

        private void ReloadConfig(CommandArgs args)
        {
            LoadConfig();
            args.Player.SendSuccessMessage("配置已重新加载");
        }

        private void SaveConfig(CommandArgs args)
        {
            SaveConfig();
            args.Player.SendSuccessMessage("配置已保存");
        }

        private void LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                SaveConfig();
                TShock.Log.ConsoleInfo("[HiddenPlayerPlugin] 配置文件已自动生成");
                return;
            }

            try
            {
                string content = File.ReadAllText(ConfigPath);
                _config = JsonConvert.DeserializeObject<HiddenPlayerConfig>(content) ?? new HiddenPlayerConfig();
                if (_config.HiddenPlayerNames != null)
                {
                    HiddenPlayers.Clear();
                    foreach (string name in _config.HiddenPlayerNames)
                    {
                        HiddenPlayers.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"加载配置失败: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                _config.HiddenPlayerNames = HiddenPlayers.ToList();
                string content = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(ConfigPath, content);
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"保存配置失败: {ex.Message}");
            }
        }

        private void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null) return;

            if (IsHiddenPlayer(player.Name))
            {
                player.SilentJoinInProgress = true;
            }
        }

        private void OnServerJoin(JoinEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null) return;

            if (IsHiddenPlayer(player.Name))
            {
                ApplyInvisibility(player);
            }
        }

        private void ApplyInvisibility(TSPlayer player)
        {
            if (!_config.EnableInvisibility)
                return;

            InvisiblePlayerIndices.Add(player.Index);

            NetMessage.SendData((int)PacketTypes.PlayerActive, -1, player.Index, null, player.Index, 0f);
            NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, player.Index, null, player.Index, 0f, 0f, 0f, 0);

            TShock.Log.ConsoleInfo($"[HiddenPlayerPlugin] {player.Name} 已进入隐身状态 (index={player.Index})");
        }

        private void DisableInvisibility(TSPlayer player)
        {
            if (!InvisiblePlayerIndices.Remove(player.Index))
                return;

            NetMessage.SendData((int)PacketTypes.PlayerActive, -1, -1, null, player.Index, 1f);
            NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, player.Index, null, player.Index);

            TShock.Log.ConsoleInfo($"[HiddenPlayerPlugin] {player.Name} 已退出隐身状态");
        }

        private int _frameCounter = 0;

        private void OnGameUpdate(EventArgs args)
        {
            if (!_config.EnableInvisibility || InvisiblePlayerIndices.Count == 0)
                return;

            _frameCounter++;
            if (_frameCounter < 60)
                return;
            _frameCounter = 0;

            foreach (int index in InvisiblePlayerIndices.ToList())
            {
                if (index < 0 || index >= TShock.Players.Length)
                    continue;

                TSPlayer player = TShock.Players[index];
                if (player == null || !Main.player[index].active)
                    continue;

                NetMessage.SendData((int)PacketTypes.PlayerActive, -1, index, null, index, 0f);
            }
        }

        private void OnServerLeave(LeaveEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null) return;

            if (IsHiddenPlayer(player.Name))
            {
                player.SilentKickInProgress = true;
                InvisiblePlayerIndices.Remove(args.Who);
            }
        }

        private void OnReloadEvent(ReloadEventArgs args)
        {
            LoadConfig();
            TShock.Log.ConsoleInfo("[HiddenPlayerPlugin] 配置已通过服务器 /reload 命令重新加载");
        }

        private void OnNetMessageSendData(object sender, Hooks.NetMessage.SendDataEventArgs e)
        {
            if (!_config.EnableInvisibility)
                return;

            if (e.MsgType != (int)PacketTypes.PlayerActive)
                return;

            int playerIndex = e.Number;
            if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
                return;

            if (!InvisiblePlayerIndices.Contains(playerIndex))
                return;

            if (e.Number2 == 0f)
                return;

            if (e.RemoteClient == -1)
            {
                e.Number2 = 0f;
                if (e.IgnoreClient != playerIndex)
                {
                    e.IgnoreClient = playerIndex;
                }
            }
            else
            {
                if (e.RemoteClient != playerIndex)
                {
                    e.Number2 = 0f;
                }
            }
        }

        public static bool IsHiddenPlayer(string playerName)
        {
            return HiddenPlayers.Contains(playerName);
        }

        public static List<TSPlayer> GetVisiblePlayers(TSPlayer viewer)
        {
            return TShock.Players
                .Where(p => p != null && p.Active)
                .Where(p => !IsHiddenPlayer(p.Name))
                .ToList();
        }
    }

    public static class HiddenPlayerPatches
    {
        public static void GetActivePlayerCount_Postfix(ref int __result)
        {
            int hiddenCount = TShock.Players.Count(p => p != null && p.Active && p.FinishedHandshake && HiddenPlayerPlugin.IsHiddenPlayer(p.Name));
            __result -= hiddenCount;
        }

        public static bool ListConnectedPlayers_Prefix(CommandArgs args)
        {
            bool invalidUsage = args.Parameters.Count > 2;

            bool displayIdsRequested = false;
            int pageNumber = 1;
            if (!invalidUsage)
            {
                foreach (string parameter in args.Parameters)
                {
                    if (parameter.Equals("-i", StringComparison.InvariantCultureIgnoreCase))
                    {
                        displayIdsRequested = true;
                        continue;
                    }

                    if (!int.TryParse(parameter, out pageNumber))
                    {
                        invalidUsage = true;
                        break;
                    }
                }
            }
            if (invalidUsage)
            {
                args.Player.SendMessage("List Online Players Syntax", Color.White);
                args.Player.SendMessage($"{TShockAPI.Commands.Specifier}playing [-i] [page]", Color.White);
                args.Player.SendMessage("Command aliases: playing, online, who", Color.White);
                args.Player.SendMessage($"Example usage: {TShockAPI.Commands.Specifier}who -i", Color.White);
                return false;
            }
            if (displayIdsRequested && !args.Player.HasPermission(Permissions.seeids))
            {
                args.Player.SendErrorMessage("You do not have permission to see player IDs.");
                return false;
            }

            var visiblePlayers = TShock.Players.Where(p => p != null && p.Active && p.FinishedHandshake && !HiddenPlayerPlugin.IsHiddenPlayer(p.Name)).ToList();

            if (visiblePlayers.Count == 0)
            {
                args.Player.SendMessage("There are currently no players online.", Color.White);
                return false;
            }
            args.Player.SendMessage($"Online Players ({visiblePlayers.Count}/{TShock.Config.Settings.MaxSlots})", Color.White);

            var players = new List<string>();

            foreach (TSPlayer ply in visiblePlayers)
            {
                if (displayIdsRequested)
                    if (ply.Account != null)
                        players.Add($"{ply.Name} (Index: {ply.Index}, Account ID: {ply.Account.ID})");
                    else
                        players.Add($"{ply.Name} (Index: {ply.Index})");
                else
                    players.Add(ply.Name);
            }

            PaginationTools.SendPage(
                args.Player, pageNumber, PaginationTools.BuildLinesFromTerms(players),
                new PaginationTools.Settings
                {
                    IncludeHeader = false,
                    FooterFormat = $"Type {TShockAPI.Commands.Specifier}who {(displayIdsRequested ? "-i" : string.Empty)} for more."
                }
            );

            return false;
        }

        public static IEnumerable<CodeInstruction> SendFileTextAsMessage_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeList = instructions.ToList();
            var addMethod = typeof(List<string>).GetMethod("Add");

            for (int i = 0; i < codeList.Count; i++)
            {
                if (codeList[i].opcode == OpCodes.Callvirt && codeList[i].operand is MethodInfo method && method == addMethod)
                {
                    codeList[i].opcode = OpCodes.Call;
                    codeList[i].operand = typeof(HiddenPlayerPatches).GetMethod("FilteredPlayerAdd");
                }
            }

            return codeList.AsEnumerable();
        }

        public static void FilteredPlayerAdd(List<string> players, string playerName)
        {
            if (!HiddenPlayerPlugin.IsHiddenPlayer(playerName))
            {
                players.Add(playerName);
            }
        }

        public static void CanSpectate_Postfix(Player __instance, int who, ref bool __result)
        {
            if (!__result)
                return;

            if (who < 0 || who == __instance.whoAmI)
                return;

            Player target = Main.player[who];
            if (target != null && target.active && HiddenPlayerPlugin.IsHiddenPlayer(target.name))
            {
                __result = false;
            }
        }
    }

    public class HiddenPlayerConfig
    {
        public List<string> HiddenPlayerNames { get; set; } = new List<string>();
        public bool EnableInvisibility { get; set; } = true;
    }
}