using System;
using System.Linq;
using System.Reflection;
using AIAgent.Core;

namespace AIAgent.Avatar;

public sealed record AvatarSnapshot(
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
    int GoalCount);

/// <summary>
/// 通过反射调用假人插件的 FakeApi，避免 AIAgent 硬依赖 AIAgentAvatar.dll。
/// </summary>
public static class AvatarBridge
{
    private static Type? _api;
    private static DateTime _probeAt = DateTime.MinValue;

    public static bool Available => Api() is not null;

    public static AvatarSnapshot? Status()
    {
        var value = Call("Get");
        if (value is null) return null;

        var t = value.GetType();
        object? P(string name) => t.GetProperty(name)?.GetValue(value);

        return new AvatarSnapshot(
            Convert.ToInt32(P("Slot") ?? 0),
            P("Name")?.ToString() ?? string.Empty,
            Convert.ToBoolean(P("Active") ?? false),
            Convert.ToBoolean(P("Playing") ?? false),
            P("State")?.ToString() ?? "未知",
            Convert.ToSingle(P("X") ?? 0f),
            Convert.ToSingle(P("Y") ?? 0f),
            Convert.ToInt32(P("Life") ?? 0),
            Convert.ToInt32(P("LifeMax") ?? 0),
            Convert.ToBoolean(P("Speaking") ?? false),
            P("Follow")?.ToString(),
            Convert.ToBoolean(P("Holding") ?? false),
            Convert.ToBoolean(P("Stuck") ?? false),
            Convert.ToInt32(P("StuckSeconds") ?? 0),
            Convert.ToBoolean(P("Attacking") ?? false),
            P("Goal")?.ToString() ?? "none",
            Convert.ToInt32(P("GoalIndex") ?? 0),
            Convert.ToInt32(P("GoalCount") ?? 0));
    }

    public static bool Say(string text) => Call("Say", text) is true;
    public static bool Follow(object target) => Call("Follow", target) is true;
    public static bool Goto(float x, float y) => Call("Goto", x, y) is true;
    public static bool Attack(int npcSlot) => Call("Attack", npcSlot) is true;
    public static bool Guard(float x, float y, float radius) => Call("Guard", x, y, radius) is true;
    public static bool Patrol(float[] points) => Call("Patrol", points) is true;
    public static bool Return() => Call("Return") is true;
    public static bool ClearGoal() => Call("ClearGoal") is true;
    public static bool Stop() => Call("Stop") is true;
    public static bool Despawn() => Call("Despawn") is true;
    public static bool SetSpeaking(bool enabled) => Call("SetSpeaking", enabled) is true;

    private static Type? Api()
    {
        if (_api is not null) return _api;
        if (DateTime.UtcNow - _probeAt < TimeSpan.FromSeconds(5)) return null;

        _probeAt = DateTime.UtcNow;
        _api = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType("FakePlayer.FakeApi", throwOnError: false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t is not null);

        return _api;
    }

    private static object? Call(string method, params object?[] args)
    {
        var api = Api();
        if (api is null) return null;

        var info = api.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        if (info is null) return null;

        try { return info.Invoke(null, args); }
        catch (TargetInvocationException ex)
        {
            PluginLog.Error($"分身调用失败: {method}", ex.InnerException ?? ex);
            return null;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"分身调用失败: {method}", ex);
            return null;
        }
    }
}
