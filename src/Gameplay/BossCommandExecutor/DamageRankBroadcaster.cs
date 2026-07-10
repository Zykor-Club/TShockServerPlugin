using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terraria;
using Terraria.GameContent;
using TShockAPI;

namespace BossCommandExecutor
{
    /// <summary>
    /// Boss伤害排行广播服务
    /// 从BossDamageTracker提取数据并格式化显示
    /// </summary>
    public class DamageRankBroadcaster
    {
        // 队伍颜色映射（与原插件保持一致）
        private static readonly Dictionary<int, string> TeamColors = new()
        {
            [0] = "5ADECE", // 白队
            [1] = "FF716D", // 红队
            [2] = "61E26B", // 绿队
            [3] = "61BFE2", // 蓝队
            [4] = "FCFE6D", // 黄队
            [5] = "E15BC2"  // 粉队
        };

        /// <summary>
        /// 广播伤害排行到所有在线玩家
        /// </summary>
        public void Broadcast(BossDamageTracker tracker, NPC npc)
        {
            try
            {
                var entries = ExtractPlayerEntries(tracker);
                if (entries.Count == 0) return;

                var message = BuildRankingMessage(tracker, npc, entries);
                BroadcastToAllPlayers(message);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BossCommand] 伤害排行广播失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 提取玩家伤害条目
        /// </summary>
        private List<NPCDamageTracker.PlayerCreditEntry> ExtractPlayerEntries(BossDamageTracker tracker)
        {
            var list = new List<NPCDamageTracker.PlayerCreditEntry>();
            
            // 通过反射或直接访问获取列表（OTAPI 3.x兼容）
            if (tracker._list == null) return list;

            foreach (var entry in tracker._list)
            {
                if (entry is NPCDamageTracker.PlayerCreditEntry playerEntry)
                {
                    list.Add(playerEntry);
                }
            }
            
            return list;
        }

        /// <summary>
        /// 构建排行消息（保持与原插件格式一致）
        /// </summary>
        private string BuildRankingMessage(
            BossDamageTracker tracker, 
            NPC npc, 
            List<NPCDamageTracker.PlayerCreditEntry> playerEntries)
        {
            var sb = new StringBuilder();
            var onlinePlayers = TShock.Players.Where(p => p?.Active == true).ToList();
            var combatants = new HashSet<string>(playerEntries.Select(p => p.PlayerName), StringComparer.OrdinalIgnoreCase);
            
            // 基础统计
            int worldDmg = tracker._worldCredit?.Damage ?? 0;
            int playerDmg = playerEntries.Sum(p => p.Damage);
            int totalDmg = playerDmg + worldDmg;
            double duration = tracker.Duration / 60.0; // 转换为秒

            // 未参战玩家
            var idlePlayers = onlinePlayers.Where(p => !combatants.Contains(p.Name)).ToList();
            string playerCountInfo = idlePlayers.Count > 0 
                ? $"[c/FF726E:{playerEntries.Count}]/[c/61BFE2:{onlinePlayers.Count}]" 
                : $"[c/FF726E:{playerEntries.Count}]";

            // 标题与基础信息
            sb.AppendLine("      [i:3455][c/AD89D5:伤][c/D68ACA:害][c/DF909A:排][c/E5A894:行][c/E5BE94:榜][i:3454]");
            sb.AppendLine($"{tracker.Name} 参战 {playerCountInfo}位 {FormatDuration(duration)}");
            sb.AppendLine($"生命: [c/FFA96D:{npc.lifeMax}] 攻击: [c/FFE36D:{npc.damage}] 防御: [c/EA64AC:{npc.defense}]");
            sb.AppendLine($"总伤: [c/FF726E:{totalDmg}] 玩家: [c/FCFE6D:{playerDmg}] 减益: [c/61BFE2:{worldDmg}]");

            // 计算MVP（DPS最高）
            var mvp = playerEntries.OrderByDescending(p => duration > 0 ? p.Damage / duration : 0).FirstOrDefault();
            
            // 获取最后一击信息
            string? lastHitterName = null;
            bool lastHitIsWorld = false;
            
            if (tracker._lastAttacker is NPCDamageTracker.PlayerCreditEntry lastPlayer)
            {
                lastHitterName = lastPlayer.PlayerName;
            }
            else if (tracker._lastAttacker is NPCDamageTracker.WorldCreditEntry)
            {
                lastHitIsWorld = true;
                lastHitterName = tracker._worldCredit?.Name.ToString();
            }

            // 构建排名列表（混合玩家和减益）
            var rankList = BuildRankingList(playerEntries, worldDmg, tracker._worldCredit?.Name.ToString());
            var percentages = NPCDamageTracker.CalculatePercentages(rankList.Select(r => r.Damage).ToArray());

            // 输出排名
            for (int i = 0; i < rankList.Count; i++)
            {
                var entry = rankList[i];
                sb.AppendLine(FormatRankingLine(
                    entry, 
                    i + 1, 
                    percentages[i], 
                    duration, 
                    mvp, 
                    lastHitterName, 
                    lastHitIsWorld,
                    onlinePlayers
                ));
            }

            // 未参战玩家列表
            if (idlePlayers.Count > 0)
            {
                sb.AppendLine($"未参战: {string.Join(", ", idlePlayers.Select(p => p.Name))}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 构建排名列表（玩家+环境伤害）
        /// </summary>
        private List<(string Name, int Damage, bool IsPlayer)> BuildRankingList(
            List<NPCDamageTracker.PlayerCreditEntry> players, 
            int worldDamage, 
            string? worldName)
        {
            var list = players.Select(p => (p.PlayerName, p.Damage, true)).ToList();
            
            if (worldDamage > 0 && !string.IsNullOrEmpty(worldName))
            {
                list.Add((worldName, worldDamage, false));
            }
            
            return list.OrderByDescending(x => x.Damage).ToList();
        }

        /// <summary>
        /// 格式化单行排名数据
        /// </summary>
        private string FormatRankingLine(
            (string Name, int Damage, bool IsPlayer) entry,
            int rank,
            int percent,
            double duration,
            NPCDamageTracker.PlayerCreditEntry? mvp,
            string? lastHitterName,
            bool lastHitIsWorld,
            List<TSPlayer> onlinePlayers)
        {
            if (entry.IsPlayer)
            {
                // 玩家行
                int dps = duration > 0 ? (int)(entry.Damage / duration) : 0;
                
                // 获取队伍颜色
                string displayName = entry.Name;
                var player = onlinePlayers.FirstOrDefault(p => 
                    p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                
                if (player != null && TeamColors.TryGetValue(player.Team, out var color))
                {
                    displayName = $"[c/{color}:{entry.Name}]";
                }

                string line = $"{rank}.{displayName} {percent}% 伤害{entry.Damage} 秒伤[c/FF706D:{dps}]";

                // 添加标记（MVP或最后一击）
                if (mvp != null && entry.Name.Equals(mvp.PlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    line += " [c/FCFE6D:<mvp>]";
                }
                else if (!lastHitIsWorld && lastHitterName != null && 
                         entry.Name.Equals(lastHitterName, StringComparison.OrdinalIgnoreCase))
                {
                    line += " [c/FF726E:<end>]";
                }

                return line;
            }
            else
            {
                // 环境伤害行（减益/陷阱）
                string line = $"{rank}.{entry.Name} {percent}% 伤害{entry.Damage}";
                
                if (lastHitIsWorld && lastHitterName != null && entry.Name == lastHitterName)
                {
                    line += " [c/FF726E:<end>]";
                }
                
                return line;
            }
        }

        /// <summary>
        /// 格式化战斗时长
        /// </summary>
        private string FormatDuration(double totalSeconds)
        {
            int secs = (int)totalSeconds;
            if (secs < 60) return $"{secs}秒";
            if (secs < 3600) return $"{secs / 60}分{secs % 60:D2}秒";
            return $"{secs / 3600}时{(secs % 3600) / 60}分{secs % 60:D2}秒";
        }

        private void BroadcastToAllPlayers(string message)
        {
            foreach (var player in TShock.Players.Where(p => p?.Active == true))
            {
                player.SendMessage(message, 255, 255, 255);
            }
        }
    }
}
