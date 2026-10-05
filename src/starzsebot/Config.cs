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

    /// <summary>
    /// 固定服务器证书指纹（默认开）。机房常按 TLS SNI 域名做「过白」拦截，
    /// 未过白就重置连接 → 我们用 IP 直连（.NET 对 IP 字面量不发 SNI）绕开，
    /// 但此时证书域名与 IP 不匹配，不能走默认校验，改用固定指纹（首次连接自动记住 = TOFU）。
    /// </summary>
    [JsonProperty("固定证书指纹")]
    public bool PinCertificate = true;

    [JsonProperty("证书指纹")]
    public string CertificateFingerprint = "";

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
    /// 上次记录的世界 ID（= Terraria Main.worldID）：WorldResetGuard 用它判定「换了世界」，
    /// 换了就清零排行统计。必须持久化，否则「重置后服务器重启」这条路径判定不出来。
    /// 0 表示尚未记录（新装/升级后首次运行，只记录不清理）。
    /// </summary>
    [JsonProperty("上次世界ID")]
    public int LastWorldId;

    /// <summary>定时自动备份存档（导出 zip 落到 tshock/starZSEBot/Exports，只保留最近若干份）</summary>
    [JsonProperty("自动备份开关")]
    public bool AutoBackup = true;

    /// <summary>自动备份间隔（小时），0 = 关闭（改配置后重载即生效）</summary>
    [JsonProperty("自动备份间隔小时")]
    public double BackupIntervalHours = 6;

    /// <summary>保留最近几份备份（含重置前自动导出的那份），超出的最旧文件会被删除</summary>
    [JsonProperty("备份保留份数")]
    public int BackupKeep = 10;

    /// <summary>重置生成新世界时的难度：经典/专家/大师/旅行；留空 = 不干预（跟随当前）</summary>
    [JsonProperty("地图难度")]
    public string WorldDifficulty = "";

    /// <summary>重置生成新世界时的大小：小/中/大；留空 = 不干预</summary>
    [JsonProperty("世界大小")]
    public string WorldSize = "";

    /// <summary>重置生成新世界时的邪恶环境：腐化/猩红；留空 = 不干预</summary>
    [JsonProperty("邪恶环境")]
    public string WorldEvil = "";


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