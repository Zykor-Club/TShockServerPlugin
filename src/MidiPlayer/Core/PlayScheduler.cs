using System.Diagnostics;
using Microsoft.Xna.Framework;
using MidiPlayer.Models;
using Terraria;
using TShockAPI;

namespace MidiPlayer.Core;

public class PlayScheduler
{
    public static readonly PlayScheduler Instance = new();

    public int MaxNotesPerTick { get; set; } = 10;

    public int SameStyleRetriggerMs
    {
        get => _soundSender.SameStyleRetriggerMs;
        set => _soundSender.SameStyleRetriggerMs = value;
    }

    public SongConverter? SongConverter => _songConverter;

    private readonly Dictionary<int, PlayerPlaybackState> _states = new();
    private readonly Dictionary<int, List<QueuedSong>> _queues = new();
    private readonly SoundSender _soundSender = new();
    private readonly object _lock = new();

    private SongConverter? _songConverter;
    private int _lookAheadMs = 15;
    private bool _globalMode;
    private GlobalPlaybackState? _globalState;
    private readonly List<GlobalQueuedSong> _globalQueue = new();
    private readonly HashSet<string> _globalQueuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _globalStarting;
    private bool _globalPausedForNoPlayers;
    private bool _globalForcePaused; // 八音盒强制暂停：全服播放被绑定八音盒关闭而暂停
    private long _globalResumeTimer; // 全服停止后，首个玩家加入时开始计时，5秒后恢复队列

    private const long ResumeDelayMs = 5000; // 玩家加入后等待5秒再恢复队列

    private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

    private PlayScheduler() { }

    // 播放结果
    public enum PlayResult { Started, Enqueued, Duplicate, Failed }

    // 注入依赖：从 PluginConfig 读取的参数
    public void Init(SongConverter songConverter, int lookAheadMs)
    {
        _songConverter = songConverter;
        _lookAheadMs = lookAheadMs;
    }

    // 检查玩家是否正在播放
    public bool IsPlaying(int playerIndex)
    {
        lock (_lock) return _states.ContainsKey(playerIndex);
    }

    // 获取播放队列数量
    public int GetQueueCount(int playerIndex)
    {
        lock (_lock) return _queues.TryGetValue(playerIndex, out var q) ? q.Count : 0;
    }

    // 当前是否全服共享播放
    public bool IsGlobalMode
    {
        get { lock (_lock) return _globalMode; }
    }

    // 八音盒强制暂停全服播放（冻结当前进度）
    public void ForcePauseGlobal()
    {
        lock (_lock)
        {
            if (_globalForcePaused)
                return;

            _globalForcePaused = true;
            if (_globalState is { IsPlaying: true, IsPaused: false })
            {
                _globalState.IsPaused = true;
                _globalState.PausedElapsed += (long)((Stopwatch.GetTimestamp() - _globalState.StartTick) * TickToMs);
            }
        }
    }

    // 八音盒打开 → 恢复全服播放
    public void ForceResumeGlobal()
    {
        lock (_lock)
        {
            if (!_globalForcePaused)
                return;

            _globalForcePaused = false;
            bool resumed = false;
            if (_globalState is { IsPaused: true })
            {
                _globalState.IsPaused = false;
                _globalState.StartTick = Stopwatch.GetTimestamp();
                resumed = true;
            }

            // 八音盒重新打开：若有冻结的歌曲被恢复播放，在八音盒上方提示
            if (resumed && _globalState?.Song != null)
            {
                string displayName = Path.GetFileNameWithoutExtension(_globalState.Song.FileName);
                FloatingTextHelper.ShowAboveMusicBox($"继续播放 {displayName}");
            }
        }
    }

    // 全服队列数量
    public int GetGlobalQueueCount()
    {
        lock (_lock) return _globalQueue.Count;
    }

    // 个人当前播放歌曲名
    public string? GetCurrentSongName(int playerIndex)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(playerIndex, out var state) && state.Song != null)
                return Path.GetFileNameWithoutExtension(state.Song.FileName);
        }
        return null;
    }

    // 全服当前播放歌曲名
    public string? GetGlobalCurrentSongName()
    {
        lock (_lock)
        {
            return _globalState?.Song == null
                ? null
                : Path.GetFileNameWithoutExtension(_globalState.Song.FileName);
        }
    }

    // 全服当前点歌人
    public string? GetGlobalCurrentRequester()
    {
        lock (_lock) return _globalState?.RequesterName;
    }

    // 分页读取全服队列
    public List<GlobalQueueEntry> GetGlobalQueue(int page, int pageSize)
    {
        lock (_lock)
        {
            if (_globalQueue.Count == 0)
                return new List<GlobalQueueEntry>();

            int start = Math.Clamp((page - 1) * pageSize, 0, Math.Max(0, _globalQueue.Count - 1));
            int count = Math.Min(pageSize, _globalQueue.Count - start);

            return _globalQueue.GetRange(start, count)
                .Select((q, i) => new GlobalQueueEntry(
                    start + i + 1,
                    Path.GetFileNameWithoutExtension(q.FilePath),
                    q.RequesterName))
                .ToList();
        }
    }

    // 分页读取个人队列
    public List<PersonalQueueEntry> GetPersonalQueue(int playerIndex, int page, int pageSize)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(playerIndex, out var q) || q.Count == 0)
                return new List<PersonalQueueEntry>();

            int start = Math.Clamp((page - 1) * pageSize, 0, Math.Max(0, q.Count - 1));
            int count = Math.Min(pageSize, q.Count - start);

            return q.GetRange(start, count)
                .Select((song, i) => new PersonalQueueEntry(
                    start + i + 1,
                    Path.GetFileNameWithoutExtension(song.FilePath)))
                .ToList();
        }
    }

    // 切换全服共享播放，切换时清空个人与全服播放状态
    public bool SetGlobalMode(bool enabled)
    {
        lock (_lock)
        {
            if (_globalMode == enabled)
                return false;

            _globalMode = enabled;
            _states.Clear();
            _queues.Clear();
            _globalState = null;
            _globalQueue.Clear();
            _globalQueuedPaths.Clear();
            _globalStarting = false;
            _globalPausedForNoPlayers = false;
            _globalForcePaused = false;
            _globalResumeTimer = 0;
        }

        _soundSender.ResetAll();

        if (enabled)
        {
            Broadcast("[i:4080] [c/66CCFF:MidiPlay] [i:4080] [c/FFFFAA:全服共享播放已开启] [c/AAAAAA:所有个人播放状态与队列已清空]");
        }
        else
        {
            Broadcast("[i:4080] [c/66CCFF:MidiPlay] [i:4080] [c/FFCCAA:全服共享播放已关闭] [c/AAAAAA:恢复个人播放模式]");
        }

        return true;
    }

    // 静默开关，只影响全服播放
    public bool ToggleSilent(int playerIndex)
    {
        bool silent = _soundSender.IsSilent(playerIndex);
        _soundSender.SetSilent(playerIndex, !silent);
        return !silent;
    }

    // 从全服队列移除，返回是否成功
    public bool RemoveGlobalQueue(int index, string requesterName, bool isAdmin,
        out string removedName, out string removedRequester, out string error)
    {
        removedName = "";
        removedRequester = "";
        error = "";

        lock (_lock)
        {
            if (!_globalMode)
            {
                error = "当前不是全服播放模式";
                return false;
            }

            if (_globalQueue.Count == 0)
            {
                error = "全服队列为空";
                return false;
            }

            if (index < 1 || index > _globalQueue.Count)
            {
                error = $"队列索引无效，范围 1-{_globalQueue.Count}";
                return false;
            }

            var item = _globalQueue[index - 1];
            if (!isAdmin && !string.Equals(item.RequesterName, requesterName, StringComparison.OrdinalIgnoreCase))
            {
                error = "只能移除自己点播的歌曲";
                return false;
            }

            _globalQueue.RemoveAt(index - 1);
            _globalQueuedPaths.Remove(Path.GetFullPath(item.FilePath));
            removedName = Path.GetFileNameWithoutExtension(item.FilePath);
            removedRequester = item.RequesterName;
            return true;
        }
    }

    // 从个人队列移除，返回是否成功
    public bool RemovePersonalQueue(int playerIndex, int index, out string removedName)
    {
        removedName = "";

        lock (_lock)
        {
            if (!_queues.TryGetValue(playerIndex, out var q) || index < 1 || index > q.Count)
                return false;

            var item = q[index - 1];
            removedName = item.DisplayName;
            q.RemoveAt(index - 1);
            return true;
        }
    }

    // 播放或加入队列
    public PlayResult Play(int playerIndex, string filePath, float volume)
    {
        string fullPath = Path.GetFullPath(filePath);

        lock (_lock)
        {
            if (_globalMode)
            {
                if (_globalQueuedPaths.Contains(fullPath))
                    return PlayResult.Duplicate;

                if (_globalState != null || _globalStarting || _globalQueue.Count > 0)
                {
                    EnqueueGlobalInternal(fullPath, volume, GetPlayerName(playerIndex));
                    return PlayResult.Enqueued;
                }

                _globalStarting = true;
            }
            else
            {
                if (_states.ContainsKey(playerIndex))
                {
                    EnqueueInternal(playerIndex, fullPath, volume);
                    return PlayResult.Enqueued;
                }
            }
        }

        if (_globalMode)
        {
            var globalSong = new GlobalQueuedSong
            {
                FilePath = fullPath,
                Volume = volume,
                RequesterName = GetPlayerName(playerIndex),
            };

            return StartGlobalPlayback(globalSong, false)
                ? PlayResult.Started : PlayResult.Failed;
        }

        return StartPlayback(playerIndex, fullPath, volume, false)
            ? PlayResult.Started : PlayResult.Failed;
    }

    private void EnqueueInternal(int playerIndex, string filePath, float volume)
    {
        if (!_queues.ContainsKey(playerIndex))
            _queues[playerIndex] = new List<QueuedSong>();
        _queues[playerIndex].Add(new QueuedSong
        {
            FilePath = filePath,
            Volume = volume,
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            RequesterName = GetPlayerName(playerIndex),
        });
    }

    private void EnqueueGlobalInternal(string filePath, float volume, string requesterName)
    {
        _globalQueue.Add(new GlobalQueuedSong
        {
            FilePath = Path.GetFullPath(filePath),
            Volume = volume,
            RequesterName = requesterName,
        });
        _globalQueuedPaths.Add(Path.GetFullPath(filePath));
    }

    private bool StartPlayback(int playerIndex, string filePath, float volume, bool fromQueue)
    {
        if (_songConverter == null) return false;

        try
        {
            var midiData = MidiParser.Parse(filePath);
            var song = _songConverter.Convert(midiData, Path.GetFileName(filePath));

            lock (_lock)
            {
                _states[playerIndex] = new PlayerPlaybackState
                {
                    Song = song,
                    Volume = volume,
                    IsPlaying = true,
                    IsPaused = false,
                    StartTick = Stopwatch.GetTimestamp(),
                    PausedElapsed = 0,
                    NextNoteIndex = 0,
                };
                _soundSender.Reset(playerIndex);
            }

            var player = TShock.Players[playerIndex];
            string displayName = Path.GetFileNameWithoutExtension(filePath);
            string duration = TimeSpan.FromMilliseconds(song.TotalDurationMs).ToString(@"mm\:ss");
            string action = fromQueue ? "[c/FFFFAA:队列播放]" : "[c/FFEEAA:开始播放]";
            player?.SendMessage(
                $"[i:4080] [c/66CCFF:MidiPlay] [i:4080] {action} [c/90EE90:{displayName}] [c/AAAAAA:{duration}]",
                Microsoft.Xna.Framework.Color.White);

            // 单人模式：在玩家头顶上方显示"正在播放"浮动文字
            if (!IsGlobalMode)
                FloatingTextHelper.ShowAbovePlayer(playerIndex, $"正在播放 {displayName}");
            return true;
        }
        catch (Exception ex)
        {
            TShock.Players[playerIndex]?.SendErrorMessage($"[c/FF6666:播放失败]: {ex.Message}");
            return false;
        }
    }

    private bool StartGlobalPlayback(GlobalQueuedSong song, bool fromQueue)
    {
        if (_songConverter == null)
        {
            lock (_lock) _globalStarting = false;
            return false;
        }

        try
        {
            var midiData = MidiParser.Parse(song.FilePath);
            var converted = _songConverter.Convert(midiData, Path.GetFileName(song.FilePath));

            lock (_lock)
            {
                _globalState = new GlobalPlaybackState
                {
                    Song = converted,
                    FilePath = Path.GetFullPath(song.FilePath),
                    Volume = song.Volume,
                    IsPlaying = true,
                    IsPaused = false,
                    StartTick = Stopwatch.GetTimestamp(),
                    PausedElapsed = 0,
                    NextNoteIndex = 0,
                    RequesterName = song.RequesterName
                };
                _globalQueuedPaths.Add(Path.GetFullPath(song.FilePath));
                _globalStarting = false;

                // 若八音盒正处于关闭状态（强制暂停激活），新开始的全服播放立即冻结，等待八音盒打开后再推进
                if (_globalForcePaused)
                {
                    _globalState.IsPaused = true;
                    _globalState.PausedElapsed = 0;
                }
            }

            _soundSender.ResetAll();

            string displayName = Path.GetFileNameWithoutExtension(song.FilePath);
            string duration = TimeSpan.FromMilliseconds(converted.TotalDurationMs).ToString(@"mm\:ss");
            string action = fromQueue ? "[c/FFFFAA:全服队列播放]" : "[c/FFEEAA:全服开始播放]";
            Broadcast(
                $"[i:4080] [c/66CCFF:MidiPlay] [i:4080] {action} [c/90EE90:{displayName}] [c/AAAAAA:{duration}] [c/CCCCCC:点歌人 {song.RequesterName}]");

            // 全服模式：在绑定八音盒上方显示浮动文字
            // 八音盒关闭时歌曲被冻结不播放，提示等待打开；否则显示"正在播放"
            if (_globalForcePaused)
                FloatingTextHelper.ShowAboveMusicBox($"{displayName} 已入队，八音盒关闭，打开后播放");
            else
                FloatingTextHelper.ShowAboveMusicBox($"正在播放 {displayName}");
            return true;
        }
        catch (Exception ex)
        {
            lock (_lock) _globalStarting = false;
            Broadcast($"[c/FF6666:全服播放失败]: {ex.Message}");
            return false;
        }
    }

    private static string GetPlayerName(int playerIndex)
    {
        return TShock.Players[playerIndex]?.Name ?? "Unknown";
    }

    private static void Broadcast(string message)
    {
        foreach (var player in TShock.Players)
            player?.SendMessage(message, Color.White);
    }

    private static bool HasOnlinePlayers()
    {
        foreach (var player in TShock.Players)
        {
            if (player != null && player.Active)
                return true;
        }
        return false;
    }

    public void Stop(int playerIndex)
    {
        lock (_lock)
        {
            _states.Remove(playerIndex);
            if (_queues.TryGetValue(playerIndex, out var queue))
                queue.Clear();
        }
        _soundSender.Reset(playerIndex);
    }

    public void Pause(int playerIndex)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(playerIndex, out var state) && state.IsPlaying && !state.IsPaused)
            {
                state.IsPaused = true;
                state.PausedElapsed += (long)((Stopwatch.GetTimestamp() - state.StartTick) * TickToMs);
            }
        }
    }

    public void Resume(int playerIndex)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(playerIndex, out var state) && state.IsPaused)
            {
                state.IsPaused = false;
                state.StartTick = Stopwatch.GetTimestamp();
            }
        }
    }

    // ==================== 每帧更新 ====================

    public void Update()
    {
        List<int>? finishedPlayers = null;
        GlobalPlaybackState? finishedGlobal = null;
        bool resumeQueueAfterLock = false;

        lock (_lock)
        {
            long nowTick = Stopwatch.GetTimestamp();

            int onlinePlayers = 0;
            foreach (var tsPlayer in TShock.Players)
            {
                if (tsPlayer != null && tsPlayer.Active)
                    onlinePlayers++;
            }

            foreach (var (playerIndex, state) in _states)
            {
                if (!state.IsPlaying || state.IsPaused || state.Song == null) continue;

                long elapsed = (long)((nowTick - state.StartTick) * TickToMs) + state.PausedElapsed;

                var player = Main.player[playerIndex];
                if (player == null || !player.active) continue;

                int notesSentThisTick = 0;

                while (state.NextNoteIndex < state.Song.Notes.Count)
                {
                    var note = state.Song.Notes[state.NextNoteIndex];
                    if (note.StartTimeMs > elapsed + _lookAheadMs) break;

                    if (notesSentThisTick >= MaxNotesPerTick)
                        break;

                    // When the server falls behind, skip quiet accompaniment instead of bursting.
                    if (note.StartTimeMs < elapsed - 50 && !note.IsMelody && note.Volume < 0.5f)
                    {
                        state.NextNoteIndex++;
                        continue;
                    }

                    _soundSender.SendNote(playerIndex, note, player.Center, state.Volume);
                    state.NextNoteIndex++;
                    notesSentThisTick++;
                }

                if (state.NextNoteIndex >= state.Song.Notes.Count)
                {
                    finishedPlayers ??= new List<int>();
                    finishedPlayers.Add(playerIndex);
                }
            }

            if (_globalMode)
            {
                if (onlinePlayers == 0)
                {
                    // 所有玩家离开：直接移除当前播放的midi，停止队列推进
                    if (_globalState != null)
                    {
                        // 停止当前播放，同时从"已播放/队列"记录中移除，允许后续重新添加
                        if (!string.IsNullOrEmpty(_globalState.FilePath))
                            _globalQueuedPaths.Remove(_globalState.FilePath);
                        _globalState = null;
                        _globalStarting = false;
                    }
                    _globalPausedForNoPlayers = true;
                }
                else if (_globalPausedForNoPlayers)
                {
                    // 有玩家在线但处于暂停状态：检查5秒计时器
                    long elapsedSinceResume = (long)((nowTick - _globalResumeTimer) * TickToMs);
                    if (elapsedSinceResume >= ResumeDelayMs)
                    {
                        _globalPausedForNoPlayers = false;
                        resumeQueueAfterLock = true;
                    }
                }
            }

            if (_globalState is { IsPlaying: true, IsPaused: false, Song: not null } globalState)
            {
                long elapsed = (long)((nowTick - globalState.StartTick) * TickToMs) + globalState.PausedElapsed;
                int notesSentThisTick = 0;

                var recipients = new List<int>();
                foreach (var tsPlayer in TShock.Players)
                {
                    if (tsPlayer == null || !tsPlayer.Active || _soundSender.IsSilent(tsPlayer.Index))
                        continue;

                    var mainPlayer = Main.player[tsPlayer.Index];
                    if (mainPlayer != null && mainPlayer.active)
                        recipients.Add(tsPlayer.Index);
                }

                while (globalState.NextNoteIndex < globalState.Song.Notes.Count)
                {
                    var note = globalState.Song.Notes[globalState.NextNoteIndex];
                    if (note.StartTimeMs > elapsed + _lookAheadMs)
                        break;

                    if (notesSentThisTick >= MaxNotesPerTick)
                        break;

                    // When the server falls behind, skip quiet accompaniment instead of bursting.
                    if (note.StartTimeMs < elapsed - 50 && !note.IsMelody && note.Volume < 0.5f)
                    {
                        globalState.NextNoteIndex++;
                        continue;
                    }

                    foreach (var playerIndex in recipients)
                    {
                        var mainPlayer = Main.player[playerIndex];
                        _soundSender.SendNote(playerIndex, note, mainPlayer.Center, globalState.Volume);
                    }

                    globalState.NextNoteIndex++;
                    notesSentThisTick++;
                }

                if (globalState.NextNoteIndex >= globalState.Song.Notes.Count)
                    finishedGlobal = globalState;
            }
        }

        if (finishedPlayers != null)
        {
            foreach (var pi in finishedPlayers)
            {
                string finishedName = "";
                lock (_lock)
                {
                    if (_states.TryGetValue(pi, out var st) && st.Song != null)
                        finishedName = Path.GetFileNameWithoutExtension(st.Song.FileName);
                    _states.Remove(pi);
                }
                _soundSender.Reset(pi);

                var tsPlayer = TShock.Players[pi];
                tsPlayer?.SendMessage(
                    $"[i:4080] [c/66CCFF:midiplay] [i:4080] [c/FFCCAA:播放完成] [c/90EE90:{finishedName}]",
                    Color.White);

                // 单人模式：在玩家头顶上方显示"播放完成"浮动文字
                if (!IsGlobalMode)
                    FloatingTextHelper.ShowAbovePlayer(pi, $"{finishedName} 播放完成");

                // 检查队列，自动播放下一首
                QueuedSong? nextSong = null;
                lock (_lock)
                {
                    if (_queues.TryGetValue(pi, out var queue) && queue.Count > 0)
                    {
                        nextSong = queue[0];
                        queue.RemoveAt(0);
                    }
                }

                if (nextSong != null)
                    StartPlayback(pi, nextSong.FilePath, nextSong.Volume, true);
            }
        }

        if (finishedGlobal != null)
        {
            string finishedName = "";
            string requesterName = "";
            lock (_lock)
            {
                if (_globalState != null && _globalState.Song != null)
                {
                    finishedName = Path.GetFileNameWithoutExtension(_globalState.Song.FileName);
                    requesterName = _globalState.RequesterName ?? "";
                }
                // 播放结束：将当前歌曲从"已播放/队列"记录中移除，允许再次添加
                if (_globalState != null && !string.IsNullOrEmpty(_globalState.FilePath))
                    _globalQueuedPaths.Remove(_globalState.FilePath);
                _globalState = null;
            }

            _soundSender.ResetAll();
            Broadcast(
                $"[i:4080] [c/66CCFF:midiplay] [i:4080] [c/FFCCAA:全服播放完成] [c/90EE90:{finishedName}] [c/AAAAAA:点歌人 {requesterName}]");

            // 全服模式：在绑定八音盒上方显示"播放完成"浮动文字
            FloatingTextHelper.ShowAboveMusicBox($"{finishedName} 播放完成");
            TryStartNextGlobal();
        }

        if (resumeQueueAfterLock)
            TryStartNextGlobal();
    }

    private void TryStartNextGlobal()
    {
        GlobalQueuedSong? nextSong = null;

        lock (_lock)
        {
            if (!_globalMode || _globalState != null || _globalStarting || _globalQueue.Count == 0)
                return;

            if (!HasOnlinePlayers())
            {
                _globalPausedForNoPlayers = true;
                return;
            }

            nextSong = _globalQueue[0];
            _globalQueue.RemoveAt(0);
            _globalQueuedPaths.Remove(Path.GetFullPath(nextSong.FilePath));
            _globalStarting = true;
        }

        if (nextSong != null && !StartGlobalPlayback(nextSong, true))
            TryStartNextGlobal();
    }

    public void OnPlayerLeave(int playerIndex)
    {
        lock (_lock) _states.Remove(playerIndex);
        _soundSender.Reset(playerIndex);
        _soundSender.ClearSilent(playerIndex);
    }

    public void OnPlayerJoin(int playerIndex)
    {
        _soundSender.Reset(playerIndex);
        _soundSender.ClearSilent(playerIndex);

        bool startTimer = false;

        lock (_lock)
        {
            if (!_globalMode)
                return;

            if (_globalPausedForNoPlayers)
            {
                // 首个玩家加入，启动5秒恢复计时器
                _globalResumeTimer = Stopwatch.GetTimestamp();
                startTimer = true;
            }
        }

        var tsPlayer = TShock.Players[playerIndex];

        if (startTimer)
        {
            tsPlayer?.SendMessage(
                $"[i:4080] [c/66CCFF:MidiPlay] [i:4080] [c/FFCCAA:全服播放将在5秒后恢复] [c/AAAAAA:队列将继续推进]",
                Color.White);
            return;
        }

        // 全服播放中，新玩家加入
        string? current = GetGlobalCurrentSongName();
        if (current != null)
        {
            tsPlayer?.SendMessage(
                $"[i:4080] [c/66CCFF:MidiPlay] [i:4080] [c/FFFFAA:全服播放中] [c/90EE90:{current}] [c/AAAAAA:将从当前进度播放]",
                Color.White);
        }
        else if (GetGlobalQueueCount() > 0)
        {
            tsPlayer?.SendInfoMessage("全服播放队列将在5秒后恢复推进");
        }
    }

    private class PlayerPlaybackState
    {
        public TerrariaSong? Song { get; set; }
        public float Volume { get; set; } = 1.0f;
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public long StartTick { get; set; }
        public long PausedElapsed { get; set; }
        public int NextNoteIndex { get; set; }
    }

    private class QueuedSong
    {
        public string FilePath { get; set; } = "";
        public float Volume { get; set; } = 1.0f;
        public string DisplayName { get; set; } = "";
        public string RequesterName { get; set; } = "";
    }

    private class GlobalPlaybackState
    {
        public TerrariaSong? Song { get; set; }
        public string FilePath { get; set; } = "";
        public float Volume { get; set; } = 1.0f;
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public long StartTick { get; set; }
        public long PausedElapsed { get; set; }
        public int NextNoteIndex { get; set; }
        public string RequesterName { get; set; } = "";
    }

    private class GlobalQueuedSong
    {
        public string FilePath { get; set; } = "";
        public float Volume { get; set; } = 1.0f;
        public string RequesterName { get; set; } = "";
    }

    public sealed record GlobalQueueEntry(int Index, string DisplayName, string RequesterName);
    public sealed record PersonalQueueEntry(int Index, string DisplayName);
}
