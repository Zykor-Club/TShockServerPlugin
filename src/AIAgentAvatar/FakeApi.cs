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
    bool Holding);

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
            dp.HoldPos);
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

        dp.Follow = target;
        dp.HoldPos = false;
        dp.TargetNpc = -1;
        return true;
    }

    public static bool Goto(float worldX, float worldY)
    {
        var dp = Current();
        if (dp is null) return false;

        dp.Follow = null;
        dp.TargetNpc = -1;
        dp.HoldPos = true;
        dp.RoamPos = new Vector2(worldX, worldY);
        dp.NextRoamTime = System.DateTime.UtcNow.AddMinutes(10);
        return true;
    }

    public static bool Attack(int npcSlot)
    {
        var dp = Current();
        if (dp is null) return false;
        if (npcSlot < 0 || npcSlot >= Main.maxNPCs) return false;

        var npc = Main.npc[npcSlot];
        if (npc is null || !npc.active || npc.friendly) return false;

        dp.Follow = null;
        dp.HoldPos = false;
        dp.TargetNpc = npcSlot;
        return true;
    }

    public static bool Stop()
    {
        var dp = Current();
        if (dp is null) return false;

        dp.Follow = null;
        dp.HoldPos = false;
        dp.TargetNpc = -1;
        dp.RoamPos = Vector2.Zero;
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
