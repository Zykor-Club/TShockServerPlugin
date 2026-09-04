using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace MidiPlayer.Core;

/// <summary>
/// 浮动文字辅助类：在玩家头顶或八音盒上方显示飘动的播放状态文字。
/// 原理：服务端无法直接调用 CombatText.NewText（服务端 netMode==2 时不渲染），
/// 需通过 CombatTextString(119) 网络消息发送给客户端，由客户端渲染飘字。
/// </summary>
public static class FloatingTextHelper
{
    /// <summary>单个图格 1 格 = 16 像素。</summary>
    private const int TilePx = 16;

    // 一组相对鲜艳的随机颜色池
    private static readonly Color[] Palette =
    {
        new(255, 80, 80),   // 红
        new(90, 200, 90),   // 绿
        new(90, 160, 255),  // 蓝
        new(255, 200, 60),  // 橙
        new(200, 100, 255), // 紫
        new(255, 120, 220), // 粉
        new(80, 255, 220),  // 青
        new(255, 255, 120), // 黄
    };

    private static Color NextColor()
    {
        return Palette[Main.rand.Next(Palette.Length)];
    }

    /// <summary>
    /// 在指定像素坐标处显示浮动文字（广播给所有客户端）。
    /// </summary>
    /// <param name="text">要显示的文字。</param>
    /// <param name="pixelX">像素 X 坐标。</param>
    /// <param name="pixelY">像素 Y 坐标。</param>
    public static void Show(float pixelX, float pixelY, string text)
    {
        var color = NextColor();
        NetMessage.SendData(
            (int)MessageID.CombatTextString,
            -1, -1,
            NetworkText.FromLiteral(text),
            unchecked((int)color.PackedValue),
            pixelX,
            pixelY);
    }

    /// <summary>
    /// 在玩家头顶上方 2 格处显示浮动文字（单人播放模式）。
    /// </summary>
    public static void ShowAbovePlayer(int playerIndex, string text)
    {
        var player = TShockAPI.TShock.Players[playerIndex];
        if (player == null || !player.Active || player.TPlayer == null)
            return;

        var pos = player.TPlayer.Center;
        Show(pos.X, pos.Y - 2 * TilePx, text); // 头顶上方 2 格
    }

    /// <summary>
    /// 在绑定八音盒上方 3 格处显示浮动文字（全服播放模式）。
    /// 未绑定时回退到玩家头顶上方 2 格。
    /// </summary>
    public static void ShowAboveMusicBox(string text)
    {
        var bound = MusicBoxBinding.Instance.Bound;
        if (bound != null)
        {
            // 八音盒左上角格中心的像素坐标，向上偏移 3 格
            float x = (bound.X + 1) * TilePx;
            float y = (bound.Y + 1) * TilePx - 3 * TilePx;
            Show(x, y, text);
            return;
        }

        // 未绑定八音盒：向所有在线玩家各自头顶显示
        foreach (var player in TShockAPI.TShock.Players)
        {
            if (player != null && player.Active && player.TPlayer != null)
            {
                var pos = player.TPlayer.Center;
                Show(pos.X, pos.Y - 2 * TilePx, text);
            }
        }
    }
}