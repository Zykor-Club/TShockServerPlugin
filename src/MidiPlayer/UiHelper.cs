using Microsoft.Xna.Framework;
using TShockAPI;

namespace MidiPlayer;

/// <summary>
/// 统一命令提示样式：渐变色文本 + [i:4080] 图标。
/// 所有 /midi 子命令的成功/失败/信息提示都应通过本类输出，保证风格一致。
/// </summary>
public static class UiHelper
{
    private static readonly Color TitleStart = new(102, 204, 255); // 66CCFF 淡蓝
    private static readonly Color TitleEnd = new(255, 102, 204);   // FF66CC 粉紫

    /// <summary>命令前缀：图标 + 渐变标题 + 图标。</summary>
    public static string Prefix(string title = "midiplay")
        => $"[i:4080] {Gradient(title, TitleStart, TitleEnd)} [i:4080]";

    /// <summary>对文本逐字渐变上色。</summary>
    public static string Gradient(string text, Color start, Color end)
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

    /// <summary>成功提示（绿色）。</summary>
    public static void Success(TSPlayer player, string text)
        => player.SendMessage($"{Prefix()} [c/90EE90:{text}]", Color.White);

    /// <summary>错误提示（红色）。</summary>
    public static void Error(TSPlayer player, string text)
        => player.SendMessage($"{Prefix()} [c/FF6666:{text}]", Color.White);

    /// <summary>信息提示（淡黄）。</summary>
    public static void Info(TSPlayer player, string text)
        => player.SendMessage($"{Prefix()} [c/FFFFAA:{text}]", Color.White);
}