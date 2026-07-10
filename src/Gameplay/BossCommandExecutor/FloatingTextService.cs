using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using TShockAPI;

namespace BossCommandExecutor
{
    /// <summary>
    /// 玩家头顶浮动文本显示服务
    /// 使用战斗文本数据包（PacketTypes.CreateCombatTextExtended）
    /// </summary>
    public class FloatingTextService
    {
        /// <summary>
        /// 为指定Boss显示配置的浮动文本
        /// </summary>
        public void ShowForBoss(Configuration.BossCommandConfig bossConfig, NPC npc)
        {
            if (BossCommandPlugin.Config.FloatingTexts?.Count == 0) return;

            // 查找该Boss的浮动文本配置
            var configs = BossCommandPlugin.Config.FloatingTexts
                .Where(f => f.Enabled && f.BossIDs?.Contains(npc.netID) == true)
                .ToList();

            if (configs.Count == 0) return;

            var onlinePlayers = TShock.Players.Where(p => p?.Active == true).ToList();
            if (onlinePlayers.Count == 0) return;

            foreach (var config in configs)
            {
                string text = ReplacePlaceholders(config.Text, bossConfig, npc);
                var color = new Color(config.TextColor.R, config.TextColor.G, config.TextColor.B);

                foreach (var player in onlinePlayers)
                {
                    ShowCombatText(player, text, color);
                }
            }
        }

        /// <summary>
        /// 在玩家头顶显示战斗文本
        /// TShock 6.1 / 1.4.5兼容实现
        /// </summary>
        private void ShowCombatText(TSPlayer player, string text, Color color)
        {
            try
            {
                // 计算位置：玩家头顶上方50像素
                Vector2 position = player.TPlayer.Center;
                position.Y -= 50f;

                // 发送战斗文本数据包
                NetMessage.SendData(
                    (int)PacketTypes.CreateCombatTextExtended,
                    remoteClient: player.Index,  // 仅目标玩家可见
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
                // 降级方案：使用普通聊天消息
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
