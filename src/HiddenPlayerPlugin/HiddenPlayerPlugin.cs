using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace HiddenPlayerPlugin
{
    [ApiVersion(2, 1)]
    public class HiddenPlayerPlugin : TerrariaPlugin
    {
        public override Version Version => new Version(1, 1, 0);
        public override string Name => "HiddenPlayerPlugin";
        public override string Author => "星梦";
        public override string Description => "隐藏特定玩家的加入广播和在线列表显示";

        private static readonly HashSet<string> HiddenPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "HiddenPlayerConfig.json");
        
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
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                RemoveHarmonyPatches();
                Commands.ChatCommands.RemoveAll(c => c.Names.Contains("hideplayer"));
            }
            base.Dispose(disposing);
        }

        private void RegisterHooks()
        {
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
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

            var listConnectedPlayersMethod = typeof(Commands).GetMethod("ListConnectedPlayers", BindingFlags.Public | BindingFlags.Static);
            if (listConnectedPlayersMethod != null)
            {
                _harmony.Patch(listConnectedPlayersMethod, transpiler: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.ListConnectedPlayers_Transpiler)));
            }

            var sendFileTextAsMessageMethod = typeof(TSPlayer).GetMethod("SendFileTextAsMessage", BindingFlags.Public | BindingFlags.Instance);
            if (sendFileTextAsMessageMethod != null)
            {
                _harmony.Patch(sendFileTextAsMessageMethod, transpiler: new HarmonyMethod(typeof(HiddenPlayerPatches), nameof(HiddenPlayerPatches.SendFileTextAsMessage_Transpiler)));
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
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string content = File.ReadAllText(ConfigPath);
                    var config = JsonConvert.DeserializeObject<HiddenPlayerConfig>(content);
                    if (config?.HiddenPlayerNames != null)
                    {
                        HiddenPlayers.Clear();
                        foreach (string name in config.HiddenPlayerNames)
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
        }

        private void SaveConfig()
        {
            try
            {
                var config = new HiddenPlayerConfig
                {
                    HiddenPlayerNames = HiddenPlayers.ToList()
                };
                string content = JsonConvert.SerializeObject(config, Formatting.Indented);
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

        private void OnServerLeave(LeaveEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null) return;

            if (IsHiddenPlayer(player.Name))
            {
                player.SilentKickInProgress = true;
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

        public static IEnumerable<CodeInstruction> ListConnectedPlayers_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeList = instructions.ToList();
            
            for (int i = 0; i < codeList.Count; i++)
            {
                if (codeList[i].opcode == OpCodes.Brfalse_S || codeList[i].opcode == OpCodes.Brfalse)
                {
                    if (i > 0 && codeList[i - 1].opcode == OpCodes.Callvirt && 
                        codeList[i - 1].operand is MethodInfo method && 
                        method.Name == "get_Active")
                    {
                        var newInstructions = new List<CodeInstruction>();
                        
                        newInstructions.Add(new CodeInstruction(OpCodes.Callvirt, typeof(TSPlayer).GetMethod("get_Name")));
                        newInstructions.Add(new CodeInstruction(OpCodes.Call, typeof(HiddenPlayerPlugin).GetMethod("IsHiddenPlayer")));
                        newInstructions.Add(new CodeInstruction(OpCodes.Brtrue_S, codeList[i].operand));
                        
                        codeList.InsertRange(i, newInstructions);
                        i += newInstructions.Count;
                    }
                }
            }
            
            return codeList.AsEnumerable();
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
    }

    public class HiddenPlayerConfig
    {
        public List<string> HiddenPlayerNames { get; set; } = new List<string>();
    }
}
