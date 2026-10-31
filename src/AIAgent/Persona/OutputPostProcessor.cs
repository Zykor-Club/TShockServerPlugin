using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AIAgent.Persona;

/// <summary>
/// 输出后处理：去 Markdown、去 AI 腔、转义聊天标签、切成气泡。
/// 游戏聊天窗口很窄，一次发一大段会刷屏。
/// </summary>
public static class OutputPostProcessor
{
    // \u0060 是反引号，写成转义是为了让这段代码不依赖字面量
    private static readonly Regex CodeFence = new("\u0060{3}.*?\u0060{3}", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^[ \t]{0,3}#{1,6}[ \t]*", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^[ \t]{0,3}>[ \t]?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^[ \t]{0,3}[-*+][ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Ordered = new(@"^[ \t]{0,3}\d{1,2}[.)][ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Newlines = new(@"\s*\n\s*", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    private static readonly string[] ChatTags =
    {
        "[c/", "[i:", "[g:", "[n:", "[s:", "[u:", "[p/", "[/c]", "[/i]",
    };

    public static string Clean(string raw, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = CodeFence.Replace(raw, " ");
        text = Heading.Replace(text, string.Empty);
        text = Quote.Replace(text, string.Empty);
        text = Bullet.Replace(text, string.Empty);
        text = Ordered.Replace(text, string.Empty);
        text = text.Replace("**", string.Empty).Replace("__", string.Empty);
        text = text.Replace("\u0060", string.Empty).Replace("*", string.Empty);
        text = text.Replace("作为一个AI", "我").Replace("作为一个 AI", "我").Replace("作为人工智能", "我");
        text = Newlines.Replace(text, " ");
        text = Spaces.Replace(text, " ");
        text = EscapeChatTags(text).Trim();

        if (maxChars > 0 && text.Length > maxChars)
        {
            text = text.Substring(0, maxChars).TrimEnd() + "…";
        }

        return text;
    }

    private static string EscapeChatTags(string text)
    {
        foreach (var tag in ChatTags)
        {
            text = text.Replace(tag, "［" + tag.Substring(1), StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>把一段完整文本切成适合逐条发送的气泡（非流式场景使用）。</summary>
    public static IReadOnlyList<string> ToBubbles(string text, int maxBubbleChars)
    {
        var bubbles = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return bubbles;

        var buffer = new StringBuilder();
        foreach (var ch in text)
        {
            buffer.Append(ch);
            var isEnd = ch is '。' or '！' or '？' or '!' or '?' or '；' or ';' or '\n';
            if (isEnd && buffer.Length >= 8)
            {
                bubbles.Add(buffer.ToString().Trim());
                buffer.Clear();
            }
            else if (buffer.Length >= maxBubbleChars)
            {
                bubbles.Add(buffer.ToString().Trim());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0) bubbles.Add(buffer.ToString().Trim());
        return bubbles;
    }
}
