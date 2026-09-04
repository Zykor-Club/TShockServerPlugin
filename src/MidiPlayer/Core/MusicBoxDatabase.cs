using TShockAPI;
using TShockAPI.DB;

namespace MidiPlayer.Core;

/// <summary>
/// 八音盒绑定数据库访问层 — 以世界唯一 ID 为键，存/取/删绑定坐标。
/// 存入 tshock.sqlite，切换世界后按 WorldId 自动恢复对应绑定。
/// </summary>
public static class MusicBoxDatabase
{
    private const string TableName = "MidiPlayerMusicBox";

    private static readonly object Lock = new();

    /// <summary>
    /// 初始化数据库表（不存在则创建）。
    /// </summary>
    public static void Initialize()
    {
        lock (Lock)
        {
            TShock.DB.Query(
                $"CREATE TABLE IF NOT EXISTS {TableName} ("
                + "WorldId INTEGER PRIMARY KEY,"
                + "X INTEGER NOT NULL,"
                + "Y INTEGER NOT NULL"
                + ")");
        }
    }

    /// <summary>
    /// 查询指定世界的八音盒绑定坐标（2×2 左上角）。无绑定返回 null。
    /// </summary>
    public static PointBinding? GetBinding(int worldId)
    {
        lock (Lock)
        {
            using var reader = TShock.DB.QueryReader(
                $"SELECT X, Y FROM {TableName} WHERE WorldId = @0", worldId);
            if (reader.Read())
                return new PointBinding(reader.Get<int>("X"), reader.Get<int>("Y"));
            return null;
        }
    }

    /// <summary>
    /// 保存（或覆盖）指定世界的八音盒绑定坐标。
    /// </summary>
    public static void SaveBinding(int worldId, int x, int y)
    {
        lock (Lock)
        {
            TShock.DB.Query(
                $"INSERT OR REPLACE INTO {TableName} (WorldId, X, Y) VALUES (@0, @1, @2)",
                worldId, x, y);
        }
    }

    /// <summary>
    /// 删除指定世界的八音盒绑定。返回是否删除成功。
    /// </summary>
    public static bool DeleteBinding(int worldId)
    {
        lock (Lock)
        {
            return TShock.DB.Query($"DELETE FROM {TableName} WHERE WorldId = @0", worldId) > 0;
        }
    }
}

/// <summary>
/// 绑定点坐标（2×2 八音盒的左上角格）。
/// </summary>
public sealed record PointBinding(int X, int Y);