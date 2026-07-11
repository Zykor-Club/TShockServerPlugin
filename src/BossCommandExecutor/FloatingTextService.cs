using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using TShockAPI;

namespace BossCommandExecutor
{
    public class FloatingTextService
    {
        public void ShowForBoss(Configuration.BossCommandConfig bossConfig, NPC npc)
        {
            if (!bossConfig.FloatingTextEnabled || string.IsNullOrEmpty(bossConfig.FloatingText)) return;

            var onlinePlayers = TShock.Players.Where(p => p?.Active == true).ToList();
            if (onlinePlayers.Count == 0) return;

            string text = ReplacePlaceholders(bossConfig.FloatingText, bossConfig, npc);
            var color = new Color(bossConfig.FloatingTextColor.R, bossConfig.FloatingTextColor.G, bossConfig.FloatingTextColor.B);

            foreach (var player in onlinePlayers)
            {
                ShowCombatText(player, text, color);
            }
        }

        private void ShowCombatText(TSPlayer player, string text, Color color)
        {
            try
            {
                Vector2 position = player.TPlayer.Center;
                position.Y -= 50f;

                NetMessage.SendData(
                    (int)PacketTypes.CreateCombatTextExtended,
                    remoteClient: player.Index,
                    ignoreClient: -1,
                    text: NetworkText.FromLiteral(text),
                    number: (int)color.PackedValue,
                    number2: position.X,
                    number3: position.Y,
                    number4: 0f
                );
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleDebug($"[BossCommand] 浮动文本发送失败 ({player.Name}): {ex.Message}");
                player.SendMessage(text, color.R, color.G, color.B);
            }
        }

        private string ReplacePlaceholders(string text, Configuration.BossCommandConfig config, NPC npc)
        {
            return text
                .Replace("{boss}", config.Name)
                .Replace("{boss.name}", config.Name)
                .Replace("{boss.id}", npc.netID.ToString())
                .Replace("{time}", System.DateTime.Now.ToString("HH:mm:ss"));
        }
    }
}
