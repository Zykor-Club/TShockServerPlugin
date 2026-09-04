using MidiPlayer.Models;

namespace MidiPlayer.Core;

public static class MelodyDetector
{
    // 识别主旋律轨道：综合音符密度、音高多样性、短音符比例、音域范围评分。
    // 返回主旋律轨道索引集合（得分 >= 最高分 70% 且 > 0.1）。
    public static HashSet<int> DetectMelodyTracks(
        List<MidiTrack> tracks,
        List<MidiNote> allNotes)
    {
        if (tracks.Count == 0) return new HashSet<int>();

        var scores = new double[tracks.Count];

        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (track.IsDrumTrack || track.Notes.Count == 0)
            {
                scores[i] = 0;
                continue;
            }

            int noteCount = track.Notes.Count;
            var pitches = track.Notes.Select(n => n.NoteNumber).ToList();

            string trackText = $"{track.Name} {track.InstrumentName} {track.ProgramName}".ToLowerInvariant();
            double nameScore = 0;
            if (trackText.Contains("melody") || trackText.Contains("lead") || trackText.Contains("vocal")
                || trackText.Contains("主旋律") || trackText.Contains("人声"))
                nameScore = 1;
            else if (trackText.Contains("bass") || trackText.Contains("pad")
                || trackText.Contains("accompaniment") || trackText.Contains("伴奏")
                || trackText.Contains("drone"))
                nameScore = -0.5;

            // 音符密度（越密集越可能是主旋律）
            double densityScore = Math.Min(noteCount / 200.0, 1.0);

            // 音高多样性
            var uniquePitches = pitches.Distinct().Count();
            double diversityScore = Math.Min((double)uniquePitches / 20.0, 1.0);

            // 短音符比例（旋律通常短音符多）
            int shortNotes = track.Notes.Count(n => n.DurationMs < 500);
            double shortNoteRatio = noteCount > 0 ? (double)shortNotes / noteCount : 0;

            // 音域范围
            int minPitch = pitches.Min();
            int maxPitch = pitches.Max();
            double rangeScore = Math.Min((maxPitch - minPitch) / 24.0, 1.0);

            scores[i] = densityScore * 0.3 + diversityScore * 0.25
                + shortNoteRatio * 0.25 + rangeScore * 0.2
                + nameScore * 0.15;
        }

        double maxScore = scores.Max();
        double threshold = maxScore * 0.7;

        var melodyIndices = new HashSet<int>();
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i] >= threshold && scores[i] > 0.1)
                melodyIndices.Add(i);
        }

        return melodyIndices;
    }
}
