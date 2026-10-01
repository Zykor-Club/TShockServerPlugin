using LinqToDB.Mapping;

namespace ZSEBot.Models;

[Table("zse_boss_kill")]
public class BossKillInfo
{
    [PrimaryKey]
    [Identity]
    [NotNull]
    [Column("id")]
    public int Id;

    [Column("account_name")]
    public string AccountName = null!;

    [Column("boss_id")]
    public int BossId;

    [Column("kill_counts")]
    public int KillCounts;
}