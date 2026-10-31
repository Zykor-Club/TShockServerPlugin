using System;
using System.Collections.Generic;
using System.Linq;

namespace AIAgent.Tools;

public readonly record struct CommandSpecResult(bool Ok, string Command, string Name, string? Reason);

/// <summary>
/// 指令字符串的解析与前置校验：去前缀、拒绝换行、拒绝黑名单。
/// 真正的权限检查交给 TShock（以玩家身份执行）。
/// </summary>
public static class CommandSpec
{
    private const int MaxLength = 200;

    public static CommandSpecResult Parse(string? raw, IReadOnlyCollection<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new CommandSpecResult(false, string.Empty, string.Empty, "指令为空");
        }

        var text = raw.Trim();

        if (text.Length > MaxLength)
        {
            return new CommandSpecResult(false, string.Empty, string.Empty, "指令太长");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            return new CommandSpecResult(false, string.Empty, string.Empty, "指令不能包含换行");
        }

        while (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith(".", StringComparison.Ordinal))
        {
            text = text.Substring(1);
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return new CommandSpecResult(false, string.Empty, string.Empty, "指令为空");
        }

        var name = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

        if (blocked.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandSpecResult(false, text, name, "这个指令被禁止");
        }

        return new CommandSpecResult(true, text, name, null);
    }
}
