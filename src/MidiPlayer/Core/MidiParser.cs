using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Common;
using MidiPlayer.Models;

namespace MidiPlayer.Core;

public static class MidiParser
{
    // 文件元数据缓存：路径 → (时长ms, BPM, 曲目名)
    private record CachedMeta(long DurationMs, int Bpm);
    private static readonly Dictionary<string, CachedMeta> MetaCache = new(StringComparer.OrdinalIgnoreCase);

    // 获取缓存的元数据（不重新解析），用于 fast list
    public static (long DurationMs, int Bpm)? GetCachedMeta(string filePath)
    {
        if (MetaCache.TryGetValue(filePath, out var meta))
            return (meta.DurationMs, meta.Bpm);
        return null;
    }

    public static MidiData Parse(string filePath)
    {
        var midiFile = MidiFile.Read(filePath);
        var tempoMap = midiFile.GetTempoMap();
        var trackChunks = midiFile.GetTrackChunks().ToList();

        var data = new MidiData();

        var tempo = tempoMap.GetTempoAtTime(new MidiTimeSpan(0));
        data.Bpm = (int)Math.Round(tempo.BeatsPerMinute);

        // === 全局收集 CC#7 (ChannelVolume) 和 CC#11 (Expression) 事件，按通道索引 ===
        // 在 MIDI Format 1 中，控制轨（track 0）可能包含所有通道的 CC 事件，
        // 而实际音符可能在别的轨道，因此需要跨轨道按通道关联。
        // 结构：channel → [(timeMs, controlNumber, value), ...]
        var globalCcEvents = new Dictionary<int, List<(long TimeMs, byte ControlNumber, byte Value)>>();

        for (int i = 0; i < trackChunks.Count; i++)
        {
            var timedEvents = trackChunks[i].GetTimedEvents();
            foreach (var timedEvent in timedEvents)
            {
                if (timedEvent.Event is ControlChangeEvent cc
                    && (cc.ControlNumber == 7 || cc.ControlNumber == 11))
                {
                    int channel = cc.Channel;
                    if (!globalCcEvents.ContainsKey(channel))
                        globalCcEvents[channel] = new List<(long, byte, byte)>();

                    var ccTimeMs = (long)timedEvent.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    globalCcEvents[channel].Add((ccTimeMs, cc.ControlNumber, cc.ControlValue));
                }
            }
        }

        // 按时间排序每个通道的 CC 事件
        foreach (var kv in globalCcEvents)
            kv.Value.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

        for (int i = 0; i < trackChunks.Count; i++)
        {
            var track = ParseTrack(trackChunks[i], tempoMap, i, globalCcEvents);
            data.Tracks.Add(track);
        }

        data.AllNotes = data.Tracks
            .SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTimeMs)
            .ToList();

        data.TotalDurationMs = (long)trackChunks
            .GetDuration<MetricTimeSpan>(tempoMap)
            .TotalMilliseconds;

        // 更新缓存
        MetaCache[filePath] = new CachedMeta(data.TotalDurationMs, data.Bpm);

        return data;
    }

    private static MidiTrack ParseTrack(TrackChunk trackChunk, TempoMap tempoMap, int trackIndex,
        Dictionary<int, List<(long TimeMs, byte ControlNumber, byte Value)>> globalCcEvents)
    {
        var midiTrack = new MidiTrack();
        var events = trackChunk.Events.ToList();

        var trackNameEvent = events.OfType<SequenceTrackNameEvent>().FirstOrDefault();
        midiTrack.Name = trackNameEvent?.Text ?? $"Track {trackIndex}";

        var programChange = events.OfType<ProgramChangeEvent>().FirstOrDefault();
        midiTrack.ProgramNumber = programChange?.ProgramNumber ?? 0;

        var instrumentNameEvent = events.OfType<InstrumentNameEvent>().FirstOrDefault();
        midiTrack.InstrumentName = instrumentNameEvent?.Text ?? "";

        var programNameEvent = events.OfType<ProgramNameEvent>().FirstOrDefault();
        midiTrack.ProgramName = programNameEvent?.Text ?? "";

        var firstChannelEvent = events.OfType<ChannelEvent>().FirstOrDefault();
        midiTrack.IsDrumTrack = firstChannelEvent?.Channel == 9;

        var notes = trackChunk.GetNotes();
        var chords = trackChunk.GetChords();
        var chordsByTick = chords
            .GroupBy(c => c.Time)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var note in notes)
        {
            try
            {
                var timeMs = TimeConverter.ConvertTo<MetricTimeSpan>(note.Time, tempoMap);
                var lengthMs = TimeConverter.ConvertTo<MetricTimeSpan>(note.Length, tempoMap);

                long startMs = (long)timeMs.TotalMilliseconds;

                var chordNotes = new List<int>();
                if (chordsByTick.TryGetValue(note.Time, out var chordList))
                {
                    var chord = chordList.FirstOrDefault(c =>
                        c.Notes.Any(n => n.NoteNumber == note.NoteNumber));
                    if (chord != null)
                    {
                        chordNotes = chord.Notes
                            .Select(n => (int)n.NoteNumber)
                            .Distinct()
                            .OrderBy(n => n)
                            .ToList();
                    }
                }

                // 从全局 CC 事件中查找该音符通道对应的 CC#7/CC#11 当前值
                float cc7 = 127f, cc11 = 127f;
                int channel = note.Channel;
                if (globalCcEvents.TryGetValue(channel, out var channelCcEvents))
                {
                    foreach (var cc in channelCcEvents)
                    {
                        if (cc.TimeMs > startMs) break;
                        if (cc.ControlNumber == 7) cc7 = cc.Value;
                        if (cc.ControlNumber == 11) cc11 = cc.Value;
                    }
                }

                float expression = (cc7 / 127f) * (cc11 / 127f);

                midiTrack.Notes.Add(new MidiNote
                {
                    NoteNumber = note.NoteNumber,
                    NoteName = note.NoteName.ToString(),
                    Octave = note.Octave,
                    Velocity = note.Velocity,
                    StartTimeMs = startMs,
                    DurationMs = (long)lengthMs.TotalMilliseconds,
                    TrackIndex = trackIndex,
                    ExpressionMultiplier = expression,
                    ChordNoteNumbers = chordNotes
                });
            }
            catch { }
        }

        return midiTrack;
    }
}
