using AIAgent.Config;
using AIAgent.Llm;
using AIAgent.Memory;
using AIAgent.Tools;
using AIAgent.Crafting;
using AIAgent.Wiki;

namespace AIAgent.Core;

/// <summary>
/// 插件全局状态。集中放这里，避免各处 new。
/// 配置对象整体替换（不就地改字段），防止并发读到半更新状态。
/// </summary>
public static class PluginState
{
    public static string DataDirectory { get; internal set; } = string.Empty;

    public static ConfigStore Store { get; internal set; } = null!;

    public static PluginConfig Config { get; internal set; } = new();

    public static MainThreadQueue MainThread { get; } = new();

    public static PlayerRegistry Players { get; } = new();

    public static RateLimiter Limiter { get; } = new();

    public static UsageMeter Usage { get; } = new();

    public static SessionStore Sessions { get; internal set; } = null!;

    public static JsonMemoryStore Memory { get; internal set; } = null!;

    public static MemoryExtractor Extractor { get; internal set; } = null!;

    public static SessionCompressor Compressor { get; internal set; } = null!;

    public static ToolInvoker Tools { get; internal set; } = null!;

    public static WikiStore Wiki { get; internal set; } = null!;

    private static Lazy<RecipeIndex> _recipes = new(() => RecipeIndex.Build(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>配方索引懒加载：首次使用时才建，避免插件初始化时配方表还没准备好。</summary>
    public static RecipeIndex Recipes => _recipes.Value;

    public static string Persona { get; internal set; } = string.Empty;

    public static int ReqId;

    public static string ConfigPath => Store.Path;
}
