using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TShockAPI;
using AIAgent.Crafting;
using AIAgent.Core;

namespace AIAgent.Tools.Builtin;

/// <summary>读取世界 Boss 击败进度。</summary>
public sealed class ProgressTool : ITool
{
    public string Name => "get_world_progress";

    public string Description =>
        "查看当前世界的 Boss 击败进度：难度、是否进入困难模式、已击败哪些 Boss、还差哪个阶段、可选 Boss 有没有打。" +
        "玩家问“我打到哪了/进度如何/下一个该打谁/XX 打过没”时调用。";

    public string Permission => "aiagent.use";


    public bool RequiresRealPlayer => false;

    public JsonObject BuildParameters() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    public ToolValidation Validate(JsonElement args, TSPlayer caller) => new(true, null);

    public ToolResult Execute(JsonElement args, TSPlayer caller)
    {
        var snap = WorldProgress.Snapshot();

        return new ToolResult(true, ToolArgs.Ok(new
        {
            difficulty = snap.Difficulty,
            hardMode = snap.HardMode,
            stage = snap.Stage,
            defeated = snap.Defeated.ToArray(),
            optionalMissing = snap.OptionalMissing.ToArray(),
            pillars = snap.Pillars.ToDictionary(kv => kv.Key, kv => kv.Value),
        }));
    }
}
