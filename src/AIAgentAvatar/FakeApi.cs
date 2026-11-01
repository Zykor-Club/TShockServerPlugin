using System.Linq;
using Terraria;
using TShockAPI;
using Microsoft.Xna.Framework;
using static FakePlayer.Plugin;

namespace FakePlayer;

public sealed record AvatarStatus(
    int Slot,
    string Name,
    bool Active,
    bool Playing,
    string State,
    float X,
    float Y,
    int Life,
    int LifeMax,
    bool Speaking,
    string? Follow,
    bool Holding,
    bool Stuck,
    int StuckSeconds,
    bool Attacking,
    string Goal,
    int GoalIndex,
    int GoalCount,
    bool Pvp);

/// <summary>
/// 给外部插件（AIAgent）用的门面：只管理一个"分身"。
/// 约定：0 或 1 个假人，所有操作都作用在当前分身。
/// </summary>
public static class FakeApi
{
    public static AvatarStatus? Get()
    {
        var dp = Current();
        if (dp is null) return null;

        var plr = dp.TSPlayer?.TPlayer;
        return new AvatarStatus(
            dp.PlayerSlot,
            dp.TSPlayer?.Name ?? string.Empty,
            dp.Active,
            dp.IsPlaying,
            dp.GetStatus(),
            plr?.position.X ?? 0,
            plr?.position.Y ?? 0,
            plr?.statLife ?? 0,
            plr?.statLifeMax2 ?? 0,
            dp.SpeakEnabled,
            dp.Follow?.Name,
            dp.HoldPos,
            dp.Stuck,
            dp.StuckSeconds,
            dp.Attacking,
            dp.Goal,
            dp.PatrolIndex,
            dp.PatrolPoints.Count,
            dp.PvpEnabled);
    }

    public static AvatarStatus? Spawn(string? name = null)
    {
        if (Current() is not null) return Get();

        var created = MyCmd.CreateAvatar(name ?? Config.AvatarName);
        return created is null ? null : Get();
    }

    public static bool Despawn()
    {
        var dp = Current();
        if (dp is null) return false;

        DesiredAvatar = null;
        dp.Close();
        for (var i = 0; i < Fakes.Length; i++)
        {
            if (ReferenceEquals(Fakes[i], dp)) Fakes[i] = null;
        }

        return true;
    }

    public static bool Say(string text)
    {
        var dp = Current();
        if (dp is null) return false;
        dp.Say(text);
        return true;
    }

    public static bool Follow(TSPlayer target)
    {
        var dp = Current();
        if (dp is null) return false;

                dp.Goal = "none";
        dp.PatrolPoints.Clear();
        dp.PatrolIndex = 0;
        dp.Follow = target;
        dp.HoldPos = false;
        dp.TargetNpc = -1;
        ResetStuck(dp);
        return true;
    }

    public static bool Goto(float worldX, float worldY)
    {
        var dp = Current();
        if (dp is null) return false;

                dp.Goal = "none";
        dp.PatrolPoints.Clear();
        dp.PatrolIndex = 0;
        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = true;
        dp.RoamPos = new Vector2(worldX, worldY);
        dp.NextRoamTime = System.DateTime.UtcNow.AddMinutes(10);
        ResetStuck(dp);
        return true;
    }

    public static bool Attack(int npcSlot)
    {
        var dp = Current();
        if (dp is null) return false;
        if (npcSlot < 0 || npcSlot >= Main.maxNPCs) return false;

        var npc = Main.npc[npcSlot];
        if (npc is null || !npc.active || npc.friendly) return false;

                dp.Goal = "none";
        dp.PatrolPoints.Clear();
        dp.PatrolIndex = 0;
        dp.Follow = null;
        dp.HoldPos = false;
        dp.TargetNpc = npcSlot;
        ResetStuck(dp);
        return true;
    }

    public static bool Guard(float x, float y, float radius)
    {
        var dp = Current();
        if (dp is null) return false;

        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = false;
        dp.Goal = "guard";
        dp.GoalCenter = new Vector2(x, y);
        dp.GoalRadius = Math.Max(3 * 16f, radius);
        dp.RoamPos = dp.GoalCenter;
        ResetStuck(dp);
        return true;
    }

    public static bool Patrol(float[] points)
    {
        var dp = Current();
        if (dp is null) return false;
        if (points is null || points.Length < 4 || points.Length % 2 != 0) return false;

        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = false;
        dp.Goal = "patrol";
        dp.PatrolPoints = new List<Vector2>();
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            dp.PatrolPoints.Add(new Vector2(points[i], points[i + 1]));
        }
        dp.PatrolIndex = 0;
        dp.RoamPos = dp.PatrolPoints[0];
        ResetStuck(dp);
        return true;
    }

    public static bool Return()
    {
        var dp = Current();
        if (dp is null) return false;

        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = false;
        dp.Goal = "return";
        dp.GoalCenter = new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
        dp.RoamPos = dp.GoalCenter;
        ResetStuck(dp);
        return true;
    }

    public static bool ClearGoal()
    {
        var dp = Current();
        if (dp is null) return false;

        dp.Goal = "none";
        dp.PatrolPoints.Clear();
        dp.PatrolIndex = 0;
        return true;
    }

    public static bool Stop()
    {
        var dp = Current();
        if (dp is null) return false;

        // 停止 = 原地站住，而不是继续漫游
                dp.Goal = "none";
        dp.PatrolPoints.Clear();
        dp.PatrolIndex = 0;
        var pos = dp.TSPlayer?.TPlayer?.position ?? Vector2.Zero;
        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = true;
        dp.RoamPos = pos;
        dp.NextRoamTime = System.DateTime.UtcNow.AddMinutes(10);
        ResetStuck(dp);
        return true;
    }

    public static bool SetPvp(bool enabled)
    {
        var dp = Current();
        if (dp is null) return false;

        dp.SetPvp(enabled);
        return true;
    }

    public static bool SetSpeaking(bool enabled)
    {
        var dp = Current();
        if (dp is null) return false;
        dp.SpeakEnabled = enabled;
        return true;
    }

    public static bool EquipDefaultRole()
    {
        var dp = Current();
        if (dp is null) return false;

        var role = RoleMgr.Load("默认");
        if (role is null) return false;

        MyCmd.ApplyRole(dp, role.Items);
        return true;
    }

    private static void ResetStuck(DummyPlayer dp)
    {
        dp.Stuck = false;
        dp.StuckSeconds = 0;
        dp.StuckCheckPos = dp.TSPlayer?.TPlayer?.position ?? Vector2.Zero;
    }

    private static DummyPlayer? Current()
    {
        var list = Fakes;
        if (list is null) return null;

        for (var i = 0; i < list.Length; i++)
        {
            var dp = list[i];
            if (dp is not null && dp.Active) return dp;
        }

        return null;
    }
}
