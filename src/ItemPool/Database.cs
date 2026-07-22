using TShockAPI;
using TShockAPI.DB;

namespace ItemPool;

/// <summary>
/// 数据库访问层 — 管理玩家领取记录
/// </summary>
public static class ItemPoolDatabase
{
    private static readonly object _lock = new();

    /// <summary>
    /// 初始化数据库表
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            TShock.DB.Query("CREATE TABLE IF NOT EXISTS ItemPool ("
                + "PlayerName TEXT NOT NULL,"
                + "PoolName TEXT NOT NULL,"
                + "ItemId INTEGER NOT NULL,"
                + "PickCount INTEGER NOT NULL DEFAULT 0,"
                + "PRIMARY KEY (PlayerName, PoolName, ItemId)"
                + ")");
        }
    }

    /// <summary>
    /// 检查玩家是否已领取某池中的某个物品（主要用于按物品模式）
    /// </summary>
    public static bool HasPicked(string playerName, string poolName, int itemId)
    {
        lock (_lock)
        {
            using var reader = TShock.DB.QueryReader(
                "SELECT PickCount FROM ItemPool WHERE PlayerName = @0 AND PoolName = @1 AND ItemId = @2",
                playerName, poolName, itemId);
            return reader.Read();
        }
    }

    /// <summary>
    /// 记录玩家领取。按物品模式：PickCount=1；按次数模式：在 ItemId=0 记录上递增
    /// </summary>
    public static void RecordPick(string playerName, string poolName, int itemId)
    {
        lock (_lock)
        {
            // 先尝试更新已存在的记录
            int affected = TShock.DB.Query(
                "UPDATE ItemPool SET PickCount = PickCount + 1 WHERE PlayerName = @0 AND PoolName = @1 AND ItemId = @2",
                playerName, poolName, itemId);

            // 如果没有更新到任何行，表示记录不存在，插入新行
            if (affected == 0)
            {
                TShock.DB.Query(
                    "INSERT INTO ItemPool (PlayerName, PoolName, ItemId, PickCount) VALUES (@0, @1, @2, 1)",
                    playerName, poolName, itemId);
            }
        }
    }

    /// <summary>
    /// 获取玩家在按次数模式下的已领取总次数
    /// </summary>
    public static int GetPickCount(string playerName, string poolName)
    {
        lock (_lock)
        {
            using var reader = TShock.DB.QueryReader(
                "SELECT PickCount FROM ItemPool WHERE PlayerName = @0 AND PoolName = @1 AND ItemId = 0",
                playerName, poolName);
            if (reader.Read())
                return reader.Get<int>("PickCount");
            return 0;
        }
    }

    /// <summary>
    /// 获取玩家在指定池中已领取的物品ID列表（按物品模式用）
    /// </summary>
    public static HashSet<int> GetPickedItems(string playerName, string poolName)
    {
        lock (_lock)
        {
            var picked = new HashSet<int>();
            using var reader = TShock.DB.QueryReader(
                "SELECT ItemId FROM ItemPool WHERE PlayerName = @0 AND PoolName = @1 AND ItemId != 0",
                playerName, poolName);
            while (reader.Read())
            {
                picked.Add(reader.Get<int>("ItemId"));
            }
            return picked;
        }
    }

    /// <summary>
    /// 重置指定玩家的领取记录（不指定池名则重置全部）
    /// </summary>
    public static int ResetPlayer(string playerName, string? poolName = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(poolName))
                return TShock.DB.Query("DELETE FROM ItemPool WHERE PlayerName = @0", playerName);
            else
                return TShock.DB.Query("DELETE FROM ItemPool WHERE PlayerName = @0 AND PoolName = @1", playerName, poolName);
        }
    }

    /// <summary>
    /// 重置所有玩家的领取记录（不指定池名则清空整个表）
    /// </summary>
    public static int ResetAll(string? poolName = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(poolName))
                return TShock.DB.Query("DELETE FROM ItemPool");
            else
                return TShock.DB.Query("DELETE FROM ItemPool WHERE PoolName = @0", poolName);
        }
    }
}
