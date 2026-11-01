using Newtonsoft.Json;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using static FakePlayer.Plugin;

namespace FakePlayer;

#region 角色文件物品
public class FakeRole
{
    [JsonProperty("进度参考", Order = -100)]
    public List<string> Reference { get; set; } = new List<string>();
    [JsonProperty("阶段值", Order = 0)]   // 数值越大越高级，肉前<肉后<花后等
    public int Tier { get; set; } = -1;
    [JsonProperty("进度限制", Order = 1)]
    public List<string> Limit { get; set; } = new();
    [JsonProperty("物品数组", Order = 2)]
    public NetItem[] Items { get; set; } = new NetItem[99];
}
#endregion

internal static class RoleMgr
{
    #region 创建目录
    private static readonly string RoleDir = Path.Combine(TShock.SavePath, PluginName, "角色模板");
    public static string GetPath(string name) => Path.Combine(RoleDir, $"{name}.json");
    public static void Init()
    {
        if (!Directory.Exists(RoleDir))
            Directory.CreateDirectory(RoleDir);
    }
    #endregion

    #region 保存角色文件
    public static bool Save(string name, FakeRole data)
    {
        try
        {
            Init();
            data.Reference.Clear();
            data.Reference =
            [ 
                "0 无 | 1 克眼 | 2 史王 | 3 世吞 | 4 克脑 | 5 世吞或克脑 | 6 巨鹿 | 7 蜂王 | 8 骷髅王前 | 9 骷髅王后",
                "10 肉前 | 11 肉后 | 12 毁灭者 | 13 双子魔眼 | 14 机械骷髅王 | 15 世花 | 16 石巨人 | 17 史后 | 18 光女 | 19 猪鲨",
                "20 拜月 | 21 月总 | 22 哀木 | 23 南瓜王 | 24 尖叫怪 | 25 冰雪女王 | 26 圣诞坦克 | 27 火星飞碟 | 28 小丑",
                "29 日耀柱 | 30 星旋柱 | 31 星云柱 | 32 星尘柱 | 33 一王后 | 34 三王后 | 35 一柱后 | 36 四柱后",
                "37 哥布林 | 38 海盗 | 39 霜月 | 40 血月 | 41 雨天 | 42 白天 | 43 夜晚 | 44 大风天 | 45 万圣节 | 46 圣诞节 | 47 派对",
                "48 旧日一 | 49 旧日二 | 50 旧日三 | 51 醉酒种子 | 52 十周年 | 53 ftw种子 | 54 蜜蜂种子 | 55 饥荒种子",
                "56 颠倒种子 | 57 陷阱种子 | 58 天顶种子",
                "59 森林 | 60 丛林 | 61 沙漠 | 62 雪原 | 63 洞穴 | 64 海洋 | 65 地表 | 66 太空 | 67 地狱 | 68 神圣 | 69 蘑菇",
                "70 腐化 | 71 猩红 | 72 邪恶 | 73 地牢 | 74 墓地 | 75 蜂巢 | 76 神庙 | 77 沙尘暴 | 78 天空 | 79 微光",
                "80 满月 | 81 亏凸月 | 82 下弦月 | 83 残月 | 84 新月 | 85 娥眉月 | 86 上弦月 | 87 盈凸月",
                "阶段值:越大越高级,花后80>肉后79>骷髅王78,确保不会被低级角色覆盖高级角色",
                "可通过/f sv 角色名 将自己当前背包保存为角色文件",
            ];

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetPath(name), json);
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"保存角色模板失败: {ex.Message}");
            return false;
        }
    }
    #endregion

    #region 保存所有文件
    public static void SaveAll()
    {
        foreach (var name in ListNames())
        {
            var role = Load(name);
            if (role != null)
            {
                Save(name, role);
            }
        }
    }
    #endregion

    #region 读取角色文件
    public static FakeRole? Load(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<FakeRole>(json);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"加载角色模板失败: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region 删除角色文件
    public static bool Delete(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch { return false; }
    }
    #endregion

    #region 列出角色文件
    public static List<string> ListNames()
    {
        Init();
        var files = Directory.GetFiles(RoleDir, "*.json");
        var names = new List<string>();
        foreach (var f in files)
            names.Add(Path.GetFileNameWithoutExtension(f));
        return names;
    }
    #endregion

    #region 创建预设模板（使用原版槽位）
    public static void SetDefaultRoles()
    {
        if (Directory.GetFiles(RoleDir, "*.json").Length > 0) return;

        void Put(NetItem[] items, int slot, int type, int stack = 1, byte prefix = 0, bool favorited = false)
        {
            if (slot >= 0 && slot < items.Length)
                items[slot] = new NetItem(type, stack, prefix, favorited);
        }

        // 默认模板（通用）
        var def = new FakeRole();
        Put(def.Items, PlayerItemSlotID.Inventory0, ItemID.CopperShortsword);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 1, ItemID.CopperPickaxe);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 2, ItemID.CopperAxe);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 3, ItemID.WoodenHammer);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 4, ItemID.WoodPlatform, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 5, ItemID.Wood, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 6, ItemID.WoodWall, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 7, ItemID.MinecartTrack, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 8, ItemID.Rope, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 9, ItemID.Torch, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 10, ItemID.Glowstick, 999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 11, ItemID.Bomb, 9999);
        Put(def.Items, PlayerItemSlotID.Inventory0 + 12, ItemID.Acorn, 999);
        Put(def.Items, PlayerItemSlotID.Armor0, ItemID.RobotMask);
        Put(def.Items, PlayerItemSlotID.Armor0 + 1, ItemID.RobotShirt);
        Put(def.Items, PlayerItemSlotID.Armor0 + 2, ItemID.RobotPants);
        Put(def.Items, PlayerItemSlotID.Armor0 + 3, ItemID.Magiluminescence);
        Save("默认", def);

        // 战士
        var war = new FakeRole();
        Put(war.Items, PlayerItemSlotID.Inventory0, ItemID.WoodenSword);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 1, ItemID.CopperPickaxe);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 2, ItemID.CopperAxe);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 3, ItemID.WoodenHammer);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 4, ItemID.WoodPlatform, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 5, ItemID.Wood, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 6, ItemID.WoodWall, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 7, ItemID.MinecartTrack, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 8, ItemID.Rope, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 9, ItemID.Torch, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 10, ItemID.Glowstick, 999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 11, ItemID.Bomb, 9999);
        Put(war.Items, PlayerItemSlotID.Inventory0 + 12, ItemID.Acorn, 999);
        Put(war.Items, PlayerItemSlotID.Armor0, ItemID.RobotMask);
        Put(war.Items, PlayerItemSlotID.Armor0 + 1, ItemID.RobotShirt);
        Put(war.Items, PlayerItemSlotID.Armor0 + 2, ItemID.RobotPants);
        Put(war.Items, PlayerItemSlotID.Armor0 + 3, ItemID.Magiluminescence);
        Save("战士", war);

        // 射手
        var rng = new FakeRole();
        Put(rng.Items, PlayerItemSlotID.Inventory0, ItemID.IronBow);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 1, ItemID.CopperPickaxe);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 2, ItemID.CopperAxe);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 3, ItemID.WoodenHammer);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 4, ItemID.WoodPlatform, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 5, ItemID.Wood, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 6, ItemID.WoodWall, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 7, ItemID.MinecartTrack, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 8, ItemID.Rope, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 9, ItemID.Torch, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 10, ItemID.Glowstick, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 11, ItemID.Bomb, 9999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 12, ItemID.Acorn, 999);
        Put(rng.Items, PlayerItemSlotID.Inventory0 + 54, ItemID.WoodenArrow, 9999);
        Put(rng.Items, PlayerItemSlotID.Armor0, ItemID.RobotMask);
        Put(rng.Items, PlayerItemSlotID.Armor0 + 1, ItemID.RobotShirt);
        Put(rng.Items, PlayerItemSlotID.Armor0 + 2, ItemID.RobotPants);
        Put(rng.Items, PlayerItemSlotID.Armor0 + 3, ItemID.Magiluminescence);
        Save("射手", rng);

        // 法师
        var mag = new FakeRole();
        Put(mag.Items, PlayerItemSlotID.Inventory0, ItemID.WandofSparking);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 1, ItemID.CopperPickaxe);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 2, ItemID.CopperAxe);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 3, ItemID.WoodenHammer);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 4, ItemID.WoodPlatform, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 5, ItemID.Wood, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 6, ItemID.WoodWall, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 7, ItemID.MinecartTrack, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 8, ItemID.Rope, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 9, ItemID.Torch, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 10, ItemID.Glowstick, 999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 11, ItemID.Bomb, 9999);
        Put(mag.Items, PlayerItemSlotID.Inventory0 + 12, ItemID.Acorn, 999);
        Put(mag.Items, PlayerItemSlotID.Armor0, ItemID.RobotMask);
        Put(mag.Items, PlayerItemSlotID.Armor0 + 1, ItemID.RobotShirt);
        Put(mag.Items, PlayerItemSlotID.Armor0 + 2, ItemID.RobotPants);
        Put(mag.Items, PlayerItemSlotID.Armor0 + 3, ItemID.Magiluminescence);
        Save("法师", mag);

        // 召唤师
        var sum = new FakeRole();
        Put(sum.Items, PlayerItemSlotID.Inventory0, ItemID.ThornWhip);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 1, ItemID.CopperPickaxe);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 2, ItemID.CopperAxe);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 3, ItemID.WoodenHammer);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 4, ItemID.WoodPlatform, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 5, ItemID.Wood, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 6, ItemID.WoodWall, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 7, ItemID.MinecartTrack, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 8, ItemID.Rope, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 9, ItemID.Torch, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 10, ItemID.Glowstick, 999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 11, ItemID.Bomb, 9999);
        Put(sum.Items, PlayerItemSlotID.Inventory0 + 12, ItemID.Acorn, 999);
        Put(sum.Items, PlayerItemSlotID.Armor0, ItemID.RobotMask);
        Put(sum.Items, PlayerItemSlotID.Armor0 + 1, ItemID.RobotShirt);
        Put(sum.Items, PlayerItemSlotID.Armor0 + 2, ItemID.RobotPants);
        Put(sum.Items, PlayerItemSlotID.Armor0 + 3, ItemID.Magiluminescence);
        Save("召唤", sum);
    }
    #endregion
}