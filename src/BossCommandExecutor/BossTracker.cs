using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Terraria;
using Terraria.GameContent;
using TShockAPI;

namespace BossCommandExecutor
{
    public class BossTracker : IDisposable
    {
        private readonly ConcurrentDictionary<int, int> _aliveInstances = new();
        
        private readonly ConcurrentDictionary<int, byte> _spawnedTypes = new();
        
        private readonly ConcurrentDictionary<string, ConcurrentHashSet<int>> _compositeBossSegments = new();
        
        private readonly Timer _cleanupTimer;

        public static readonly Dictionary<int, int[]> MultiSegmentBossMap = new()
        {
            { 13, new[] { 13, 14, 15 } },
            { 14, new[] { 13, 14, 15 } },
            { 15, new[] { 13, 14, 15 } },
            { 266, new[] { 266, 267 } },
            { 267, new[] { 266, 267 } },
            { 35, new[] { 35, 36 } },
            { 36, new[] { 35, 36 } },
            { 113, new[] { 113, 114 } },
            { 114, new[] { 113, 114 } },
            { 125, new[] { 125, 126 } },
            { 126, new[] { 125, 126 } },
            { 127, new[] { 127, 128, 129, 130, 131 } },
            { 128, new[] { 127, 128, 129, 130, 131 } },
            { 129, new[] { 127, 128, 129, 130, 131 } },
            { 130, new[] { 127, 128, 129, 130, 131 } },
            { 131, new[] { 127, 128, 129, 130, 131 } },
            { 134, new[] { 134, 135, 136 } },
            { 135, new[] { 134, 135, 136 } },
            { 136, new[] { 134, 135, 136 } },
            { 245, new[] { 245, 246, 247, 248 } },
            { 246, new[] { 245, 246, 247, 248 } },
            { 247, new[] { 245, 246, 247, 248 } },
            { 248, new[] { 245, 246, 247, 248 } },
            { 396, new[] { 396, 397, 398 } },
            { 397, new[] { 396, 397, 398 } },
            { 398, new[] { 396, 397, 398 } }
        };

        public BossTracker()
        {
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public void MarkAsAlive(int whoAmI, int netID, string bossName)
        {
            _aliveInstances[whoAmI] = netID;
            _spawnedTypes[netID] = 1;
            
            if (MultiSegmentBossMap.TryGetValue(netID, out var segmentTypes))
            {
                var key = string.Join(",", segmentTypes.OrderBy(t => t));
                var segments = _compositeBossSegments.GetOrAdd(key, _ => new ConcurrentHashSet<int>());
                segments.Add(whoAmI);
                TShock.Log.ConsoleDebug($"[BossCommand] 追踪复合Boss体节: {bossName} (NetID:{netID}, Idx:{whoAmI}, Key:{key})");
            }
            else
            {
                var customDef = NPCDamageTracker.CustomBossDefinitions[netID];
                if (customDef != null && customDef.NPCTypes != null)
                {
                    var key = string.Join(",", customDef.NPCTypes.OrderBy(t => t));
                    var segments = _compositeBossSegments.GetOrAdd(key, _ => new ConcurrentHashSet<int>());
                    segments.Add(whoAmI);
                    TShock.Log.ConsoleDebug($"[BossCommand] 追踪复合Boss体节(NPCDamageTracker): {bossName} (NetID:{netID}, Idx:{whoAmI}, Key:{key})");
                }
                else
                {
                    TShock.Log.ConsoleDebug($"[BossCommand] 追踪Boss: {bossName} (NetID:{netID}, Idx:{whoAmI})");
                }
            }
        }

        public bool TryProcessDeath(int whoAmI, int netID)
        {
            bool wasAlive = _aliveInstances.TryRemove(whoAmI, out var storedNetId);
            if (!wasAlive) return false;
            if (storedNetId != netID) return false;

            int[] segmentTypes = null;
            if (MultiSegmentBossMap.TryGetValue(netID, out segmentTypes))
            {
                var key = string.Join(",", segmentTypes.OrderBy(t => t));
                if (_compositeBossSegments.TryGetValue(key, out var segments))
                {
                    segments.TryRemove(whoAmI);
                    
                    foreach (var segmentType in segmentTypes)
                    {
                        if (IsAnyNPCAliveOfType(segmentType))
                        {
                            TShock.Log.ConsoleDebug($"[BossCommand] 复合Boss {key} 还有存活体节(类型:{segmentType})，跳过");
                            return false;
                        }
                    }
                    
                    _compositeBossSegments.TryRemove(key, out _);
                    TShock.Log.ConsoleDebug($"[BossCommand] 复合Boss {key} 所有体节已死亡，触发命令");
                }
            }
            else
            {
                var customDef = NPCDamageTracker.CustomBossDefinitions[netID];
                if (customDef != null && customDef.NPCTypes != null)
                {
                    segmentTypes = customDef.NPCTypes.ToArray();
                    var key = string.Join(",", segmentTypes.OrderBy(t => t));
                    if (_compositeBossSegments.TryGetValue(key, out var segments))
                    {
                        segments.TryRemove(whoAmI);
                        
                        foreach (var segmentType in segmentTypes)
                        {
                            if (IsAnyNPCAliveOfType(segmentType))
                            {
                                TShock.Log.ConsoleDebug($"[BossCommand] 复合Boss(NPCDamageTracker) {key} 还有存活体节(类型:{segmentType})，跳过");
                                return false;
                            }
                        }
                        
                        _compositeBossSegments.TryRemove(key, out _);
                        TShock.Log.ConsoleDebug($"[BossCommand] 复合Boss(NPCDamageTracker) {key} 所有体节已死亡，触发命令");
                    }
                }
            }

            return true;
        }

        public bool WasEverSpawned(int netID) => _spawnedTypes.ContainsKey(netID);

        public bool WasAnySpawned(int[] types)
        {
            foreach (var type in types)
            {
                if (_spawnedTypes.ContainsKey(type))
                    return true;
            }
            return false;
        }

        private static bool IsAnyNPCAliveOfType(int type)
        {
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];
                if (npc.active && npc.type == type)
                    return true;
            }
            return false;
        }

        private void Cleanup(object? state)
        {
            var toRemove = _aliveInstances.Keys
                .Where(idx => idx >= 0 && idx < Main.npc.Length && !Main.npc[idx].active)
                .ToList();
            
            foreach (var idx in toRemove)
                _aliveInstances.TryRemove(idx, out _);
                
            foreach (var kvp in _compositeBossSegments)
            {
                kvp.Value.RemoveWhere(idx => idx < 0 || idx >= Main.npc.Length || !Main.npc[idx].active);
                if (kvp.Value.IsEmpty)
                    _compositeBossSegments.TryRemove(kvp.Key, out _);
            }
                
            if (toRemove.Count > 0)
                TShock.Log.ConsoleDebug($"[BossCommand] 清理 {toRemove.Count} 个残留Boss记录");
        }

        public void Dispose() => _cleanupTimer?.Dispose();

        private class ConcurrentHashSet<T>
        {
            private readonly ConcurrentDictionary<T, byte> _dict = new();

            public void Add(T item) => _dict.TryAdd(item, 0);
            public bool TryRemove(T item) => _dict.TryRemove(item, out _);
            public bool IsEmpty => _dict.IsEmpty;

            public void RemoveWhere(Func<T, bool> predicate)
            {
                foreach (var key in _dict.Keys.ToArray())
                {
                    if (predicate(key))
                        _dict.TryRemove(key, out _);
                }
            }
        }
    }
}