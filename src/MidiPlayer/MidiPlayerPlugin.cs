using System.Diagnostics;
using MidiPlayer.Config;
using MidiPlayer.Core;
using OTAPI;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace MidiPlayer;

[ApiVersion(2, 1)]
public class MidiPlayerPlugin : TerrariaPlugin
{
    public override string Name => "MidiPlayer";
    public override string Author => "主要开发：星梦; 协助开发：mountain; 灵感来源与开发指导：ruyou";
    public override string Description => "通过Terraria播放 MIDI 音乐......";
    public override Version Version => new Version(1, 4, 5, 5);

    private Commands.MidiCommands _midiCommands = null!;

    public MidiPlayerPlugin(Main game) : base(game) { }

    public override void Initialize()
    {
        // 1. 初始化 SoundID 索引映射
        if (SoundID.IndexByName == null)
        {
            SoundID.FillAccessMap();
            Console.WriteLine("[MidiPlayer] 已初始化 SoundID 访问映射");
        }
        SoundSender.Initialize();

        // 2. 加载配置
        LoadAndApplyConfig();

        // 3. 初始化八音盒绑定（读取数据库、订阅数据包/图格事件）
        MusicBoxBinding.Instance.Initialize();

        // 4. 注册 Hooks 和命令
        // 4.1 屏蔽客户端手弹乐器包，防止污染全局音高导致 MIDI 高音跑调
        Hooks.MessageBuffer.GetData += InstrumentSoundInterceptor.OnGetData;
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        Terraria.WorldGen.Hooks.OnWorldLoad += OnWorldLoad;
        GeneralHooks.ReloadEvent += OnReload;
        _midiCommands = new Commands.MidiCommands();
        _midiCommands.Register(TShockAPI.Commands.ChatCommands);

        Console.WriteLine("[MidiPlayer] 初始化完成");
    }

    private void LoadAndApplyConfig()
    {
        var config = MidiPlayerConfig.Load();
        Console.WriteLine($"[MidiPlayer] 配置已加载: MidiPlayer/MidiPlayerConfig.json");

        // 加载乐器映射表（首次自动生成默认配置）
        var instrumentMap = InstrumentMapConfig.Load();
        InstrumentMapper.Instance = new InstrumentMapper(instrumentMap);

        var songConverter = new SongConverter
        {
            EnableBellHarmony = config.EnableBellHarmony,
            EnableHarpReverb = config.EnableHarpReverb
        };
        config.ApplyTo(songConverter);

        PlayScheduler.Instance.Init(songConverter, config.LookAheadMs);
        PlayScheduler.Instance.MaxNotesPerTick = config.MaxNotesPerTick;
        PlayScheduler.Instance.SameStyleRetriggerMs = config.SameStyleRetriggerMs;
        InstrumentSoundInterceptor.Enabled = config.BlockInstrumentSound;
        InstrumentSoundInterceptor.WarnOnManualPlay = config.WarnOnManualPlay;
    }

    // 热重载配置（由 TShock /reload 触发）
    public void ReloadConfig()
    {
        LoadAndApplyConfig();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hooks.MessageBuffer.GetData -= InstrumentSoundInterceptor.OnGetData;
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            Terraria.WorldGen.Hooks.OnWorldLoad -= OnWorldLoad;
            GeneralHooks.ReloadEvent -= OnReload;
            _midiCommands.Unregister(TShockAPI.Commands.ChatCommands);
            MusicBoxBinding.Instance.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnGameUpdate(EventArgs args)
    {
        PlayScheduler.Instance.Update();
        MusicBoxBinding.Instance.CheckBindTimeout();
    }
    private void OnServerJoin(JoinEventArgs args) => PlayScheduler.Instance.OnPlayerJoin(args.Who);
    private void OnServerLeave(LeaveEventArgs args) => PlayScheduler.Instance.OnPlayerLeave(args.Who);
    private static void OnWorldLoad() => MusicBoxBinding.Instance.OnWorldLoad();
    private void OnReload(ReloadEventArgs args) => ReloadConfig();
}
