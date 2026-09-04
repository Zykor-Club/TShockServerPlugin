using Microsoft.Xna.Framework;
using OTAPI;
using Terraria;
using TShockAPI;

namespace MidiPlayer.Core;

/// <summary>
/// 屏蔽客户端手弹乐器产生的 InstrumentSound(58) 数据包，防止其污染全局音高 Main.musicPitch，
/// 从而避免 MIDI 播放时同一乐器的音高被"限制/跑调"。
///
/// 原理：客户端渲染竖琴(26)/铃铛(35)/吉他斧(47)时，音高一律取自全局单值 Main.musicPitch
/// （见 LegacySoundPlayer，这三件乐器无视显式音高）。任何一次手弹都会把 Main.musicPitch
/// 改写为对应音高并常驻，导致后续 MIDI 音符被错误压到该音高附近，直到重启客户端才恢复。
///
/// 有效范围：丢弃"客户端→服务器→其它客户端"的 58 转发，可彻底挡住"听到他人手弹"的污染；
/// 但玩家在本地亲自弹奏时，音高改写发生在客户端本地、不经服务器，服务器无法拦截，
/// 只能给弹奏者本人弹提示，并视情形建议不要在 MIDI 播放期间手弹这几种乐器。
/// </summary>
public static class InstrumentSoundInterceptor
{
    private const byte InstrumentSoundPacketId = 58;

    // 由配置开关控制；可通过 /reload 或配置文件热更新
    public static bool Enabled = true;

    /// <summary>手弹乐器时是否给弹奏者本人弹提示（防止 MIDI 音高污染）。</summary>
    public static bool WarnOnManualPlay = true;

    // 日志节流，避免他人连续手弹时刷屏
    private static long _lastLogMs;

    // 每玩家提示节流：避免快速连弹时反复弹提示刷屏
    private static readonly Dictionary<int, long> _lastWarnMs = new();

    private const long WarnCooldownMs = 8000;   // 同一玩家两次提示的最小间隔

    public static void OnGetData(object? sender, Hooks.MessageBuffer.GetDataEventArgs e)
    {
        if (!Enabled)
            return;
        if (e.PacketId != InstrumentSoundPacketId)
            return;

        int whoAmI = e.Instance.whoAmI;
        string name = whoAmI >= 0 && whoAmI < Main.maxPlayers && Main.player[whoAmI]?.active == true
            ? Main.player[whoAmI].name
            : $"索引{whoAmI}";

        long now = Environment.TickCount64;

        // 丢弃该包：不进入 TShock 逻辑，也不转发给其它客户端。
        // 对"他人手弹经服务器转发"有效；对弹奏者本人本地已改的音高无能为力。
        e.Result = OTAPI.HookResult.Cancel;

        // 播放前弹奏同样会影响后续播放效果，因此任何一次手弹都提示（做过节流防刷屏）
        if (WarnOnManualPlay && now - GetLastWarn(whoAmI) >= WarnCooldownMs)
        {
            SetLastWarn(whoAmI, now);
            TShock.Players[whoAmI]?.SendMessage(
                $"[i:4080] [c/66CCFF:MidiPlay] [i:4080] [c/FFAA66:请勿手弹竖琴/铃铛/吉他斧] [c/AAAAAA:否则会影响MIDI播放的音高还原(播放前后皆受影响)]",
                Color.OrangeRed);
        }

        if (now - _lastLogMs >= 5000)
        {
            _lastLogMs = now;
            TShock.Log.ConsoleInfo(
                $"[MidiPlayer] 已屏蔽玩家 \"{name}\" 的手弹乐器包(InstrumentSound#58)，" +
                (IsMidiAudibleTo(whoAmI)
                    ? "该玩家正在收听MIDI，已弹出提示。"
                    : "防止其污染全局音高导致 MIDI 高音跑调。"));
        }
    }

    private static bool IsMidiAudibleTo(int playerIndex)
    {
        // 个人模式：仅该玩家自己的歌曲正在播放
        if (PlayScheduler.Instance.IsPlaying(playerIndex))
            return true;

        // 全服模式：任一全局歌曲正在播放，则所有在线玩家都在收听
        return PlayScheduler.Instance.IsGlobalMode
            && PlayScheduler.Instance.GetGlobalCurrentSongName() != null;
    }

    private static long GetLastWarn(int playerIndex)
    {
        lock (_lastWarnMs) return _lastWarnMs.TryGetValue(playerIndex, out long v) ? v : 0;
    }

    private static void SetLastWarn(int playerIndex, long now)
    {
        lock (_lastWarnMs) _lastWarnMs[playerIndex] = now;
    }
}