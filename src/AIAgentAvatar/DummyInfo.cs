extern alias TrAlias;
using System.Security.AccessControl;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FakePlayer.Plugin;
using TrColor = TrAlias.Microsoft.Xna.Framework.Color;

namespace FakePlayer;

public class FInfo
{
    [JsonProperty("名字", Order = 0)] 
    public string Name { get; set; } = string.Empty;
    [JsonProperty("密码", Order = 1)] 
    public string Password { get; set; } = Config.DefPass;
    public string UUID { get; set; } = Guid.NewGuid().ToString();
    [JsonProperty("难度", Order = 2)] 
    public byte Difficulty { get; set; } = 0;      // 0:软核,1:中核,2:硬核,3:旅途
    [JsonProperty("队伍", Order = 3)] 
    public byte Team { get; set; } = 4;  // 0:无, 1:红, 2:绿, 3:蓝, 4:黄, 5:粉
    [JsonProperty("额外饰品栏", Order = 5)] 
    public bool ExtraAccessory { get; set; } = true;
    [JsonProperty("使用生物群落火把", Order = 6)] 
    public bool UsingBiomeTorches { get; set; } = true;
    [JsonProperty("快乐火把时间", Order = 7)] 
    public bool HappyFunTorchTime { get; set; } = true;
    [JsonProperty("解锁生物群落火把", Order = 8)] 
    public bool UnlockedBiomeTorches { get; set; } = true;
    [JsonProperty("解锁超级矿车", Order = 9)] 
    public bool UnlockedSuperCart { get; set; } = true;
    [JsonProperty("启用超级矿车", Order = 10)] 
    public bool EnabledSuperCart { get; set; } = true;
    [JsonProperty("语音变体", Order = 11)] 
    public byte VoiceVariant { get; set; } = 1;
    [JsonProperty("语音音高", Order = 12)] 
    public float VoicePitchOffset { get; set; } = 0f;
    [JsonProperty("皮肤", Order = 13)] 
    public byte SkinVariant { get; set; }
    [JsonProperty("头发", Order = 14)] 
    public byte Hair { get; set; }
    [JsonProperty("染发", Order = 15)] 
    public byte HairDye { get; set; }
    [JsonProperty("隐藏设置", Order = 16)] 
    public byte HideMisc { get; set; }
    [JsonProperty("头发颜色", Order = 17)] 
    public TrColor HairColor { get; set; }
    [JsonProperty("皮肤颜色", Order = 18)] 
    public TrColor SkinColor { get; set; }
    [JsonProperty("眼睛颜色", Order = 19)] 
    public TrColor EyeColor { get; set; }
    [JsonProperty("上衣颜色", Order = 20)] 
    public TrColor ShirtColor { get; set; }
    [JsonProperty("内衣颜色", Order = 21)] 
    public TrColor UnderShirtColor { get; set; }
    [JsonProperty("裤子颜色", Order = 22)] 
    public TrColor PantsColor { get; set; }
    [JsonProperty("鞋子颜色", Order = 23)] 
    public TrColor ShoeColor { get; set; }

    public void SetRandom(int idx,string? name = null)
    {
        Name = string.IsNullOrEmpty(name) ? $"{Config.Names} {idx:D5}" : name;
        Hair = (byte)Main.rand.Next(1, 134);
        HairDye = (byte)Main.rand.Next(0, 32);
        SkinVariant = (byte)Main.rand.Next(0, 10);
        VoiceVariant = (byte)Main.rand.Next(1, 5);
        VoicePitchOffset = (float)(Main.rand.NextDouble() * 2 - 1);
        byte r = (byte)Main.rand.Next(180, 255);
        byte g = (byte)Main.rand.Next(180, 255);
        byte b = (byte)Main.rand.Next(180, 255);
        var randCol = new TrColor { R = r, G = g, B = b, A = 255 };
        HairColor = randCol;
        SkinColor = randCol;
        EyeColor = randCol;
        ShirtColor = randCol;
        UnderShirtColor = randCol;
        PantsColor = randCol;
        ShoeColor = randCol;

        // 其他默认值
        Password = Config.DefPass;
        UUID = Guid.NewGuid().ToString();
        Difficulty = 0;
        ExtraAccessory = true;
        UsingBiomeTorches = true;
        HappyFunTorchTime = true;
        UnlockedBiomeTorches = true;
        UnlockedSuperCart = true;
        EnabledSuperCart = true;
        HideMisc = 0;
    }
}

internal class DummyInfo
{
    #region 创建目录
    private static readonly string DummyDir = Path.Combine(TShock.SavePath, PluginName, "假人");
    public static string GetPath(string name) => Path.Combine(DummyDir, $"{name}.json");
    public static void Init()
    {
        if (!Directory.Exists(DummyDir))
            Directory.CreateDirectory(DummyDir);
    }
    #endregion

    #region 保存外貌模板
    public static bool Save(FInfo data)
    {
        try
        {
            Init();
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetPath(data.Name), json);
            return true;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"保存假人配置失败: {ex.Message}");
            return false;
        }
    }
    #endregion

    #region 保存所有
    public static void SaveAll()
    {
        var infos = LoadAll();
        foreach (var info in infos)
            Save(info);
    } 
    #endregion

    #region 读取指定假人外貌模板
    public static FInfo? Load(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<FInfo>(json);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"加载假人配置失败: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region 删除指定外貌假人模板
    public static bool Delete(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch { return false; }
    }
    #endregion

    #region 读取全部假人外貌模板方法
    public static List<FInfo> LoadAll()
    {
        Init();
        var files = Directory.GetFiles(DummyDir, "*.json");
        var list = new List<FInfo>();
        foreach (var f in files)
        {
            try
            {
                string json = File.ReadAllText(f);
                var info = JsonConvert.DeserializeObject<FInfo>(json);
                if (info != null) list.Add(info);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"加载假人文件失败: {f} - {ex.Message}");
            }
        }
        return list;
    }
    #endregion

    #region 设置默认方法
    public static void SetDefault()
    {
        Init();

        // 如果目录为空，生成5个随机假人
        if (Directory.GetFiles(DummyDir, "*.json").Length == 0)
        {
            for (int i = 0; i < 5; i++)
            {
                var info = new FInfo();
                info.Team = 4;
                info.SetRandom(i + 1);
                Save(info);
            }
        }
    }
    #endregion
}