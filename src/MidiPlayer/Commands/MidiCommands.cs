using Microsoft.Xna.Framework;
using MidiPlayer.Config;
using MidiPlayer.Core;
using TShockAPI;

namespace MidiPlayer.Commands;

public class MidiCommands
{
    private const string MidiFolder = "MidiSongs";
    private const int PageSize = 10;

    private static string MidiFolderPath =>
        Path.Combine(TShock.SavePath, "MidiPlayer", MidiFolder);

    private TShockAPI.Command? _command;
    private static string[]? _cachedFiles;
    private static DateTime _cacheTime;

    public void Register(List<TShockAPI.Command> cmdList)
    {
        _command = new TShockAPI.Command("midiplayer.play", Dispatch, "midi")
        {
            HelpText = "/midi — 显示所有子命令帮助"
        };
        cmdList.Add(_command);
    }

    public void Unregister(List<TShockAPI.Command> cmdList)
    {
        if (_command != null)
            cmdList.Remove(_command);
    }

    // 控：需要玩家实体交互的命令不可在控制台执行
    private static bool RequirePlayer(TShockAPI.CommandArgs args)
    {
        if (args.Player.RealPlayer)
            return true;

        UiHelper.Error(args.Player, "该命令不能在控制台使用");
        return false;
    }

    private void Dispatch(TShockAPI.CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            ShowHelp(args);
            return;
        }

        switch (args.Parameters[0].ToLowerInvariant())
        {
            case "play":   Play(args); break;
            case "p":      Play(args); break;
            case "stop":   Stop(args); break;
            case "s":      Stop(args); break;
            case "pause":  Pause(args); break;
            case "resume": Resume(args); break;
            case "r":      Resume(args); break;
            case "list":   List(args); break;
            case "l":      List(args); break;
            case "add":    Add(args); break;
            case "allon":  AllOn(args); break;
            case "alloff": AllOff(args); break;
            case "bind":   Bind(args); break;
            case "unbind": Unbind(args); break;
            case "plist":  Playlist(args); break;
            case "pl":     Playlist(args); break;
            case "dlist":  RemoveFromPlaylist(args); break;
            case "slient": Silent(args); break;
            case "silent": Silent(args); break;
            case "sl":     Silent(args); break;
            case "merge":  ToggleMerge(args); break;
            case "limit":  ToggleLimit(args); break;
            case "help":   ShowHelp(args); break;
            default:
                UiHelper.Error(args.Player, $"未知子命令: {args.Parameters[0]}，输入 /midi 查看帮助");
                break;
        }
    }

    private void Play(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        var player = args.Player;
        if (args.Parameters.Count < 2)
        {
            UiHelper.Error(player, "用法: /midi play <文件名或编号> [音量 0.0-2.0]");
            return;
        }

        string filePath;

        if (int.TryParse(args.Parameters[1], out int index))
        {
            RefreshCache();
            if (_cachedFiles == null || _cachedFiles.Length == 0)
            {
                UiHelper.Error(player, "没有可用的 MIDI 文件");
                return;
            }
            if (index < 1 || index > _cachedFiles.Length)
            {
                UiHelper.Error(player, $"编号 {index} 无效，范围: 1-{_cachedFiles.Length}");
                return;
            }
            filePath = _cachedFiles[index - 1];
        }
        else
        {
            string fileName = args.Parameters[1];
            if (!fileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                fileName += ".mid";

            filePath = Path.Combine(MidiFolderPath, fileName);
            filePath = Path.GetFullPath(filePath);

            if (!filePath.StartsWith(Path.GetFullPath(MidiFolderPath), StringComparison.OrdinalIgnoreCase))
            {
                UiHelper.Error(player, "非法的文件路径!");
                return;
            }
        }

        if (!File.Exists(filePath))
        {
            UiHelper.Error(player, "文件不存在，使用 /midi list 查看可用文件");
            return;
        }

        float volume = 1.0f;
        if (args.Parameters.Count >= 3 && float.TryParse(args.Parameters[2], out float v))
            volume = Math.Clamp(v, 0.0f, 2.0f);

        var result = PlayScheduler.Instance.Play(player.Index, filePath, volume);

        switch (result)
        {
            case PlayScheduler.PlayResult.Enqueued:
            {
                string displayName = Path.GetFileNameWithoutExtension(filePath);
                int queuePos = PlayScheduler.Instance.IsGlobalMode
                    ? PlayScheduler.Instance.GetGlobalQueueCount()
                    : PlayScheduler.Instance.GetQueueCount(player.Index);
                string mode = PlayScheduler.Instance.IsGlobalMode ? "全服播放队列" : "播放队列";
                player.SendMessage(
                    $"{UiHelper.Prefix()} [c/AAAAFF:已加入{mode}] [c/90EE90:{displayName}] [c/AAAAAA:(#{queuePos})]",
                    Microsoft.Xna.Framework.Color.White);
                break;
            }
            case PlayScheduler.PlayResult.Duplicate:
                UiHelper.Error(player, "该 MIDI 已在全服播放或队列中，不能重复添加");
                break;
            case PlayScheduler.PlayResult.Failed:
                UiHelper.Error(player, "播放失败");
                break;
        }
    }

    private void Stop(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        if (PlayScheduler.Instance.IsGlobalMode)
        {
            UiHelper.Info(args.Player, "全服播放中，请使用 /midi alloff 停止全服播放");
            return;
        }

        PlayScheduler.Instance.Stop(args.Player.Index);
        UiHelper.Success(args.Player, "播放已停止");
    }

    private void Pause(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        if (PlayScheduler.Instance.IsGlobalMode)
        {
            UiHelper.Info(args.Player, "全服播放中，个人暂停不生效");
            return;
        }

        if (!PlayScheduler.Instance.IsPlaying(args.Player.Index))
        {
            UiHelper.Info(args.Player, "当前没有正在播放的音乐");
            return;
        }

        PlayScheduler.Instance.Pause(args.Player.Index);
        UiHelper.Success(args.Player, "播放已暂停");
    }

    private void Resume(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        if (PlayScheduler.Instance.IsGlobalMode)
        {
            UiHelper.Info(args.Player, "全服播放中，个人恢复不生效");
            return;
        }

        if (!PlayScheduler.Instance.IsPlaying(args.Player.Index))
        {
            UiHelper.Info(args.Player, "当前没有正在播放的音乐");
            return;
        }

        PlayScheduler.Instance.Resume(args.Player.Index);
        UiHelper.Success(args.Player, "播放已恢复");
    }

    private void List(TShockAPI.CommandArgs args)
    {
        if (!Directory.Exists(MidiFolderPath))
        {
            UiHelper.Info(args.Player, "MidiPlayer/MidiSongs 文件夹不存在，请先创建并放入 .mid 文件");
            return;
        }

        RefreshCache();
        if (_cachedFiles == null || _cachedFiles.Length == 0)
        {
            UiHelper.Info(args.Player, "没有找到 MIDI 文件");
            return;
        }

        int totalFiles = _cachedFiles.Length;
        int totalPages = (totalFiles + PageSize - 1) / PageSize;
        int page = 1;

        if (args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out int p))
            page = Math.Clamp(p, 1, totalPages);

        int start = (page - 1) * PageSize;
        int end = Math.Min(start + PageSize, totalFiles);

        args.Player.SendMessage(
            $"{UiHelper.Prefix()} [c/FFEEAA:MIDI 文件 ({totalFiles}首) 第 {page}/{totalPages} 页]",
            Microsoft.Xna.Framework.Color.White);

        for (int i = start; i < end; i++)
        {
            var name = Path.GetFileNameWithoutExtension(_cachedFiles[i]);

            var meta = MidiParser.GetCachedMeta(_cachedFiles[i]);
            if (meta.HasValue)
            {
                var duration = TimeSpan.FromMilliseconds(meta.Value.DurationMs);
                args.Player.SendMessage(
                    $"[c/FFD700:[{i + 1}]] [c/90EE90:{name}] [c/C0C0C0:{duration.Minutes:D2}:{duration.Seconds:D2}]",
                    Microsoft.Xna.Framework.Color.White);
            }
            else
            {
                try
                {
                    var data = MidiParser.Parse(_cachedFiles[i]);
                    var duration = TimeSpan.FromMilliseconds(data.TotalDurationMs);
                    args.Player.SendMessage(
                        $"[c/FFD700:[{i + 1}]] [c/90EE90:{name}] [c/C0C0C0:{duration.Minutes:D2}:{duration.Seconds:D2}]",
                        Microsoft.Xna.Framework.Color.White);
                }
                catch
                {
                    args.Player.SendMessage(
                        $"[c/FFD700:[{i + 1}]] [c/90EE90:{name}] [c/FF6666:解析失败]",
                        Microsoft.Xna.Framework.Color.White);
                }
            }
        }

        if (totalPages > 1)
            UiHelper.Info(args.Player, $"输入 /midi list <页码> 翻页，共 {totalPages} 页");
    }

    // /midi add <编号> — 将指定歌曲加入“需微调”列表（自动做整曲微调）；无参数时列出当前列表
    private void Add(TShockAPI.CommandArgs args)
    {
        var player = args.Player;
        if (!player.HasPermission("midiplayer.admin"))
        {
            UiHelper.Error(player, "你没有管理员权限");
            return;
        }

        var converter = PlayScheduler.Instance.SongConverter;
        if (converter == null)
        {
            UiHelper.Error(player, "插件尚未初始化完成");
            return;
        }

        if (!converter.EnableAutoTranspose)
        {
            UiHelper.Error(player, "自动微调未开启，请先将配置文件中的 \"启用自动微调\" 设为 true 并 /reload");
            return;
        }

        // 无参数：列出当前需微调的歌曲
        if (args.Parameters.Count < 2)
        {
            if (converter.AutoTransposeSongs.Count == 0)
            {
                UiHelper.Info(player, "需微调歌曲列表为空，使用 /midi add <编号> 添加");
                return;
            }
            UiHelper.Info(player, $"当前需微调歌曲 ({converter.AutoTransposeSongs.Count}首):");
            foreach (var name in converter.AutoTransposeSongs)
                player.SendMessage($"[c/FFD700:-] [c/90EE90:{name}]", Color.White);
            return;
        }

        RefreshCache();
        if (_cachedFiles == null || _cachedFiles.Length == 0)
        {
            UiHelper.Error(player, "没有可用的 MIDI 文件");
            return;
        }
        if (!int.TryParse(args.Parameters[1], out int index) || index < 1 || index > _cachedFiles.Length)
        {
            UiHelper.Error(player, $"编号 {args.Parameters[1]} 无效，范围: 1-{_cachedFiles.Length}，使用 /midi list 查看");
            return;
        }

        string fileName = Path.GetFileName(_cachedFiles[index - 1]);

        if (converter.AutoTransposeSongs.Contains(fileName))
        {
            UiHelper.Info(player, $"{fileName} 已在需微调列表中");
            return;
        }

        converter.AutoTransposeSongs.Add(fileName);

        // 同步写入配置以持久化（Load 时会自动清理已被删除的 MIDI 文件）
        var config = MidiPlayerConfig.Load();
        if (!config.AutoTransposeSongs.Contains(fileName))
            config.AutoTransposeSongs.Add(fileName);
        MidiPlayerConfig.Save(config);

        UiHelper.Success(player, $"已加入需微调列表: {fileName}，播放时将自动整曲微调");
    }

    private void AllOn(TShockAPI.CommandArgs args)
    {
        if (!args.Player.HasPermission("midiplayer.admin"))
        {
            UiHelper.Error(args.Player, "你没有管理员权限");
            return;
        }

        if (!PlayScheduler.Instance.SetGlobalMode(true))
        {
            UiHelper.Info(args.Player, "全服共享播放已经处于开启状态");
            return;
        }

        // 开启全服播放后，按绑定八音盒当前开关状态同步强制暂停/恢复
        MusicBoxBinding.Instance.ApplyCurrentState();
    }

    private void AllOff(TShockAPI.CommandArgs args)
    {
        if (!args.Player.HasPermission("midiplayer.admin"))
        {
            UiHelper.Error(args.Player, "你没有管理员权限");
            return;
        }

        if (!PlayScheduler.Instance.SetGlobalMode(false))
            UiHelper.Info(args.Player, "全服共享播放已经处于关闭状态");
    }

    private void Bind(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        var player = args.Player;
        if (!player.HasPermission("midiplayer.admin"))
        {
            UiHelper.Error(player, "你没有管理员权限");
            return;
        }

        if (MusicBoxBinding.Instance.IsInBindMode)
        {
            // 再次输入 /midi bind → 取消当前绑定模式
            MusicBoxBinding.Instance.CancelBindMode(player);
            UiHelper.Info(player, "已取消绑定模式");
            return;
        }

        if (MusicBoxBinding.Instance.EnterBindMode(player))
            UiHelper.Success(player, "已进入绑定模式，请右键目标八音盒完成绑定（30秒内有效）");
        else
            UiHelper.Error(player, "进入绑定模式失败");
    }

    private void Unbind(TShockAPI.CommandArgs args)
    {
        var player = args.Player;
        if (!player.HasPermission("midiplayer.admin"))
        {
            UiHelper.Error(player, "你没有管理员权限");
            return;
        }

        if (MusicBoxBinding.Instance.UnbindCurrentWorld(player))
            UiHelper.Success(player, "当前世界的八音盒绑定已解除");
        else
            UiHelper.Info(player, "当前世界没有绑定的八音盒");
    }

    private void Playlist(TShockAPI.CommandArgs args)
    {
        var player = args.Player;
        bool global = PlayScheduler.Instance.IsGlobalMode;
        string modeText = global ? "[c/FFD700:全服共享播放]" : "[c/90EE90:个人播放]";

        player.SendMessage(
            $"{UiHelper.Prefix()} 当前模式: {modeText}",
            Microsoft.Xna.Framework.Color.White);

        int total;
        string currentText = "";

        if (global)
        {
            var currentName = PlayScheduler.Instance.GetGlobalCurrentSongName();
            var requester = PlayScheduler.Instance.GetGlobalCurrentRequester();
            if (currentName != null)
                currentText = $"[c/FFFFAA:正在播放] [c/90EE90:{currentName}] [c/AAAAAA:点歌人 {requester}]";
            total = PlayScheduler.Instance.GetGlobalQueueCount();
        }
        else
        {
            var currentName = PlayScheduler.Instance.GetCurrentSongName(player.Index);
            if (currentName != null)
                currentText = $"[c/FFFFAA:正在播放] [c/90EE90:{currentName}]";
            total = PlayScheduler.Instance.GetQueueCount(player.Index);
        }

        int totalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
        int page = 1;
        if (args.Parameters.Count >= 2 && int.TryParse(args.Parameters[1], out int p))
            page = Math.Clamp(p, 1, totalPages);

        player.SendMessage(
            $"{(global ? "[c/FFD700:全服播放队列]" : "[c/90EE90:个人播放队列]")} ({total}首) 第 {page}/{totalPages} 页",
            Color.White);

        if (!string.IsNullOrEmpty(currentText))
            player.SendMessage(currentText, Color.White);

        if (total == 0)
        {
            UiHelper.Info(player, "队列为空");
            return;
        }

        if (global)
        {
            foreach (var entry in PlayScheduler.Instance.GetGlobalQueue(page, PageSize))
                player.SendMessage(
                    $"[c/FFD700:[{entry.Index}]] [c/90EE90:{entry.DisplayName}] [c/AAAAAA:点歌人 {entry.RequesterName}]",
                    Color.White);
        }
        else
        {
            foreach (var entry in PlayScheduler.Instance.GetPersonalQueue(player.Index, page, PageSize))
                player.SendMessage(
                    $"[c/FFD700:[{entry.Index}]] [c/90EE90:{entry.DisplayName}]",
                    Color.White);
        }

        if (totalPages > 1)
            UiHelper.Info(player, $"输入 /midi plist <页码> 翻页，共 {totalPages} 页");
    }

    private void RemoveFromPlaylist(TShockAPI.CommandArgs args)
    {
        var player = args.Player;
        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int index))
        {
            UiHelper.Error(player, "用法: /midi dlist <索引号>");
            return;
        }

        if (PlayScheduler.Instance.IsGlobalMode)
        {
            bool isAdmin = player.HasPermission("midiplayer.admin");
            if (PlayScheduler.Instance.RemoveGlobalQueue(index, player.Name, isAdmin,
                    out string removedName, out string removedRequester, out string error))
            {
                UiHelper.Success(player, $"已从全服队列移除: {removedName} (点歌人 {removedRequester})");
            }
            else
            {
                UiHelper.Error(player, error);
            }
            return;
        }

        if (PlayScheduler.Instance.RemovePersonalQueue(player.Index, index, out string name))
            UiHelper.Success(player, $"已从个人队列移除: {name}");
        else
            UiHelper.Error(player, "队列索引无效");
    }

    private void Silent(TShockAPI.CommandArgs args)
    {
        if (!RequirePlayer(args))
            return;

        bool silent = PlayScheduler.Instance.ToggleSilent(args.Player.Index);
        if (PlayScheduler.Instance.IsGlobalMode)
            UiHelper.Success(args.Player, silent ? "已开启静默，你不会听到全服 MIDI 播放" : "已关闭静默");
        else
            UiHelper.Info(args.Player, silent ? "已开启静默（全服播放开启后生效）" : "已关闭静默");
    }

    private void ToggleMerge(TShockAPI.CommandArgs args)
    {
        var converter = PlayScheduler.Instance.SongConverter;
        if (converter == null)
        {
            UiHelper.Error(args.Player, "插件尚未初始化完成");
            return;
        }

        bool? onOff = null;
        if (args.Parameters.Count >= 2)
        {
            if (args.Parameters[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                onOff = true;
            else if (args.Parameters[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                onOff = false;
            else
            {
                UiHelper.Error(args.Player, "用法: /midi merge [on/off]");
                return;
            }
        }

        if (onOff.HasValue)
        {
            converter.EnableNoteMerge = onOff.Value;
            SaveConfig(converter);
            UiHelper.Success(args.Player,
                onOff.Value ? "重复音符合并已开启" : "重复音符合并已关闭");
        }
        else
        {
            UiHelper.Info(args.Player,
                $"重复音符合并: {(converter.EnableNoteMerge ? "[c/90EE90:开启]" : "[c/FF6666:关闭]")}，使用 /midi merge on/off 切换");
        }
    }

    private void ToggleLimit(TShockAPI.CommandArgs args)
    {
        var converter = PlayScheduler.Instance.SongConverter;
        if (converter == null)
        {
            UiHelper.Error(args.Player, "插件尚未初始化完成");
            return;
        }

        bool? onOff = null;
        if (args.Parameters.Count >= 2)
        {
            if (args.Parameters[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                onOff = true;
            else if (args.Parameters[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                onOff = false;
            else
            {
                UiHelper.Error(args.Player, "用法: /midi limit [on/off]");
                return;
            }
        }

        if (onOff.HasValue)
        {
            converter.EnableNoteLimit = onOff.Value;
            SaveConfig(converter);
            UiHelper.Success(args.Player,
                onOff.Value ? "音符限流已开启" : "音符限流已关闭");
        }
        else
        {
            UiHelper.Info(args.Player,
                $"音符限流: {(converter.EnableNoteLimit ? "[c/90EE90:开启]" : "[c/FF6666:关闭]")}，使用 /midi limit on/off 切换");
        }
    }

    private static void SaveConfig(SongConverter converter)
    {
        var config = MidiPlayerConfig.Load();
        config.EnableNoteMerge = converter.EnableNoteMerge;
        config.EnableNoteLimit = converter.EnableNoteLimit;
        MidiPlayerConfig.Save(config);
    }

    private void ShowHelp(TShockAPI.CommandArgs args)
    {
        args.Player.SendMessage(UiHelper.Prefix(), Color.White);

        bool isAdmin = args.Player.HasPermission("midiplayer.admin");

        var helpLines = new List<string>
        {
            "/midi play <文件名或编号> 音量 — 播放MIDI（播放中自动加入队列）",
            "/midi stop — 停止播放并清空队列",
            "/midi pause — 暂停播放",
            "/midi resume — 恢复播放",
            "/midi list 页码 — 列出可用文件（支持翻页）"
        };

        // 管理员命令：无权限者不显示入口（执行时仍会校验权限）
        if (isAdmin)
        {
            helpLines.Add("/midi allon — 开启全服共享播放（管理员）");
            helpLines.Add("/midi alloff — 关闭全服共享播放（管理员）");
            helpLines.Add("/midi bind — 进入绑定模式，右键八音盒完成绑定（管理员）");
            helpLines.Add("/midi unbind — 解除当前世界的八音盒绑定（管理员）");
            helpLines.Add("/midi add <编号> — 将歌曲加入需微调列表（管理员）");
        }

        helpLines.AddRange(new[]
        {
            "/midi plist 页码 — 查看当前模式与播放队列",
            "/midi dlist <索引> — 移除播放队列中的歌曲",
            "/midi slient — 切换全服播放静默",
            "/midi merge [on/off] — 切换重复音符合并开关",
            "/midi limit [on/off] — 切换音符限流开关",
            "/reload — 热重载插件配置（TShock 自带命令）"
        });

        for (int i = 0; i < helpLines.Count; i++)
        {
            float t = helpLines.Count == 1 ? 0f : (float)i / (helpLines.Count - 1);
            var start = Color.Lerp(new Color(255, 200, 80), new Color(120, 230, 160), t);
            var end = Color.Lerp(new Color(255, 140, 80), new Color(255, 110, 190), t);
            args.Player.SendMessage(GradientText(helpLines[i], start, end), Color.White);
        }
    }

    private static string GradientText(string text, Color start, Color end)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return string.Concat(text.Select((ch, i) =>
        {
            float t = text.Length == 1 ? 0f : (float)i / (text.Length - 1);
            byte r = (byte)(start.R + (end.R - start.R) * t);
            byte g = (byte)(start.G + (end.G - start.G) * t);
            byte b = (byte)(start.B + (end.B - start.B) * t);
            return $"[c/{r:X2}{g:X2}{b:X2}:{ch}]";
        }));
    }

    private static void RefreshCache()
    {
        if (_cachedFiles != null && (DateTime.Now - _cacheTime).TotalSeconds < 5)
            return;

        if (!Directory.Exists(MidiFolderPath))
        {
            _cachedFiles = Array.Empty<string>();
            return;
        }

        _cachedFiles = Directory.GetFiles(MidiFolderPath, "*.mid")
            .Select(Path.GetFullPath)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _cacheTime = DateTime.Now;
    }
}
