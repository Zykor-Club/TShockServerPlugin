using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;

namespace AIAgent.Core;

/// <summary>
/// 以槽位索引为主键的玩家登记表。
///
/// 为什么不用 name 做键：TShock 在触发 ServerLeave 之前已经把 Players[who] 置为 null，
/// 插件在离开钩子里拿不到人了（旧版就是这个坑，导致清理永远不执行）。
/// 所以必须在玩家进来时自己记一份 index -> 身份 的映射。
/// </summary>
public sealed class PlayerRegistry
{
    private sealed class Slot
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? AccountName { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.Now;
    }

    private readonly ConcurrentDictionary<int, Slot> _slots = new();

    public int Count => _slots.Count;

    public IReadOnlyList<string> Snapshot()
    {
        return _slots
            .OrderBy(kv => kv.Key)
            .Select(kv => string.IsNullOrWhiteSpace(kv.Value.AccountName)
                ? $"#{kv.Key} {kv.Value.DisplayName}"
                : $"#{kv.Key} {kv.Value.DisplayName}({kv.Value.AccountName})")
            .ToList();
    }

    public void OnJoin(int who) => _slots[who] = new Slot();

    public void OnGreet(int who, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return;
        Update(who, slot => slot.DisplayName = displayName);
    }

    public void OnPostLogin(TSPlayer player)
    {
        if (player is null) return;
        var account = player.Account?.Name;
        if (string.IsNullOrWhiteSpace(account)) return;
        Update(player.Index, slot => slot.AccountName = account);
    }

    public void OnLogout(TSPlayer player)
    {
        if (player is null) return;
        Update(player.Index, slot => slot.AccountName = null);
    }

    /// <summary>玩家离开：取出身份并从表中移除，返回值可用于落盘记忆等收尾工作。</summary>
    public (string DisplayName, string? AccountName)? Remove(int who)
    {
        if (!_slots.TryRemove(who, out var slot)) return null;
        return (slot.DisplayName, slot.AccountName);
    }

    /// <summary>命令入口顺手刷新一次（应对改名、补登录等场景）。</summary>
    public void Touch(TSPlayer player)
    {
        if (player is null || !player.Active) return;

        var name = player.Name;
        var account = player.Account?.Name;

        Update(player.Index, slot =>
        {
            if (!string.IsNullOrWhiteSpace(name)) slot.DisplayName = name;
            if (!string.IsNullOrWhiteSpace(account)) slot.AccountName = account;
        });
    }

    public Subject? Resolve(TSPlayer player)
    {
        if (player is null) return null;
        var name = string.IsNullOrWhiteSpace(player.Name) ? $"#{player.Index}" : player.Name;
        var account = player.Account?.Name;
        var group = player.Group?.Name;
        return new Subject(account ?? string.Empty, name, group, !string.IsNullOrWhiteSpace(account));
    }

    private void Update(int index, Action<Slot> mutate)
    {
        var slot = _slots.GetOrAdd(index, _ => new Slot());
        lock (slot)
        {
            mutate(slot);
        }
    }
}
