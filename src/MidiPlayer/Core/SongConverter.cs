using MidiPlayer.Models;

namespace MidiPlayer.Core;

public class SongConverter
{
    // 每乐器音量校准表（可通过配置文件 MidiPlayerConfig.json 修改）
    public static readonly Dictionary<InstrumentType, float> VolumeCalibration = new()
    {
        { InstrumentType.Harp,      1.33f },
        { InstrumentType.Bell,      1.20f },
        { InstrumentType.GuitarAxe, 1.13f },
        { InstrumentType.Drum,      0.50f },
        { InstrumentType.Guitar,    0.45f },
    };

    public bool EnableBellHarmony { get; set; } = true;
    public bool EnableHarpReverb { get; set; } = true;
    public bool EnableNoteMerge { get; set; } = true;
    public bool EnableNoteLimit { get; set; } = true;

    // 自动微调总开关：开启后仅对“需微调歌曲”列表中的曲子做整曲移调，其余曲子保持原样
    public bool EnableAutoTranspose { get; set; } = false;

    // 手动移调微调量（半音）：叠加在自动计算上，仅作用于需微调的歌曲
    public int ManualTransposeOffset { get; set; } = 0;

    // 需微调歌曲的文件名列表（含 .mid 后缀），与 MidiPlayerConfig 中的列表共享引用
    public List<string> AutoTransposeSongs { get; set; } = new();

    // 将 MidiData 转换为可播放的 TerrariaSong
    public TerrariaSong Convert(MidiData data, string fileName)
    {
        var melodyTracks = MelodyDetector.DetectMelodyTracks(data.Tracks, data.AllNotes);
        var selectedTracks = SelectPriorityTracks(data, melodyTracks);

        var notes = new List<TerrariaNote>(data.AllNotes.Count * 2);
        var filteredNotes = data.AllNotes
            .Where(n => selectedTracks.Contains(n.TrackIndex))
            .ToList();

        // 仅对“需微调歌曲”列表中的曲子做整曲移调；其余曲子保持原样（transpose = 0）
        bool shouldAuto = EnableAutoTranspose && AutoTransposeSongs.Contains(fileName);
        int transpose = shouldAuto
            ? ComputeConditionalTranspose(filteredNotes, data) + ManualTransposeOffset
            : 0;

        foreach (var midiNote in filteredNotes)
        {
            bool isMelody = melodyTracks.Contains(midiNote.TrackIndex);
            var track = data.Tracks[midiNote.TrackIndex];
            var instrument = InstrumentMapper.AssignInstrument(track.ProgramNumber, track.IsDrumTrack);

            switch (instrument)
            {
                case InstrumentType.Drum:
                    notes.Add(new TerrariaNote
                    {
                        StartTimeMs = midiNote.StartTimeMs,
                        Instrument = InstrumentType.Drum,
                        PitchOffset = 0,
                        Volume = CalibrateVolume(InstrumentType.Drum,
                            VelocityCurve(midiNote.Velocity), midiNote.ExpressionMultiplier),
                        SoundStyle = InstrumentMapper.MapDrumStyle(midiNote.NoteNumber),
                        IsMelody = false,
                        OriginalNoteNumber = midiNote.NoteNumber
                    });
                    break;

                case InstrumentType.Guitar:
                    var chordNotes = midiNote.ChordNoteNumbers.Count > 0
                        ? midiNote.ChordNoteNumbers
                        : filteredNotes
                            .Where(n => n.TrackIndex == midiNote.TrackIndex
                                && Math.Abs(n.StartTimeMs - midiNote.StartTimeMs) <= 50)
                            .Select(n => n.NoteNumber)
                            .ToList();

                    int chordStyle = InstrumentMapper.MapGuitarChord(chordNotes);

                    if (midiNote.NoteNumber == chordNotes.Min())
                    {
                        notes.Add(new TerrariaNote
                        {
                            StartTimeMs = midiNote.StartTimeMs,
                            Instrument = InstrumentType.Guitar,
                            PitchOffset = 0,
                            Volume = CalibrateVolume(InstrumentType.Guitar,
                                VelocityCurve(midiNote.Velocity), midiNote.ExpressionMultiplier),
                            SoundStyle = chordStyle,
                            IsMelody = isMelody,
                            OriginalNoteNumber = midiNote.NoteNumber
                        });
                    }
                    break;

                case InstrumentType.Harp:
                {
                    float baseVol = VelocityCurve(midiNote.Velocity);
                    float harpPitch = InstrumentMapper.MapPitch(midiNote.NoteNumber, transpose);
                    float harpVolume = CalibrateVolume(InstrumentType.Harp,
                        baseVol, midiNote.ExpressionMultiplier);

                    notes.Add(new TerrariaNote
                    {
                        StartTimeMs = midiNote.StartTimeMs,
                        Instrument = InstrumentType.Harp,
                        PitchOffset = harpPitch,
                        Volume = harpVolume,
                        SoundStyle = 26,
                        IsMelody = isMelody,
                        OriginalNoteNumber = midiNote.NoteNumber
                    });

                    // Bell 同奏
                    if (EnableBellHarmony && harpPitch > 0.5f)
                    {
                        notes.Add(new TerrariaNote
                        {
                            StartTimeMs = midiNote.StartTimeMs,
                            Instrument = InstrumentType.Bell,
                            PitchOffset = harpPitch,
                            Volume = Math.Clamp(harpVolume * 0.8f, 0, 1),
                            SoundStyle = 35,
                            IsMelody = isMelody,
                            OriginalNoteNumber = midiNote.NoteNumber
                        });
                    }

                    // 竖琴残响
                    if (EnableHarpReverb)
                    {
                        notes.Add(new TerrariaNote
                        {
                            StartTimeMs = midiNote.StartTimeMs + 50,
                            Instrument = InstrumentType.Harp,
                            PitchOffset = harpPitch,
                            Volume = Math.Clamp(harpVolume * 0.25f, 0, 1),
                            SoundStyle = 26,
                            IsMelody = isMelody,
                            OriginalNoteNumber = midiNote.NoteNumber
                        });
                    }
                    break;
                }

                case InstrumentType.Bell:
                {
                    float baseVol = VelocityCurve(midiNote.Velocity);
                    float bellVolume = CalibrateVolume(InstrumentType.Bell,
                        baseVol, midiNote.ExpressionMultiplier);
                    notes.Add(new TerrariaNote
                    {
                        StartTimeMs = midiNote.StartTimeMs,
                        Instrument = InstrumentType.Bell,
                        PitchOffset = InstrumentMapper.MapPitch(midiNote.NoteNumber, transpose),
                        Volume = bellVolume,
                        SoundStyle = 35,
                        IsMelody = isMelody,
                        OriginalNoteNumber = midiNote.NoteNumber
                    });
                    break;
                }

                case InstrumentType.GuitarAxe:
                {
                    float baseVol = VelocityCurve(midiNote.Velocity);
                    float gv = CalibrateVolume(InstrumentType.GuitarAxe,
                        baseVol, midiNote.ExpressionMultiplier);
                    notes.Add(new TerrariaNote
                    {
                        StartTimeMs = midiNote.StartTimeMs,
                        Instrument = InstrumentType.GuitarAxe,
                        PitchOffset = InstrumentMapper.MapPitch(midiNote.NoteNumber, transpose),
                        Volume = gv,
                        SoundStyle = 47,
                        IsMelody = isMelody,
                        OriginalNoteNumber = midiNote.NoteNumber
                    });
                    break;
                }
            }
        }

        notes = notes.OrderBy(n => n.StartTimeMs).ToList();

        // Style-aware voice reduction:
        // - 26/35/47 allow client-side overlap, so keep chords aligned.
        // - 133-148 are one-instance-per-style, so only keep the loudest per style.
        notes = OptimizeForClientVoices(notes);

        return new TerrariaSong
        {
            FileName = fileName,
            Notes = notes,
            TotalDurationMs = data.TotalDurationMs
        };
    }

    // 整曲微调移调量计算：为需微调的歌曲挑选一个整体移调量，使绝大多数音符落在 60-84 音域内、
    // 大幅减少逐音符折叠造成的八度丢失；音域内音符本就无折叠时自然返回 0。
    private int ComputeConditionalTranspose(List<MidiNote> filteredNotes, MidiData data)
    {
        var pitchNotes = filteredNotes
            .Where(n =>
            {
                var track = data.Tracks[n.TrackIndex];
                var inst = InstrumentMapper.AssignInstrument(track.ProgramNumber, track.IsDrumTrack);
                return inst is InstrumentType.Harp or InstrumentType.Bell or InstrumentType.GuitarAxe;
            })
            .ToList();

        if (pitchNotes.Count == 0)
            return 0;

        int bestTranspose = 0;
        double bestCost = double.PositiveInfinity;

        for (int t = -InstrumentMapper.Octave; t <= InstrumentMapper.Octave; t++)
        {
            double cost = 0;
            foreach (var n in pitchNotes)
                cost += FoldCost(n.NoteNumber, t) * Math.Max(n.DurationMs, 1);

            // 取总折叠代价最小者；代价相同时偏向更小的移调量（尽量贴近 0）
            if (cost < bestCost)
            {
                bestCost = cost;
                bestTranspose = t;
            }
            else if (Math.Abs(cost - bestCost) < 1e-9 && Math.Abs(t) < Math.Abs(bestTranspose))
            {
                bestTranspose = t;
            }
        }

        return bestTranspose;
    }

    // 计算某音符在给定移调量下的八度折叠距离（半音）。在音域内为 0，越界则加上被移入时跨过的八度数。
    private static int FoldCost(int midiNote, int transpose)
    {
        int adjusted = midiNote + transpose;
        while (adjusted < InstrumentMapper.MinMidi)
            adjusted += InstrumentMapper.Octave;
        while (adjusted > InstrumentMapper.MaxMidi)
            adjusted -= InstrumentMapper.Octave;
        return Math.Abs(adjusted - (midiNote + transpose));
    }

    // 力度曲线：将 MIDI Velocity (0-127) 映射为音量 (0-1)。
    private static float VelocityCurve(int velocity)
    {
        return velocity / 127f;
    }

    // 音量校准：Velocity × CC表情控制 × 乐器校准。
    // 不再使用 isMelody 一刀切因子，音量完全由 MIDI 自身的 Velocity + CC 决定。
    private static float CalibrateVolume(InstrumentType instrument,
        float baseVolume, float ccExpression = 1.0f)
    {
        baseVolume *= ccExpression;
        baseVolume *= VolumeCalibration.GetValueOrDefault(instrument, 1.0f);
        return Math.Clamp(baseVolume, 0, 1);
    }

    private List<TerrariaNote> OptimizeForClientVoices(List<TerrariaNote> notes)
    {
        // 如果两个开关都关闭，直接返回原始列表
        if (!EnableNoteMerge && !EnableNoteLimit)
            return notes;

        const long groupMs = 2;

        var result = new List<TerrariaNote>(notes.Count);
        int i = 0;

        while (i < notes.Count)
        {
            long groupTime = notes[i].StartTimeMs;
            var group = new List<TerrariaNote>();

            while (i < notes.Count && notes[i].StartTimeMs - groupTime <= groupMs)
            {
                group.Add(notes[i]);
                i++;
            }

            List<TerrariaNote> workingList;

            if (EnableNoteMerge)
            {
                // Merge exact duplicates first so the same note is not re-triggered in place.
                workingList = new List<TerrariaNote>(group.Count);
                foreach (var samePitch in group.GroupBy(n => (n.Instrument, n.SoundStyle, MathF.Round(n.PitchOffset, 2))))
                {
                    var sameNotes = samePitch.OrderByDescending(n => n.Volume).ToList();
                    var keep = sameNotes[0];
                    if (sameNotes.Count > 1)
                        keep.Volume = Math.Clamp(keep.Volume * (1f + 0.25f * (sameNotes.Count - 1)), 0f, 1f);
                    workingList.Add(keep);
                }
            }
            else
            {
                workingList = group;
            }

            if (EnableNoteLimit)
            {
                foreach (var styleGroup in workingList.GroupBy(n => n.SoundStyle))
                {
                    var styleNotes = styleGroup.ToList();

                    // Harp/Bell/GuitarAxe are exempt from the client's Stop-before-retrigger path.
                    bool overlapAllowed = styleNotes[0].SoundStyle is 26 or 35 or 47;
                    if (!overlapAllowed)
                    {
                        result.Add(styleNotes.OrderByDescending(n => n.Volume).First());
                        continue;
                    }

                    float gain = styleNotes.Count <= 4 ? 1f : MathF.Sqrt(4f / styleNotes.Count);
                    foreach (var note in styleNotes)
                    {
                        note.Volume = Math.Clamp(note.Volume * gain, 0f, 1f);
                        result.Add(note);
                    }
                }
            }
            else
            {
                result.AddRange(workingList);
            }
        }

        return result.OrderBy(n => n.StartTimeMs).ToList();
    }

    // === 轨道筛选 ===

    private static HashSet<int> SelectPriorityTracks(MidiData data, HashSet<int> melodyTrackIndices)
    {
        var selected = new HashSet<int>();

        foreach (var idx in melodyTrackIndices)
            selected.Add(idx);

        int drumIdx = data.Tracks.FindIndex(t => t.IsDrumTrack);
        if (drumIdx >= 0)
            selected.Add(drumIdx);

        if (data.Tracks.Count <= 5)
        {
            for (int i = 0; i < data.Tracks.Count; i++)
                selected.Add(i);
            return selected;
        }

        var accompaniment = data.Tracks
            .Select((t, i) => (Track: t, Index: i))
            .Where(x => !melodyTrackIndices.Contains(x.Index)
                && !x.Track.IsDrumTrack && x.Track.Notes.Count > 0)
            .OrderByDescending(x => x.Track.Notes.Count)
            .Take(2);

        foreach (var (_, idx) in accompaniment)
            selected.Add(idx);

        return selected;
    }
}
