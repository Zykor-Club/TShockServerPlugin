using LinqToDB;
using LinqToDB.Mapping;

namespace ZSEBot.Models;

[Table("zse_statistic")]
public class ZSECharacterInfo
{
    [PrimaryKey]
    [NotNull]
    [Column("account_name")]
    public string AccountName = null!;

    [Column("online_minute")]
    public int OnlineMinute;

    [Column("death")]
    public int Death;

    [NotColumn]
    private DateTime _lastUpdate = DateTime.Now;

    [NotColumn] public List<BossKillInfo> BossKills { get; private set; } = [];

    public static ZSECharacterInfo? GetByName(string name)
    {
        using var db = Database.Db;

        var characterInfo = db
            .GetTable<ZSECharacterInfo>()
            .FirstOrDefault(c => c.AccountName == name);


        if (characterInfo == null)
        {
            return null;
        }

        var bossInfo = db
            .GetTable<BossKillInfo>()
            .Where(c => c.AccountName == name)
            .ToList();

        characterInfo.BossKills = bossInfo;
        return characterInfo;
    }

    public void CreatOrUpdate()
    {
        using var db = Database.Db;
        this.OnlineMinute += (int) (DateTime.Now - this._lastUpdate).TotalMinutes;
        this._lastUpdate = DateTime.Now;

        db.InsertOrReplace(this);

        foreach (var bossKill in this.BossKills)
        {
            var result = db.GetTable<BossKillInfo>().FirstOrDefault(x =>
                x.AccountName == bossKill.AccountName && x.BossId == bossKill.BossId
            );

            if (result is null)
            {
                db.Insert(bossKill);
            }
            else
            {
                result.KillCounts = bossKill.KillCounts;
                db.Update(result);
            }
        }
    }

    public static void CleanAll()
    {
        using var db = Database.Db;
        db.GetTable<ZSECharacterInfo>().Delete();
        db.GetTable<BossKillInfo>().Delete();
    }
}