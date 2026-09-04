using MidiPlayer.Config;
using MidiPlayer.Models;

namespace MidiPlayer.Core;

public class InstrumentMapper
{
    public static InstrumentMapper? Instance { get; set; }

    private readonly Dictionary<int, InstrumentType> _programLookup;
    private readonly Dictionary<int, int> _drumStyleLookup;
    private readonly List<(int Root, int[] Semitones, int Style)> _chordDefs;

    public InstrumentMapper(InstrumentMapConfig config)
    {
        _programLookup = config.ProgramLookup;
        _drumStyleLookup = config.DrumStyleLookup;
        _chordDefs = config.ChordDefinitions;
    }

    // === 音高映射（纯数学计算，不依赖配置） ===
    public const int MidiNoteC5 = 72;
    public const int MinMidi = 60;
    public const int MaxMidi = 84;
    public const int Octave = 12;
    public const float MaxPitch = 1.0f;

    // MIDI Note → pitchOffset：先通过加减八度把音符移入 60-84 范围，再计算 pitch
    // transpose：整曲移调量（半音），用于尽量让音域中心贴近 C5，减少逐音符折叠造成的八度信息丢失
    public static float MapPitch(int midiNote, int transpose = 0)
    {
        int adjusted = midiNote + transpose;
        while (adjusted < MinMidi)
            adjusted += Octave;
        while (adjusted > MaxMidi)
            adjusted -= Octave;

        float pitch = (adjusted - MidiNoteC5) / 12f;
        return Math.Clamp(pitch, -1.0f, 1.0f);
    }

    // === 乐器分配（实例方法，从配置读取） ===
    public InstrumentType AssignInstrumentImpl(int programNumber, bool isDrumTrack)
    {
        if (isDrumTrack)
            return InstrumentType.Drum;

        if (_programLookup.TryGetValue(programNumber, out var inst))
            return inst;

        return InstrumentType.Harp;
    }

    // === 鼓组映射（实例方法，从配置读取） ===
    public int MapDrumStyleImpl(int midiNote)
    {
        if (_drumStyleLookup.TryGetValue(midiNote, out var style))
            return style;

        return 146; // 默认 Bass Drum
    }

    // === 吉他和弦识别（实例方法，从配置读取） ===
    public int MapGuitarChordImpl(List<int> simultaneousNotes)
    {
        if (simultaneousNotes.Count < 3)
            return 133;

        var intervals = simultaneousNotes
            .Select(n => n % 12)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (intervals.Count == 0)
            return 133;

        int root = intervals[0];

        foreach (var (r, semitones, style) in _chordDefs)
        {
            if (root == r
                && intervals.Contains((r + semitones[0]) % 12)
                && intervals.Contains((r + semitones[1]) % 12))
            {
                return style;
            }
        }

        return 133; // 默认 C
    }

    // === 静态包装方法（保持 SongConverter 等调用方不变） ===
    public static InstrumentType AssignInstrument(int programNumber, bool isDrumTrack)
    {
        return Instance?.AssignInstrumentImpl(programNumber, isDrumTrack) ?? InstrumentType.Harp;
    }

    public static int MapDrumStyle(int midiNote)
    {
        return Instance?.MapDrumStyleImpl(midiNote) ?? 146;
    }

    public static int MapGuitarChord(List<int> simultaneousNotes)
    {
        return Instance?.MapGuitarChordImpl(simultaneousNotes) ?? 133;
    }
}