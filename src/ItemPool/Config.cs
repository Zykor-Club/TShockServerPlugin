using Newtonsoft.Json;
using TShockAPI;

namespace ItemPool;

/// <summary>
/// 配置文件模型 — 定义物品池的结构
/// </summary>
public class ItemPoolConfig
{
    private const string ConfigPath = "tshock/ItemPool.json";

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly object _lock = new();

    public static ItemPoolConfig Instance = new();

    [JsonProperty("物品池列表")]
    public List<ItemPoolEntry> 物品池列表 { get; set; } = new();

    /// <summary>
    /// 从硬盘读取配置文件，不存在则生成默认配置
    /// </summary>
    public static void Read()
    {
        lock (_lock)
        {
            ItemPoolConfig result;
            if (!File.Exists(ConfigPath))
            {
                result = CreateDefault();
                result.Write();
            }
            else
            {
                using FileStream fs = new(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using StreamReader sr = new(fs);
                result = JsonConvert.DeserializeObject<ItemPoolConfig>(sr.ReadToEnd(), JsonSettings)!;
                if (result == null)
                    result = new ItemPoolConfig();
            }

            // 将配置文件中的默认启用状态同步到运行时开关
            foreach (var pool in result.物品池列表)
            {
                pool.运行时启用 = pool.默认启用;
            }
            Instance = result;
        }
    }

    /// <summary>
    /// 将当前配置写入硬盘
    /// </summary>
    public void Write()
    {
        lock (_lock)
        {
            using FileStream fs = new(ConfigPath, FileMode.Create, FileAccess.Write, FileShare.Write);
            using StreamWriter sw = new(fs);
            sw.Write(JsonConvert.SerializeObject(this, JsonSettings));
        }
    }

    /// <summary>
    /// 创建默认配置文件（含示例池）
    /// </summary>
    private static ItemPoolConfig CreateDefault()
    {
        return new ItemPoolConfig
        {
            物品池列表 = new List<ItemPoolEntry>
            {
                new()
                {
                    池名称 = "新手礼包",
                    模式 = "按物品",
                    最大领取次数 = 0,
                    说明 = "新手一次性礼包，可选一件强力武器",
                    默认启用 = true,
                    物品列表 = new List<PoolItem>
                    {
                        new() { 物品ID = 757, 数量 = 1, 前缀 = 0 },
                        new() { 物品ID = 1553, 数量 = 1, 前缀 = 0 },
                        new() { 物品ID = 4953, 数量 = 1, 前缀 = 0 }
                    }
                },
                new()
                {
                    池名称 = "建筑材料包",
                    模式 = "按次数",
                    最大领取次数 = 3,
                    说明 = "每人可选 3 种建筑材料",
                    默认启用 = true,
                    物品列表 = new List<PoolItem>
                    {
                        new() { 物品ID = 2, 数量 = 999, 前缀 = 0 },
                        new() { 物品ID = 8, 数量 = 999, 前缀 = 0 },
                        new() { 物品ID = 424, 数量 = 999, 前缀 = 0 },
                        new() { 物品ID = 30, 数量 = 999, 前缀 = 0 }
                    }
                }
            }
        };
    }

    /// <summary>
    /// 按名称查找物品池（大小写不敏感部分匹配）
    /// </summary>
    public static ItemPoolEntry? FindPool(string name)
    {
        lock (_lock)
        {
            return Instance.物品池列表.FirstOrDefault(
                p => p.池名称.Equals(name, StringComparison.OrdinalIgnoreCase)
                     || p.池名称.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                     || p.池名称.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}

/// <summary>
/// 单个物品池的定义
/// </summary>
public class ItemPoolEntry
{
    [JsonProperty("池名称")]
    public string 池名称 { get; set; } = "";

    [JsonProperty("模式")]
    public string 模式 { get; set; } = "按物品";

    [JsonProperty("最大领取次数")]
    public int 最大领取次数 { get; set; }

    [JsonProperty("说明")]
    public string 说明 { get; set; } = "";

    [JsonProperty("默认启用")]
    public bool 默认启用 { get; set; } = true;

    [JsonProperty("物品列表")]
    public List<PoolItem> 物品列表 { get; set; } = new();

    /// <summary>
    /// 运行时开关状态（不序列化到文件）
    /// </summary>
    [JsonIgnore]
    public bool 运行时启用 { get; set; } = true;

    /// <summary>
    /// 判断是否为"按物品"模式
    /// </summary>
    [JsonIgnore]
    public bool 是否按物品模式 => 模式 == "按物品";

    /// <summary>
    /// 获取模式的中文描述
    /// </summary>
    [JsonIgnore]
    public string 模式描述 => 是否按物品模式
        ? "每件物品限领一次"
        : 最大领取次数 > 0
            ? $"每人最多领取 {最大领取次数} 次"
            : "不限领取次数";
}

/// <summary>
/// 池中的单个物品定义
/// </summary>
public class PoolItem
{
    [JsonProperty("物品ID")]
    public int 物品ID { get; set; }

    [JsonProperty("数量")]
    public int 数量 { get; set; } = 1;

    [JsonProperty("前缀")]
    public int 前缀 { get; set; }
}
