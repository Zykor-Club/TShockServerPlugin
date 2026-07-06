using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace AIAgent;

[ApiVersion(2, 1)]
public class AIAgentPlugin : TerrariaPlugin
{
    public override string Name => "AIAgent";
    public override string Author => "AI、星梦";
    public override string Description => "智能AI助手，支持多种AI平台对接";
    public override Version Version => new Version(1, 0, 5);

    public AIAgentPlugin(Main game) : base(game) { }

    public override void Initialize()
    {
        ConfigManager.LoadConfig();
        ConfigManager.LoadTokenStats();

        Commands.ChatCommands.Add(new Command("aiagent.use", HandleAIGCommand, "aig")
        {
            HelpText = "AIAgent 智能助手主命令"
        });

        ServerApi.Hooks.ServerChat.Register(this, OnServerChat);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        GetDataHandlers.KillMe += OnPlayerDeath;

        // 检测 PlayerMail 插件
        Task.Run(async () =>
        {
            await Task.Delay(5000);
            ChatUtils.TryDetectPlayerMailPlugin();
        });

        // 启动主动聊天定时器
        if (PluginState.Config.EnableAutoChat)
        {
            PluginState.AutoChatTimer = new System.Threading.Timer(
                _ =>
                {
                    try { ProcessAutoChatQueue(); }
                    catch (Exception ex)
                    {
                        TShock.Log.Error($"[AIAgent] 主动聊天定时器异常: {ex.Message}");
                    }
                },
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));
        }

        TShock.Log.Info("[AIAgent] 智能AI助手插件已加载 v1.0.5");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PluginState.Cts.Cancel();
            PluginState.Cts.Dispose();

            ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            GetDataHandlers.KillMe -= OnPlayerDeath;

            PluginState.AutoChatTimer?.Dispose();
            ConfigManager.SaveTokenStats();
        }
        base.Dispose(disposing);
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null || !plr.RealPlayer) return;

        var session = PluginState.Sessions.GetOrAdd(plr.Name, _ => new Session());
        lock (session.HistoryLock)
        {
            session.History.Clear();
        }
        session.TotalTokens = 0;
        session.RequestCount = 0;
    }

    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;

        PluginState.Sessions.TryRemove(plr.Name, out _);
        ConfigManager.SaveTokenStats();
    }

    private void OnServerChat(ServerChatEventArgs args)
    {
        if (args.Handled) return;
        var plr = TShock.Players[args.Who];
        if (plr == null || !plr.RealPlayer) return;
        if (!plr.HasPermission("aiagent.use")) return;

        var cfg = PluginState.Config;
        if (cfg.AllowedPlayers != null && cfg.AllowedPlayers.Count > 0 &&
            !cfg.AllowedPlayers.Contains(plr.Name))
            return;

        var text = args.Text;
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("/") || text.StartsWith(".")) return;

        if (cfg.EnableAutoChat)
        {
            PluginState.AutoChatQueue.Enqueue(new AutoChatEvent
            {
                PlayerName = plr.Name,
                EventType = "chat",
                Content = text,
                Time = DateTime.Now
            });

            while (PluginState.AutoChatQueue.Count > cfg.AutoChatMaxQueue)
                PluginState.AutoChatQueue.TryDequeue(out _);
        }
    }

    private void OnPlayerDeath(object? sender, GetDataHandlers.KillMeEventArgs args)
    {
        var plr = TShock.Players[args.PlayerId];
        if (plr == null || !plr.RealPlayer) return;
        if (!plr.HasPermission("aiagent.use")) return;

        var cfg = PluginState.Config;
        if (!cfg.EnableAutoChat) return;

        PluginState.AutoChatQueue.Enqueue(new AutoChatEvent
        {
            PlayerName = plr.Name,
            EventType = "death",
            Content = $"{plr.Name} 死亡了",
            Time = DateTime.Now
        });
    }

    private void ProcessAutoChatQueue()
    {
        var cfg = PluginState.Config;
        if (!cfg.EnableAutoChat) return;
        if (PluginState.AutoChatQueue.IsEmpty) return;

        var now = DateTime.Now;
        if ((now - PluginState.LastGlobalAutoChatTime).TotalSeconds < cfg.AutoChatInterval)
            return;

        if (!PluginState.AutoChatQueue.TryDequeue(out var evt)) return;

        if (PluginState.LastAutoChatTime.TryGetValue(evt.PlayerName, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < cfg.AutoChatPlayerCooldown)
                return;
        }

        var plr = TShock.Players.FirstOrDefault(p => p?.Name == evt.PlayerName);
        if (plr == null) return;

        var session = PluginState.Sessions.GetOrAdd(plr.Name, _ => new Session());

        lock (session.HistoryLock)
        {
            if (!session.History.Any())
            {
                session.History.Add(new ChatMessage
                {
                    Role = "system",
                    Content = ChatUtils.BuildSystemPrompt(plr.Name)
                });
            }

            session.History.Add(new ChatMessage
            {
                Role = "user",
                Content = $"[系统事件] {evt.EventType}: {evt.Content}"
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var resp = await AIHandler.HandleAutoChat(session);
                if (!string.IsNullOrWhiteSpace(resp))
                {
                    TSPlayer.All.SendInfoMessage($"[c/FF69B4:[{cfg.AIName}]] {resp}");
                    PluginState.LastAutoChatTime[evt.PlayerName] = DateTime.Now;
                    PluginState.LastGlobalAutoChatTime = DateTime.Now;

                    if (cfg.EnableContextMemory)
                    {
                        lock (session.HistoryLock)
                        {
                            session.History.Add(new ChatMessage { Role = "assistant", Content = resp });
                        }
                    }
                }
            }
            catch { }
        });
    }

    private void HandleAIGCommand(CommandArgs args)
    {
        var plr = args.Player;
        if (plr == null) return;

        if (!plr.HasPermission("aiagent.use"))
        {
            plr.SendErrorMessage("[AIAgent] 你没有使用权限，需要 aiagent.use 权限。");
            return;
        }

        var cfg = PluginState.Config;
        if (cfg.AllowedPlayers != null && cfg.AllowedPlayers.Count > 0 &&
            !cfg.AllowedPlayers.Contains(plr.Name))
        {
            plr.SendErrorMessage("[AIAgent] 你不在AI使用白名单中。");
            return;
        }

        if (args.Parameters.Count == 0)
        {
            CommandHandler.SendHelp(plr);
            return;
        }

        var first = args.Parameters[0].ToLower();

        // 设置命令
        if (first == "set")
        {
            CommandHandler.HandleSet(plr, args);
            return;
        }

        // 模型管理
        if (first == "model")
        {
            CommandHandler.HandleModel(plr, args);
            return;
        }

        // 统计
        if (first == "stats")
        {
            CommandHandler.HandleStats(plr, args);
            return;
        }

        // 白名单
        if (first == "whitelist")
        {
            CommandHandler.HandleWhitelist(plr, args);
            return;
        }

        // 人设
        if (first == "persona")
        {
            CommandHandler.HandlePersona(plr, args);
            return;
        }

        // 关键词
        if (first == "keywords")
        {
            CommandHandler.HandleKeywords(plr, args);
            return;
        }

        // 清空统计
        if (first == "clearstats")
        {
            if (!plr.HasPermission("aiagent.admin"))
            {
                plr.SendErrorMessage("[AIAgent] 需要 aiagent.admin 权限。");
                return;
            }
            PluginState.TokenStats.Clear();
            ConfigManager.SaveTokenStats();
            plr.SendSuccessMessage("[AIAgent] 所有Token统计数据已清空。");
            return;
        }

        // say 长文本
        if (first == "say")
        {
            if (args.Parameters.Count < 2)
            {
                plr.SendErrorMessage("[AIAgent] 用法: /aig say <内容>");
                return;
            }
            var content = string.Join(" ", args.Parameters.Skip(1));
            if (content.StartsWith("/"))
            {
                plr.SendErrorMessage("[AIAgent] 请不要向AI发送游戏命令，如需与AI对话请直接输入文本内容。");
                return;
            }
            _ = HandleAIRequest(plr, content);
            return;
        }

        // 默认：直接对话
        var directContent = string.Join(" ", args.Parameters);
        if (directContent.StartsWith("/"))
        {
            plr.SendErrorMessage("[AIAgent] 请不要向AI发送游戏命令，如需与AI对话请直接输入文本内容。");
            return;
        }
        _ = HandleAIRequest(plr, directContent);
    }

    private async Task HandleAIRequest(TSPlayer plr, string content)
    {
        var session = PluginState.Sessions.GetOrAdd(plr.Name, _ => new Session());
        await AIHandler.HandleAI(plr, session, content);
    }
}
