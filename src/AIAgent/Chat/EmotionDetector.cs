using System;
using System.Collections.Generic;

namespace AIAgent.Chat;

public enum Emotion
{
    None,
    Tired,
    Sad,
    Happy,
    Angry,
    Anxious,
}

/// <summary>
/// 轻量情绪识别：关键词命中，不调用模型。
/// 目的不是"读心"，而是让回复先接住情绪，别一上来就给方案。
/// </summary>
public static class EmotionDetector
{
    private static readonly Dictionary<Emotion, string[]> Words = new()
    {
        [Emotion.Tired] = new[] { "累", "困", "疲惫", "没力气", "肝不动", "想睡" },
        [Emotion.Sad] = new[] { "难过", "伤心", "哭", "委屈", "孤独", "寂寞", "失落", "emo", "郁闷" },
        [Emotion.Happy] = new[] { "开心", "高兴", "太好了", "舒服", "爽", "棒", "哈哈", "嘿嘿" },
        [Emotion.Angry] = new[] { "气死", "生气", "烦死", "讨厌", "滚", "有病", "服了" },
        [Emotion.Anxious] = new[] { "害怕", "紧张", "焦虑", "慌", "崩溃", "怎么办", "急" },
    };

    private static readonly Dictionary<Emotion, string> Hints = new()
    {
        [Emotion.Tired] = "主人现在很累。先接一句“累坏了吧”，不要给建议、不要追问原因。",
        [Emotion.Sad] = "主人情绪低落。先让他知道你在，别急着解决问题，也别追问细节。",
        [Emotion.Happy] = "主人心情不错。具体地替他高兴，可以撒个娇，不要只说“真棒”。",
        [Emotion.Angry] = "主人有点生气。先顺着他说，不要反驳、不要讲道理。",
        [Emotion.Anxious] = "主人有点焦虑。语气放软，先安抚一句，再问要不要帮忙。",
    };

    public static Emotion Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Emotion.None;

        foreach (var pair in Words)
        {
            foreach (var word in pair.Value)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) return pair.Key;
            }
        }

        return Emotion.None;
    }

    public static string Hint(Emotion emotion) =>
        Hints.TryGetValue(emotion, out var hint) ? hint : string.Empty;
}
