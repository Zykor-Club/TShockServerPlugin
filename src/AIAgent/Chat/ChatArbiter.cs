using System;
using System.Collections.Generic;
using AIAgent.Config;

namespace AIAgent.Chat;

public readonly record struct ChatSignal(
    bool MentionsName,
    bool IsQuestion,
    bool IsGameTopic,
    bool RecentPartner,
    double SecondsSinceReplyToPlayer,
    double SecondsSinceAnyReply,
    int OtherMessagesWithinWindow,
    bool AddressesOtherPlayer = false,
    bool HasEmotion = false,
    double MinutesSinceLastMessage = 999,
    int RepliesToday = 0,
    int Jitter = 0,
    bool AddressesAi = false,
    bool IsInstruction = false);

public readonly record struct ChatDecision(bool Reply, int Score, string Reason);

/// <summary>
/// 多人抢话的仲裁器：只做本地打分，不调用模型。
/// 参考了 MaiBot 的"频率倾向 + 点名优先"、以及社区"本地门闸"实践里的硬规则与随机抖动。
/// </summary>
public static class ChatArbiter
{
    public static ChatDecision Decide(ChatSignal signal, ChatSettings cfg)
    {
        // 硬规则 1：点名，任何模式都回
        if (signal.MentionsName) return new ChatDecision(true, 100, "点名");

        // 硬规则 2：明显在跟别人说话（"小明，……" / "@小明"）
        if (signal.AddressesOtherPlayer) return new ChatDecision(false, 0, "在跟别人说话");

        if (string.Equals(cfg.Mode, "全部", StringComparison.OrdinalIgnoreCase))
        {
            if (cfg.DailyReplyLimit > 0 && signal.RepliesToday >= cfg.DailyReplyLimit)
            {
                return new ChatDecision(false, 0, "今日上限");
            }

            if (signal.SecondsSinceAnyReply < cfg.GlobalCooldownSeconds)
            {
                return new ChatDecision(false, 0, "公屏冷却");
            }
            return new ChatDecision(true, 50, "全部模式");
        }

        if (!string.Equals(cfg.Mode, "智能", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatDecision(false, 0, "提及模式未点名");
        }

        // 硬规则 3：冷却与日上限（不塞进分数，日志更可解释）
        if (signal.SecondsSinceAnyReply < cfg.GlobalCooldownSeconds)
        {
            return new ChatDecision(false, 0, "刚接过话");
        }

        if (cfg.DailyReplyLimit > 0 && signal.RepliesToday >= cfg.DailyReplyLimit)
        {
            return new ChatDecision(false, 0, "今日上限");
        }

        var score = 0;
        var reasons = new List<string>();

        if (signal.RecentPartner && signal.SecondsSinceReplyToPlayer <= cfg.ConversationWindowSeconds)
        {
            score += 35;
            reasons.Add("对话延续");
        }

        if (signal.IsQuestion)
        {
            score += 25;
            reasons.Add("问句");
        }

        if (signal.IsGameTopic)
        {
            score += 20;
            reasons.Add("游戏话题");
        }

        if (signal.HasEmotion)
        {
            score += 12;
            reasons.Add("有情绪");
        }

        // 消息里直接出现“你”，基本就是在跟它说话
        if (signal.AddressesAi)
        {
            score += 12;
            reasons.Add("在跟我说");
        }

        // “你直接用 /butcher 啊”这类给指令的追问
        if (signal.IsInstruction)
        {
            score += 8;
            reasons.Add("给指令");
        }

        if (signal.MinutesSinceLastMessage >= 10)
        {
            score += 8;
            reasons.Add("冷场");
        }

        if (signal.OtherMessagesWithinWindow >= 3)
        {
            score -= 15 * (signal.OtherMessagesWithinWindow - 2);
            reasons.Add("人多");
        }

        // 阈值附近的随机抖动：避免形成机械习惯
        score += Math.Clamp(signal.Jitter, 0, 6);

        var reply = score >= cfg.SmartThreshold;
        return new ChatDecision(reply, score, string.Join("+", reasons));
    }
}
