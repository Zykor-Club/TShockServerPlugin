using Newtonsoft.Json;
using System.IO;
using TShockAPI;
namespace ZSEBot;

public class Config
{
    private static string ConfigPath = Path.Combine(TShock.SavePath, "starZSEbot.json");
    // 读/写共用同一把锁（防止重载与写盘交错）；volatile 保证换实例后其他线程立即可见，
    // 读者拿到的永远是「完整的新实例或完整的旧实例」，不会读到半写状态
    private static readonly object FileLock = new ();
    public static volatile Config Settings = new ();

    [JsonProperty("白名单开关")]
    public bool WhiteList = true;

    [JsonProperty("服务器地址")]
    public string ServerUrl = "api.terraria.ink:22338";

    [JsonProperty("启用TLS")]
    public bool UseTls = true;

    [JsonProperty("密钥")]
    public string Token = "";

    [JsonProperty("群OpenID")]
    public string GroupOpenId = "114514";

    [JsonProperty("在线显示进度")]
    public bool ShowProcessInPlayerList = true;

    [JsonProperty("商店分组标签")]
    public string ShopTag = "生存服";

    [JsonProperty("白名单拦截提示的群号")]
    public long GroupNumber;


    /// <summary>
    /// 将配置文件写入硬盘
    /// </summary>
    internal void Write()
    {
        lock (FileLock)
        {
            using FileStream fileStream = new (ConfigPath, FileMode.Create, FileAccess.Write, FileShare.Write);
            using StreamWriter streamWriter = new (fileStream);
            streamWriter.Write(JsonConvert.SerializeObject(this, JsonSettings));
        }
    }

    /// <summary>
    /// 从硬盘读取配置文件
    /// </summary>
    internal void Read()
    {
        lock (FileLock)
        {
            Config result;
            if (!File.Exists(ConfigPath))
            {
                result = new Config();
                result.Write();
            }
            else
            {
                using FileStream fileStream = new (ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using StreamReader streamReader = new (fileStream);
                result = JsonConvert.DeserializeObject<Config>(streamReader.ReadToEnd(), JsonSettings)!;
            }

            // 整体替换（引用赋值）：读者要么看到旧配置、要么看到新配置，配合 volatile 保证可见性
            Settings = result;
        }
    }

    private static readonly JsonSerializerSettings JsonSettings = new () { Formatting = Formatting.Indented, ObjectCreationHandling = ObjectCreationHandling.Replace };
}