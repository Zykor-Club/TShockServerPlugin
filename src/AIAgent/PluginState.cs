using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;

namespace AIAgent;

public static class PluginState
{
    public static Config Config { get; set; } = new();
    public static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    public static readonly ConcurrentDictionary<string, Session> Sessions = new();
    public static readonly ConcurrentDictionary<string, PlayerTokenStats> TokenStats = new();
    public static int ReqId = 0;

    public const string StatsFileName = "AIAgent_Stats.json";

    public static readonly ConcurrentQueue<AutoChatEvent> AutoChatQueue = new();
    public static readonly ConcurrentDictionary<string, DateTime> LastAutoChatTime = new();
    public static DateTime LastGlobalAutoChatTime = DateTime.MinValue;
    public static System.Threading.Timer? AutoChatTimer;

    // 全局线程同步锁 - 用于 session.History 并发访问
    public static readonly object HistoryLock = new();

    // 优雅关闭信号
    public static readonly CancellationTokenSource Cts = new();

    // 邮件插件反射缓存
    public static object? PlayerMailPluginInstance = null;
    public static MethodInfo? MailSenderMethod = null;
    public static bool PlayerMailDetected = false;
}
