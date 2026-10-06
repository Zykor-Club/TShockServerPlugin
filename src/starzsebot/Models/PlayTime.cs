using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;

namespace ZSEBot.Models;

/// <summary>
/// 累计在线时长（**永不重置**，与 zse_statistic.online_minute 不同：后者换世界会清零）。
/// 用于"总在线时长"展示与跨服合并（机器人侧按账号汇总，一个联合体共用）。
/// </summary>
[Table("zse_playtime")]
public class PlayTime
{
    [PrimaryKey]
    [NotNull]
    [Column("account_name")]
    public string AccountName = null!;

    [Column("seconds")]
    public long Seconds;

    [Column("updated_at")]
    public long UpdatedAt;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>给某账号累加在线秒数（不存在则插入）；只加不减</summary>
    internal static void Add(string account, long seconds)
    {
        if (string.IsNullOrWhiteSpace(account) || seconds <= 0)
        {
            return;
        }

        try
        {
            using var db = Database.Db;
            var affected = db.GetTable<PlayTime>()
                .Where(p => p.AccountName == account)
                .Set(p => p.Seconds, p => p.Seconds + seconds)
                .Set(p => p.UpdatedAt, Now())
                .Update();
            if (affected == 0)
            {
                try
                {
                    db.Insert(new PlayTime { AccountName = account, Seconds = seconds, UpdatedAt = Now() });
                }
                catch
                {
                    // 并发插入冲突 → 回到 UPDATE
                    db.GetTable<PlayTime>()
                        .Where(p => p.AccountName == account)
                        .Set(p => p.Seconds, p => p.Seconds + seconds)
                        .Set(p => p.UpdatedAt, Now())
                        .Update();
                }
            }
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.ConsoleError($"[starZSEbot]累加在线时长失败({account}): {ex.Message}");
        }
    }

    internal static List<PlayTime> All()
    {
        try
        {
            using var db = Database.Db;
            return db.GetTable<PlayTime>().OrderByDescending(p => p.Seconds).ToList();
        }
        catch (Exception ex)
        {
            TShockAPI.TShock.Log.ConsoleError($"[starZSEbot]读取在线时长失败: {ex.Message}");
            return [];
        }
    }
}
