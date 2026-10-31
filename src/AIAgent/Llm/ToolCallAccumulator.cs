using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AIAgent.Llm;

/// <summary>
/// 流式 tool_calls 是分片下发的：同一个 index 会收到多次 delta，
/// name 通常只在第一片，arguments 会被切成多段字符串。必须按 index 聚合。
/// </summary>
public sealed class ToolCallAccumulator
{
    private sealed class Builder
    {
        public string Id = string.Empty;
        public StringBuilder Name = new();
        public StringBuilder Arguments = new();
    }

    private readonly SortedDictionary<int, Builder> _builders = new();

    public void Add(JsonElement toolCallDelta)
    {
        if (toolCallDelta.ValueKind != JsonValueKind.Object) return;
        if (!toolCallDelta.TryGetProperty("index", out var indexEl) || !indexEl.TryGetInt32(out var index))
        {
            index = _builders.Count;
        }

        if (!_builders.TryGetValue(index, out var builder))
        {
            builder = new Builder();
            _builders[index] = builder;
        }

        if (toolCallDelta.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var id = idEl.GetString();
            if (!string.IsNullOrEmpty(id)) builder.Id = id;
        }

        if (!toolCallDelta.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object) return;

        if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            builder.Name.Append(nameEl.GetString());
        }

        if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
        {
            builder.Arguments.Append(argsEl.GetString());
        }
    }

    public List<ToolCall> Build()
    {
        var result = new List<ToolCall>(_builders.Count);
        foreach (var builder in _builders.Values)
        {
            result.Add(new ToolCall
            {
                Id = builder.Id,
                Name = builder.Name.ToString(),
                Arguments = builder.Arguments.ToString(),
            });
        }
        return result;
    }

    public bool Any => _builders.Count > 0;
}
