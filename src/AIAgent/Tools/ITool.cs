using System.Text.Json;
using System.Text.Json.Nodes;
using TShockAPI;

namespace AIAgent.Tools;

public readonly record struct ToolValidation(bool Ok, string? Reason);

public readonly record struct ToolResult(bool Ok, string PayloadJson);

/// <summary>
/// 工具契约。参数必须强类型、可校验；执行只在主线程进行。
/// </summary>
public interface ITool
{
    string Name { get; }

    string Description { get; }

    string Permission { get; }

    bool RequiresRealPlayer { get; }

    JsonObject BuildParameters();

    ToolValidation Validate(JsonElement args, TSPlayer caller);

    ToolResult Execute(JsonElement args, TSPlayer caller);

    /// <summary>给玩家看的描述，用在确认提示里。</summary>
    string Describe(JsonElement args) => Name;

    /// <summary>配置决定这个工具是否对当前服务器开放。</summary>
    bool IsAvailable(Config.PluginConfig cfg) => true;
}

internal static class ToolArgs
{
    public static int? Int(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    public static string? Str(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static TSPlayer? FindOnline(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return TShockAPI.TShock.Players.FirstOrDefault(p =>
            p is { Active: true } && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string Ok(object payload) => JsonSerializer.Serialize(payload);
}
