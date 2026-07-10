using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Terraria;
using TShockAPI;

namespace BossCommandExecutor
{
    /// <summary>
    /// 极简Boss追踪器
    /// 核心：记录存活的Boss实例，死亡时只处理一次
    /// </summary>
    public class BossTracker : IDisposable
    {
        // Key: npc.whoAmI (实例ID), Value: netID (Boss类型)
        // 只记录配置中存在的Boss
        private readonly ConcurrentDictionary<int, int> _aliveInstances = new();
        
        // 记录哪些Boss类型被生成过（用于RequireSummoned检查）
        private readonly ConcurrentDictionary<int, byte> _spawnedTypes = new();
        
        private readonly Timer _cleanupTimer;

        public BossTracker()
        {
            // 每5分钟清理一次残留数据（防内存泄漏）
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Boss生成时调用
        /// </summary>
        public void MarkAsAlive(int whoAmI, int netID, string bossName)
        {
            _aliveInstances[whoAmI] = netID;
            _spawnedTypes[netID] = 1; // 标记该类型已生成
            
            TShock.Log.ConsoleDebug($"[BossCommand] 追踪Boss: {bossName} (NetID:{netID}, Idx:{whoAmI})");
        }

        /// <summary>
        /// Boss死亡时调用
        /// 返回true表示这是有效的首次死亡（该实例之前被追踪过）
        /// </summary>
        public bool TryProcessDeath(int whoAmI, int netID)
        {
            // 从存活列表移除该实例
            bool wasAlive = _aliveInstances.TryRemove(whoAmI, out var storedNetId);
            
            // 如果之前不在列表中，说明是重复触发（多体节）或未追踪的
            if (!wasAlive) return false;
            
            // 验证NetID匹配（安全检查）
            return storedNetId == netID;
        }

        /// <summary>
        /// 检查该Boss类型是否被生成过
        /// </summary>
        public bool WasEverSpawned(int netID) => _spawnedTypes.ContainsKey(netID);

        /// <summary>
        /// 清理已经不存在的NPC实例（防止内存泄漏）
        /// </summary>
        private void Cleanup(object? state)
        {
            var toRemove = _aliveInstances.Keys
                .Where(idx => idx >= 0 && idx < Main.npc.Length && !Main.npc[idx].active)
                .ToList();
            
            foreach (var idx in toRemove)
                _aliveInstances.TryRemove(idx, out _);
                
            if (toRemove.Count > 0)
                TShock.Log.ConsoleDebug($"[BossCommand] 清理 {toRemove.Count} 个残留Boss记录");
        }

        public void Dispose() => _cleanupTimer?.Dispose();
    }
}
