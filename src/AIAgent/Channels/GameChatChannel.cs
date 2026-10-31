using System;
using System.Collections.Concurrent;
using System.Linq;
using AIAgent.Chat;
using AIAgent.Core;
using TShockAPI;
using TShockAPI.Hooks;

namespace AIAgent.Channels;

/// <summary>
/// 只读两种输入：
///   1) 玩家在聊天栏里发的消息（PlayerChat 钩子，不含命令、不含系统消息）
///   2) 玩家死亡（主循环里做生死状态翻转检测）
/// 是否开口由 ChatArbiter 本地打分决定，不额外调用模型。
/// </summary>
public static class GameChatChannel
{
    private static readonly ConcurrentDictionary<int, bool> DeadState = new();
    private static readonly ConcurrentDictionary<int, DateTime> LastDeath = new();

    private static readonly string[] GameTopics =
    {
        "boss", "Boss", "材料", "合成", "制作", "配方", "进度", "装备", "武器", "药水",
        "挖矿", "刷", "召唤", "掉落", "配饰", "翅膀", "镐", "剑", "盔甲", "套装",
        "事件", "血月", "日食", "地狱", "困难模式", "天顶", "泰拉", "钓鱼", "boss",
    };

    public static void OnPlayerChat(PlayerChatEventArgs args)
    {
        try
        {
            var player = args.Player;
            if (player is null) return;
            Process(player, args.RawText);
        }
        catch (Exception ex)
        {
            // 聊天钩子里抛异常会打断 TShock 的聊天广播，必须吞掉并记录
            PluginLog.Error("聊天钩子异常", ex);
        }
    }

    public static void Process(TSPlayer player, string raw, bool bypassActive = false)
    {
        var cfg = PluginState.Config;
        if (!cfg.Chat.Enabled) return;
        if (player is null) return;
        if (!bypassActive && !player.Active) return;
        if (!player.HasPermission("aiagent.use")) return;

        var text = (raw ?? string.Empty).Trim();
        if (text.Length < cfg.Chat.MinLength) return;

        var subject = PluginState.Players.Resolve(player);
        var key = subject?.Key ?? player.Name;
        ChatActivity.RecordPlayerMessage(key, player.Name, text);

        var mentions = MentionsName(cfg, text);
        var payload = mentions ? StripName(cfg, text) : text;
        if (payload.Length == 0) payload = "叫我干嘛呀";

        var decision = !cfg.Chat.ArbitrationEnabled
            ? new ChatDecision(true, 0, "抢话判定关闭")
            : ChatArbiter.Decide(
                new ChatSignal(
                MentionsName: mentions,
                IsQuestion: LooksLikeQuestion(text),
                IsGameTopic: ContainsGameTopic(text),
                RecentPartner: ChatActivity.IsRecentPartner(key, cfg.Chat.ConversationWindowSeconds),
                SecondsSinceReplyToPlayer: ChatActivity.SecondsSinceReplyTo(key),
                SecondsSinceAnyReply: ChatActivity.SecondsSinceAnyReply(),
                    OtherMessagesWithinWindow: ChatActivity.OthersWithin(key),
                    AddressesOtherPlayer: AddressesOtherPlayer(player, text),
                    HasEmotion: HasEmotion(text),
                    MinutesSinceLastMessage: ChatActivity.MinutesSinceLastMessage(),
                    RepliesToday: ChatActivity.RepliesToday(),
                    Jitter: Random.Shared.Next(0, 7),
                    AddressesAi: LooksAddressedToAi(text),
                    IsInstruction: HasInstruction(text)),
                cfg.Chat);

        if (cfg.DebugLog)
        {
            PluginLog.Info($"仲裁 {(decision.Reply ? "回复" : "忽略")}({decision.Score}:{decision.Reason}) | {player.Name} | {text}");
        }

        if (!decision.Reply) return;

        var mode = cfg.Chat.PublicReply ? ChatReplyMode.Public : ChatReplyMode.Private;
        _ = ChatService.HandleAsync(player, payload, mode);
    }

    private static bool MentionsName(Config.PluginConfig cfg, string text)
    {
        var name = cfg.AIName;
        return text.Contains(name, StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("@" + name, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripName(Config.PluginConfig cfg, string text)
    {
        var name = cfg.AIName;
        return text
            .Replace("@" + name, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(name, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '，', ',', ':', '：', '!', '！', '?', '？', '~', '～');
    }

    private static bool LooksLikeQuestion(string text)
    {
        if (text.Contains('？') || text.Contains('?')) return true;

        string[] markers = { "吗", "呢", "怎么", "为什么", "如何", "能不能", "是不是", "有没有", "哪", "什么", "多少" };
        return markers.Any(text.Contains);
    }

    /// <summary>明显在跟别的玩家说话（"小明，……" 或 "@小明"）。</summary>
    private static bool AddressesOtherPlayer(TSPlayer speaker, string text)
    {
        foreach (var other in TShock.Players)
        {
            if (other is null || !other.Active || other.Index == speaker.Index) continue;

            var name = other.Name;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;

            if (text.StartsWith("@" + name, StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("@" + name, StringComparison.OrdinalIgnoreCase)) return true;
            if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool HasEmotion(string text) => EmotionDetector.Detect(text) != Emotion.None;

    /// <summary>消息前 8 个字里出现“你”，基本就是在跟 AI 说话。</summary>
    private static bool LooksAddressedToAi(string text)
    {
        var index = text.IndexOf('你');
        return index >= 0 && index <= 8;
    }

    private static readonly string[] InstructionWords =
    {
        "使用", "指令", "直接", "帮我", "试试", "输入", "告诉我", "按",
    };

    private static bool HasInstruction(string text) =>
        InstructionWords.Any(word => text.Contains(word, StringComparison.Ordinal));

    private static bool ContainsGameTopic(string text) =>
        GameTopics.Any(topic => text.Contains(topic, StringComparison.OrdinalIgnoreCase));

    /// <summary>主循环里调用：检测 活着 → 死亡 的那一下。</summary>
    public static void PollDeaths()
    {
        var cfg = PluginState.Config;
        if (!cfg.Chat.Enabled || !cfg.Chat.DeathReaction) return;

        foreach (var player in TShock.Players)
        {
            if (player is null || !player.Active) continue;

            var tplayer = player.TPlayer;
            if (tplayer is null) continue;

            var dead = tplayer.dead || tplayer.statLife <= 0;
            DeadState.TryGetValue(player.Index, out var wasDead);

            if (dead && !wasDead) OnDied(player);
            DeadState[player.Index] = dead;
        }
    }

    /// <summary>测试用：忽略冷却，立即触发一次死亡回应。</summary>
    public static void SimulateDeath(TSPlayer player)
    {
        LastDeath.TryRemove(player.Index, out _);
        OnDied(player);
    }

    public static void ResetPlayer(int index)
    {
        DeadState.TryRemove(index, out _);
        LastDeath.TryRemove(index, out _);
    }

    private static void OnDied(TSPlayer player)
    {
        var cfg = PluginState.Config;
        if (!player.HasPermission("aiagent.use")) return;

        var subject = PluginState.Players.Resolve(player);
        var key = subject?.Key ?? player.Name;
        var streak = CareService.RecordDeath(key, DateTime.UtcNow, cfg.Care.DeathStreakWindowMinutes);
        var isStreak = streak >= cfg.Care.DeathStreak;

        var now = DateTime.UtcNow;
        var gap = isStreak ? 20 : cfg.Chat.DeathCooldownSeconds;
        if (LastDeath.TryGetValue(player.Index, out var last) &&
            (now - last).TotalSeconds < gap)
        {
            return;
        }

        LastDeath[player.Index] = now;

        if (cfg.DebugLog) PluginLog.Info($"死亡触发: {player.Name} | 连死 {streak} 次");

        var mode = cfg.Chat.PublicReply ? ChatReplyMode.Public : ChatReplyMode.Private;
        var prompt = isStreak
            ? $"（系统事件）{player.Name} 在 {cfg.Care.DeathStreakWindowMinutes} 分钟里连续死了 {streak} 次。心疼一点，用一句话安慰；不要问怎么死的，不要说教。"
            : $"（系统事件）{player.Name} 刚刚死了。用一句话回应，别追问细节。";

        _ = ChatService.HandleAsync(player, prompt, mode, isEvent: true);
    }
}
