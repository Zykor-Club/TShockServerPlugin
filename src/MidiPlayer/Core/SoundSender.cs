using Microsoft.Xna.Framework;
using MidiPlayer.Models;
using Terraria;
using Terraria.ID;

namespace MidiPlayer.Core;

public class SoundSender
{
    // 从 SoundID.IndexByName 初始化乐器 soundIndex 映射
    public static void Initialize()
    {
        Console.WriteLine("[MidiPlayer] === 乐器SoundIndex映射 ===");
        foreach (InstrumentType instrument in Enum.GetValues<InstrumentType>())
        {
            if (InstrumentSoundIndex.ContainsKey(instrument)) continue;
            string soundName = GetSoundName(instrument);
            if (SoundID.IndexByName != null && SoundID.IndexByName.TryGetValue(soundName, out ushort index))
            {
                InstrumentSoundIndex[instrument] = index;
            }
            else
            {
                ushort fallback = instrument switch
                {
                    InstrumentType.Harp => 26,
                    InstrumentType.Bell => 35,
                    InstrumentType.GuitarAxe => 47,
                    InstrumentType.Guitar => 47,
                    InstrumentType.Drum => 60,
                    _ => 26
                };
                InstrumentSoundIndex[instrument] = fallback;
            }
            Console.WriteLine($"[MidiPlayer] {instrument}: soundIndex={InstrumentSoundIndex[instrument]}");
        }
        Console.WriteLine("[MidiPlayer] =========================");
    }

    private static string GetSoundName(InstrumentType instrument) => instrument switch
    {
        InstrumentType.Harp => "Item26",
        InstrumentType.Bell => "Item35",
        InstrumentType.GuitarAxe => "Item47",
        InstrumentType.Guitar => "Item47",
        InstrumentType.Drum => "Item60",
        _ => "Item26"
    };

    private static readonly Dictionary<InstrumentType, ushort> InstrumentSoundIndex = new();

    private readonly object _lock = new();
    private readonly HashSet<int> _failedClients = new();
    private readonly HashSet<int> _silentPlayers = new();
    private readonly Random _rng = new();
    private readonly Dictionary<int, Dictionary<int, StyleSendState>> _lastStyleSent = new();

    public int SameStyleRetriggerMs { get; set; } = 35;

    public void SendNote(int playerIndex, TerrariaNote note, Vector2 position, float volumeMultiplier)
    {
        lock (_lock)
        {
            if (_failedClients.Contains(playerIndex))
                return;

            try
            {
                var player = Main.player[playerIndex];
                if (player == null || !player.active)
                {
                    _failedClients.Add(playerIndex);
                    return;
                }

                ushort soundIndex = InstrumentSoundIndex.TryGetValue(note.Instrument, out ushort idx) ? idx : (ushort)26;

                int soundStyle = note.Instrument switch
                {
                    InstrumentType.Drum or InstrumentType.Guitar => note.SoundStyle,
                    _ => note.SoundStyle
                };

                float pitchOffset = note.PitchOffset;
                if (pitchOffset == -1f)
                    pitchOffset = -0.99f;
                pitchOffset = Math.Clamp(pitchOffset, -0.99f, 1f);

                float sendVolume = Math.Clamp(note.Volume * volumeMultiplier, 0f, 1f);

                // Guitar chords and drums only keep one client instance per style.
                if (IsNonOverlapStyle(soundStyle) && !CanRetrigger(playerIndex, soundStyle, sendVolume))
                    return;

                // Small deterministic spatial offset for panning variety.
                // Client instance truncation is handled by the retrigger gate above.
                float microAngle = note.OriginalNoteNumber * 2.828f;
                float microRadius = 8f;
                Vector2 microOffset = new(
                    microRadius * MathF.Cos(microAngle),
                    microRadius * MathF.Sin(microAngle));

                // 额外 ±1 随机防抖：防止同音高+同位置+同时刻的两个音符完全重合
                microOffset.X += _rng.Next(-1, 2);
                microOffset.Y += _rng.Next(-1, 2);

                var soundInfo = new NetMessage.NetSoundInfo(
                    position: position + microOffset,
                    soundIndex: soundIndex,
                    style: soundStyle,
                    volume: sendVolume,
                    pitchOffset: pitchOffset
                );

                NetMessage.PlayNetSound(soundInfo, remoteClient: playerIndex);

                if (IsNonOverlapStyle(soundStyle))
                    RecordSent(playerIndex, soundStyle, sendVolume);
            }
            catch
            {
                _failedClients.Add(playerIndex);
            }
        }
    }

    public void Reset(int playerIndex)
    {
        lock (_lock)
        {
            _failedClients.Remove(playerIndex);
            _lastStyleSent.Remove(playerIndex);
        }
    }

    // 重置所有玩家发声状态，不清除静默状态
    public void ResetAll()
    {
        lock (_lock)
        {
            _failedClients.Clear();
            _lastStyleSent.Clear();
        }
    }

    public void SetSilent(int playerIndex, bool silent)
    {
        lock (_lock)
        {
            if (silent)
                _silentPlayers.Add(playerIndex);
            else
                _silentPlayers.Remove(playerIndex);
        }
    }

    public bool IsSilent(int playerIndex)
    {
        lock (_lock) return _silentPlayers.Contains(playerIndex);
    }

    public void ClearSilent(int playerIndex)
    {
        lock (_lock) _silentPlayers.Remove(playerIndex);
    }

    private static bool IsNonOverlapStyle(int soundStyle)
    {
        return soundStyle is >= 133 and <= 148;
    }

    private bool CanRetrigger(int playerIndex, int soundStyle, float sendVolume)
    {
        if (!_lastStyleSent.TryGetValue(playerIndex, out var styles))
            return true;
        if (!styles.TryGetValue(soundStyle, out var last))
            return true;

        long delta = Environment.TickCount64 - last.SentMs;
        return delta >= SameStyleRetriggerMs || sendVolume > last.Volume * 1.15f;
    }

    private void RecordSent(int playerIndex, int soundStyle, float sendVolume)
    {
        if (!_lastStyleSent.TryGetValue(playerIndex, out var styles))
            _lastStyleSent[playerIndex] = styles = new Dictionary<int, StyleSendState>();

        styles[soundStyle] = new StyleSendState
        {
            SentMs = Environment.TickCount64,
            Volume = sendVolume
        };
    }

    private sealed class StyleSendState
    {
        public long SentMs;
        public float Volume;
    }
}
