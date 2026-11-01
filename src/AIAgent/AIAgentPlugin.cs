using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using AIAgent.Channels;
using AIAgent.Chat;
using AIAgent.Commands;
using AIAgent.Config;
using AIAgent.Core;
using AIAgent.Memory;
using AIAgent.Tools;
using AIAgent.Tools.Builtin;
using AIAgent.Wiki;

namespace AIAgent;

[ApiVersion(2, 1)]
public sealed class AIAgentPlugin : TerrariaPlugin
{
    private CancellationTokenSource? _idleCts;
    private int _tick;

    public AIAgentPlugin(Main game) : base(game)
    {
    }

    public override string Name => "AIAgent";

    public override Version Version => new(2, 0, 0);

    public override string Author => "星梦";

    public override string Description => "服务器 AI 助手：会聊天、有记忆、能安全调用游戏内工具";

    public override void Initialize()
    {
        PluginState.DataDirectory = Path.Combine(TShock.SavePath, "AIAgent");
        PluginLog.Initialize(PluginState.DataDirectory);
        EmbeddedAssets.Ensure(PluginState.DataDirectory);
        PluginState.Store = new ConfigStore(PluginState.DataDirectory);
        PluginState.Config = PluginState.Store.Load();

        PluginState.Sessions = new SessionStore();
        PluginState.Memory = new JsonMemoryStore(Path.Combine(PluginState.DataDirectory, "memory"));
        PluginState.Extractor = new MemoryExtractor(PluginState.Memory);
        PluginState.Compressor = new SessionCompressor();
        SessionPersistence.Load(PluginState.DataDirectory);
        PluginState.Persona = LoadPersona();
        PluginState.Wiki = LoadWiki();
        GreetingService.EnsureLoaded();

        var registry = new ToolRegistry();
        registry.Register(new StatusTool());
        registry.Register(new WorldInfoTool());
        registry.Register(new HealTool());
        registry.Register(new GiveItemTool());
        registry.Register(new TeleportTool());
        registry.Register(new SetTimeTool());
        registry.Register(new WikiTool());
        registry.Register(new RecipeTool());
        registry.Register(new ProgressTool());
        registry.Register(new FindRecipeTool());
        registry.Register(new WeatherTool());
        registry.Register(new BringPlayerTool());
        registry.Register(new RunCommandTool());
        registry.Register(new ListCommandsTool());
        registry.Register(new AvatarStatusTool());
        registry.Register(new AvatarSayTool());
        registry.Register(new AvatarFollowTool());
        registry.Register(new AvatarGotoTool());
        registry.Register(new AvatarAttackTool());
        registry.Register(new AvatarScanTool());
        registry.Register(new AvatarGuardTool());
        registry.Register(new AvatarPatrolTool());
        registry.Register(new AvatarReturnTool());
        registry.Register(new AvatarStopTool());
        PluginState.Tools = new ToolInvoker(registry);

        TShockAPI.Commands.ChatCommands.Add(new Command("aiagent.use", OnAig, "aig")
        {
            HelpText = "AIAgent 服务器智能助手",
            AllowServer = true,
            DoLog = false,
        });

        GeneralHooks.ReloadEvent += OnReload;
        ServerApi.Hooks.ServerJoin.Register(this, OnJoin);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
        ServerApi.Hooks.NpcStrike.Register(this, BossChannel.OnStrike);
        ServerApi.Hooks.NpcKilled.Register(this, BossChannel.OnNpcKilled);
        ServerApi.Hooks.GamePostUpdate.Register(this, OnPostUpdate);
        PlayerHooks.PlayerChat += GameChatChannel.OnPlayerChat;
        PlayerHooks.PlayerPostLogin += OnPostLogin;
        PlayerHooks.PlayerLogout += OnLogout;

        _idleCts = new CancellationTokenSource();
        _ = Task.Run(() => IdleLoopAsync(_idleCts.Token));

        PluginLog.Info($"v{AigCommandRouter.Version} 已加载 | 配置 {PluginState.ConfigPath} | " +
                       $"供应商 {PluginState.Config.Providers.List.Count} 个 | 模型 {PluginState.Config.Model} | " +
                       $"记忆 {(PluginState.Config.Memory.Enabled ? "开" : "关")} | 工具 {registry.All.Count} 个");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;

            GeneralHooks.ReloadEvent -= OnReload;
            ServerApi.Hooks.ServerJoin.Deregister(this, OnJoin);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
            ServerApi.Hooks.NpcStrike.Deregister(this, BossChannel.OnStrike);
            ServerApi.Hooks.NpcKilled.Deregister(this, BossChannel.OnNpcKilled);
            ServerApi.Hooks.GamePostUpdate.Deregister(this, OnPostUpdate);
            PlayerHooks.PlayerChat -= GameChatChannel.OnPlayerChat;
            PlayerHooks.PlayerPostLogin -= OnPostLogin;
            PlayerHooks.PlayerLogout -= OnLogout;

            try
            {
                PluginLog.FlushMirror(1000);
                SessionPersistence.Save(PluginState.DataDirectory);
                PluginState.Store.Save(PluginState.Config);
                PluginLog.Info("退出前已保存配置与会话");
            }
            catch (Exception ex)
            {
                PluginLog.Error("退出前保存配置失败", ex);
            }
        }

        base.Dispose(disposing);
    }

    private void OnAig(CommandArgs args) => AigCommandRouter.Handle(args);

    private void OnPostUpdate(EventArgs args)
    {
        PluginState.MainThread.Pump(32);
        PluginLog.FlushMirror();

        _tick++;
        if (_tick % 60 == 0)
        {
            Crafting.WorldProgress.RefreshCache();
            ProactiveService.Tick(DateTime.Now);
        }
        if (_tick % 15 != 0) return;
        try
        {
            GameChatChannel.PollDeaths();
            BossChannel.Poll();
        }
        catch (Exception ex)
        {
            PluginLog.Error("主循环轮询异常", ex);
        }
    }

    private void OnJoin(JoinEventArgs args) => PluginState.Players.OnJoin(args.Who);

    private void OnGreet(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        PluginState.Players.OnGreet(args.Who, player?.Name);
        if (player is not null) GreetingService.OnPlayerGreeted(player);
    }

    private void OnLeave(LeaveEventArgs args)
    {
        var gone = PluginState.Players.Remove(args.Who);
        if (gone is null) return;

        GameChatChannel.ResetPlayer(args.Who);

        var account = string.IsNullOrWhiteSpace(gone.Value.AccountName) ? gone.Value.DisplayName : gone.Value.AccountName;
        PluginLog.Info($"玩家离开 #{args.Who} {gone.Value.DisplayName}（{account}）");

        if (!string.IsNullOrWhiteSpace(account))
        {
            CareService.ResetPlayer(account);
            _ = MemoryService.FinalizeAsync(account, gone.Value.DisplayName);
        }
    }

    private void OnPostLogin(PlayerPostLoginEventArgs args) => PluginState.Players.OnPostLogin(args.Player);

    private void OnLogout(PlayerLogoutEventArgs args) => PluginState.Players.OnLogout(args.Player);

    private void OnReload(ReloadEventArgs args)
    {
        PluginState.Config = PluginState.Store.Load();
        PluginState.Persona = LoadPersona();
        PluginState.Wiki = LoadWiki();
        PluginLog.Info($"配置已重新加载 | 模型 {PluginState.Config.Model}");
        args.Player?.SendSuccessMessage("[AIAgent] 配置已重新加载");
    }

    private static Wiki.IWikiSource LoadWiki()
    {
        var cfg = PluginState.Config.Wiki;
        var json = Path.Combine(PluginState.DataDirectory, "wiki.json");
        var db = Path.Combine(PluginState.DataDirectory, cfg.Database);
        var engine = (cfg.Engine ?? "json").Trim().ToLowerInvariant();

        if (engine is "sqlite" or "auto")
        {
            try
            {
                if (!File.Exists(db) && engine == "sqlite" && File.Exists(json))
                {
                    var built = Wiki.WikiDatabase.Build(json, db);
                    PluginLog.Info($"知识库已建库: {built} 页 → {db}");
                }

                if (File.Exists(db))
                {
                    return Wiki.SqliteWikiStore.Open(db);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error("SQLite 知识库加载失败，回退 JSON", ex);
            }
        }

        return Wiki.WikiStore.Load(json);
    }

    private static string LoadPersona()
    {
        var path = Path.Combine(PluginState.DataDirectory, "persona.md");
        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path).Trim();
                if (text.Length > 0) return text;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"读取人设文件失败: {path}", ex);
            }
        }

        if (!string.IsNullOrWhiteSpace(PluginState.Config.Persona))
        {
            return PluginState.Config.Persona;
        }

        // 文件和配置都为空时的兜底，至少保证有个角色
        return "你是服务器里的助手，说话简短、自然、有点俏皮；不要自称 AI 模型。";
    }

    private async Task IdleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                MemoryService.FinalizeIdle(PluginState.Config.Memory.IdleMinutes);
            }
            catch (Exception ex)
            {
                PluginLog.Error("空闲记忆整理失败", ex);
            }
        }
    }
}
