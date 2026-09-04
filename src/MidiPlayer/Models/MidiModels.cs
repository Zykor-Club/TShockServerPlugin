namespace MidiPlayer.Models;

// 五种泰拉瑞亚乐器类型
public enum InstrumentType
{
    Harp,       // 竖琴 - Item26
    Bell,       // 铃铛 - Item35
    GuitarAxe,  // 吉他斧 - Item47
    Drum,       // 鼓组 - 多Style 139-148
    Guitar      // 吉他和弦 - Style 133-138
}

// MIDI 解析后的原始数据
public class MidiData
{
    public int Bpm { get; set; }
    public List<MidiTrack> Tracks { get; set; } = new();
    public List<MidiNote> AllNotes { get; set; } = new();
    public long TotalDurationMs { get; set; }
}

// MIDI 轨道信息
public class MidiTrack
{
    public string Name { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public int ProgramNumber { get; set; }
    public bool IsDrumTrack { get; set; }
    public List<MidiNote> Notes { get; set; } = new();
}

// 单个 MIDI 音符（已转换毫秒）
public class MidiNote
{
    public long StartTimeMs { get; set; }
    public long DurationMs { get; set; }
    public int NoteNumber { get; set; }    // MIDI 音符号码 (0-127)
    public string NoteName { get; set; } = "";
    public int Octave { get; set; }
    public int Velocity { get; set; }      // 力度 (0-127)
    public int TrackIndex { get; set; }
    public float ExpressionMultiplier { get; set; } = 1.0f; // CC#7+#11 合并
    public List<int> ChordNoteNumbers { get; set; } = new();
}

// 转换后的泰拉瑞亚可播放音符
public class TerrariaNote
{
    public long StartTimeMs { get; set; }
    public InstrumentType Instrument { get; set; }
    public float PitchOffset { get; set; }
    public float Volume { get; set; }
    public int SoundStyle { get; set; }       // 用于鼓组和吉他的 style 编号
    public bool IsMelody { get; set; }        // 是否为主旋律
    public int OriginalNoteNumber { get; set; }
}

// 转换完成的歌曲
public class TerrariaSong
{
    public string FileName { get; set; } = "";
    public List<TerrariaNote> Notes { get; set; } = new();
    public long TotalDurationMs { get; set; }
}
