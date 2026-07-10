using Newtonsoft.Json;
using TShockAPI;

namespace CustomDeathMessages;

public class Configuration
{
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "CustomDeathMessages.json");

    [JsonProperty("死亡消息模板")]
    public Dictionary<string, DeathCategoryConfig> Messages { get; set; } = new()
    {
        ["摔死"] = new() { Messages = new() { "[i:321]{Player} 摔成了一滩。", "[i:321]{Player} 从高处自由落体。", "[i:321]{Player} 低估了重力。" } },
        ["溺水"] = new() { Messages = new() { "[i:321]{Player} 溺水了。", "[i:321]{Player} 在水里失去了意识。" } },
        ["岩浆"] = new() { Messages = new() { "[i:321]{Player} 试图在岩浆里游泳。", "[i:321]{Player} 在岩浆中融化了。" } },
        ["普通"] = new() { Messages = new() { "[i:321]{Player} 被干掉了。", "[i:321]{Player} 死了。" } },
        ["击杀"] = new() { Messages = new() { "[i:321]{Player} 被击杀了。", "[i:321]{Player} 倒下了。" } },
        ["石化"] = new() { Messages = new() { "[i:321]{Player} 石化后摔碎了。", "[i:321]{Player} 变成了碎石。" } },
        ["刺穿"] = new() { Messages = new() { "[i:321]{Player} 被刺穿了。", "[i:321]{Player} 被戳死了。" } },
        ["窒息"] = new() { Messages = new() { "[i:321]{Player} 窒息了。", "[i:321]{Player} 喘不过气来。" } },
        ["烧死"] = new() { Messages = new() { "[i:321]{Player} 被烧死了。", "[i:321]{Player} 在火焰中化为灰烬。" } },
        ["中毒"] = new() { Messages = new() { "[i:321]{Player} 中毒身亡。", "[i:321]{Player} 被毒死了。" } },
        ["触电"] = new() { Messages = new() { "[i:321]{Player} 触电了。", "[i:321]{Player} 被电焦了。" } },
        ["逃离血肉墙"] = new() { Messages = new() { "[i:321]{Player} 试图逃离血肉墙。", "[i:321]{Player} 没能逃出地狱。" } },
        ["被舔"] = new() { Messages = new() { "[i:321]{Player} 被舔了一口。", "[i:321]{Player} 被黏糊糊的舌头带走了。" } },
        ["传送"] = new() { Messages = new() { "[i:321]{Player} 传送事故。", "[i:321]{Player} 在传送中迷失了。" } },
        ["炼狱之火"] = new() { Messages = new() { "[i:321]{Player} 在炼狱之火中烧成灰。", "[i:321]{Player} 被地狱烈焰吞噬了。" } },
        ["黑暗吞噬"] = new() { Messages = new() { "[i:321]{Player} 在黑暗中被吞噬了。", "[i:321]{Player} 消失在黑暗中。" } },
        ["饥饿"] = new() { Messages = new() { "[i:321]{Player} 饿死了。", "[i:321]{Player} 因饥饿而倒下。" } },
        ["太空"] = new() { Messages = new() { "[i:321]{Player} 飞到了外太空。", "[i:321]{Player} 脱离了大气层。" } },
        ["挡刀"] = new() { Messages = new() { "[i:321]{Player} 替队友挡了致命一击。", "[i:321]{Player} 为队友牺牲了。" } },
        ["深渊"] = new() { Messages = new() { "[i:321]{Player} 掉到了世界底部。", "[i:321]{Player} 坠入了深渊。" } },
        ["吸血鬼自燃"] = new() { Messages = new() { "[i:321]{Player} 在阳光下自燃了。", "[i:321]{Player} 被阳光烧成了灰。" } },

        ["PVP击杀"] = new() { Messages = new() { "[i:757]{Player} 被 {Killer} 干掉了！", "[i:757]{Player} 被 {Killer} 击杀了。", "[i:757]{Player} 被 {Killer} 打得落花流水。" } },
        ["PVP弹幕击杀"] = new() { Messages = new() { "[i:757]{Player} 被 {Killer} 用 {Projectile} 射杀了！", "[i:757]{Player} 被 {Killer} 的 {Projectile} 击倒了。" } },
        ["NPC击杀"] = new() { Color = "205,133,63", Messages = new() { "[i:320]{Player} 被 {NPC} 杀死了。", "[i:320]{Player} 被 {NPC} 击败了。", "[i:320]{Player} 成了 {NPC} 的盘中餐。" } },
        ["弹幕击杀"] = new() { Messages = new() { "{Player} 被弹幕击中身亡。", "{Player} 被 {Projectile} 击中了。" } },
        ["自定义"] = new() { Messages = new() { "[i:321]{Player} {CustomReason}" } },

        ["未知"] = new() { Messages = new() { "[i:321]{Player} 死了。", "[i:321]{Player} 倒下了。" } },
    };

    [JsonProperty("死亡次数公告")]
    public Dictionary<int, string> DeathMilestones { get; set; } = new()
    {
        [10] = "公告：{Player} 已经死亡 10 次了！",
        [50] = "公告：{Player} 已经死亡 50 次了。",
        [100] = "公告：{Player} 已经死亡 100 次！",
        [500] = "公告：{Player} 已经死亡 500 次，真是坚持不懈！",
        [1000] = "公告：{Player} 已经死亡 1000 次！",
    };

    public class DeathCategoryConfig
    {

        [JsonProperty("颜色（RGB值")]
        public string Color { get; set; } = "";

        [JsonProperty("消息")]
        public List<string> Messages { get; set; } = new();
    }

    public static Configuration Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var config = new Configuration();
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
            TShock.Log.ConsoleInfo("[CustomDeathMessages] 已创建默认配置文件: " + ConfigPath);
            return config;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonConvert.DeserializeObject<Configuration>(json) ?? new Configuration();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError("[CustomDeathMessages] 配置文件加载失败，使用默认配置: " + ex.Message);
            return new Configuration();
        }
    }
}
