using System;

namespace AIAgent.Tools;

public enum ToolDenyReason
{
    None,
    NoPermission,
    NotRealPlayer,
    QuotaExceeded,
    InvalidArgs,
}

public readonly record struct ToolPolicyDecision(bool Allowed, ToolDenyReason Reason, string Message);

/// <summary>
/// 工具执行策略：把"能不能执行"从执行器里抽出来，纯函数、可单测。
/// 顺序固定：权限 → 真实玩家 → 配额 → 参数 → 确认。
/// </summary>
public static class ToolPolicy
{
    public static ToolPolicyDecision Evaluate(
        bool hasPermission,
        bool requiresRealPlayer,
        bool isRealPlayer,
        bool quotaExceeded,
        bool validationOk,
        string? validationMessage)
    {
        if (!hasPermission)
        {
            return new ToolPolicyDecision(false, ToolDenyReason.NoPermission, "没有权限");
        }

        if (requiresRealPlayer && !isRealPlayer)
        {
            return new ToolPolicyDecision(false, ToolDenyReason.NotRealPlayer, "只能在游戏内使用");
        }

        if (quotaExceeded)
        {
            return new ToolPolicyDecision(false, ToolDenyReason.QuotaExceeded, "今日次数已用完");
        }

        if (!validationOk)
        {
            return new ToolPolicyDecision(false, ToolDenyReason.InvalidArgs,
                string.IsNullOrWhiteSpace(validationMessage) ? "参数不对" : validationMessage);
        }

        return new ToolPolicyDecision(true, ToolDenyReason.None, string.Empty);
    }
}
