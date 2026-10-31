using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace AIAgent.Core;

/// <summary>
/// 统一日志入口。
///
/// TShock 的 TextLog.Write 内部没有锁、StreamWriter 也不是线程安全的，而我们在后台线程大量写日志。
/// 所以：自己写一份 plugin.log（私有锁串行化，永远可用），
/// 再通过主线程镜像到 TShock 日志，避免在后台线程和 TShock 自己的 writer 抢同一个 StreamWriter。
/// </summary>
public static class PluginLog
{
    private const string Prefix = "[AIAgent]";
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<(string Level, string Text)> Mirror = new();
    private static string _filePath = string.Empty;

    public static void Initialize(string dataDirectory)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, "plugin.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
            {
                File.Move(path, path + ".old", overwrite: true);
            }

            _filePath = path;
        }
        catch
        {
            // 日志自身失败不影响插件
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

    public static string Req(int id) => $"req#{id}";

    /// <summary>主线程调用：把排队的日志镜像到 TShock 日志（控制台/服务器日志）。</summary>
    public static void FlushMirror(int max = 20)
    {
        for (var i = 0; i < max; i++)
        {
            if (!Mirror.TryDequeue(out var item)) return;

            lock (Gate)
            {
                try
                {
                    if (item.Level == "ERROR") TShockAPI.TShock.Log.Error(item.Text);
                    else if (item.Level == "WARN") TShockAPI.TShock.Log.Warn(item.Text);
                    else TShockAPI.TShock.Log.Info(item.Text);
                }
                catch
                {
                    // 服务器日志不可用时忽略
                }
            }
        }
    }

    private static void Write(string level, string message)
    {
        var now = DateTime.Now;
        var text = $"{Prefix} {message}";

        try
        {
            if (_filePath.Length > 0)
            {
                var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}";
                lock (Gate)
                {
                    File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                }
            }
        }
        catch
        {
            // 写文件失败时至少还有镜像
        }

        Mirror.Enqueue((level, $"[{now:HH:mm:ss}] {text}"));
        while (Mirror.Count > 5000) Mirror.TryDequeue(out _);
    }
}
