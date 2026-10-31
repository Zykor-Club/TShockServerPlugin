namespace AIAgent.Core;

/// <summary>
/// 一次对话的主体。记忆以账号为键（角色名会改），未登录时退化为角色名。
/// </summary>
public sealed record Subject(string AccountName, string DisplayName, string? GroupName, bool IsLoggedIn)
{
    public string Key => IsLoggedIn && !string.IsNullOrWhiteSpace(AccountName) ? AccountName : DisplayName;

    public override string ToString() => IsLoggedIn ? $"{DisplayName}({AccountName})" : DisplayName;
}
