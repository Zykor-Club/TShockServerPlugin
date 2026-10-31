using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using TShockAPI;
using AIAgent.Core;

namespace AIAgent.Tools;

/// <summary>
/// 工具注册表。按玩家权限裁剪后再发给模型——没权限的工具根本不出现在 tools 里，
/// 从源头消除"模型提议了但执行不了"的越权路径。
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _map = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ITool> All => _map.Values;

    public void Register(ITool tool) => _map[tool.Name] = tool;

    public ITool? Find(string name) => _map.TryGetValue(name, out var tool) ? tool : null;

    public List<ITool> For(TSPlayer player) => For(player.HasPermission);

    /// <summary>按权限谓词裁剪（便于测试，也不绑死 TSPlayer）。</summary>
    public List<ITool> For(Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(hasPermission);

        var cfg = PluginState.Config;
        return _map.Values
            .Where(tool => tool.IsAvailable(cfg))
            .Where(tool => hasPermission(tool.Permission))
            .Where(tool => !cfg.Tools.TryGetValue(tool.Name, out var settings) || settings.Enabled)
            .ToList();
    }

    public JsonArray BuildSchemaFor(TSPlayer player)
    {
        var array = new JsonArray();
        foreach (var tool in For(player))
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.BuildParameters(),
                },
            });
        }
        return array;
    }
}
