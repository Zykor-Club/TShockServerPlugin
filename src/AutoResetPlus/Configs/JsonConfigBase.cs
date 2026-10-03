using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using TShockAPI;
using TShockAPI.Hooks;

namespace AutoResetPlus.Configs;

// 自研 JSON 配置基类：替代 LazyAPI 的 JsonConfigBase。
// 行为对齐原实现：按当前语言命名配置文件（如 AutoReset.zh-CN.json）、
// 按语言本地化属性名、文件缺失时写入默认值。
public abstract class JsonConfigBase<T> where T : JsonConfigBase<T>, new()
{
    private static T? _instance;

    private static JsonSerializerSettings? _settings;

    private static CultureInfo? _culture;

    // 配置文件相对 TShock.SavePath 的路径（不含语言后缀）
    protected virtual string Filename => typeof(T).Namespace ?? typeof(T).Name;

    // 子类在此填充默认值（首次生成配置文件时调用）
    protected virtual void SetDefault() { }

    private string FullFilename => Path.Combine(TShock.SavePath, $"{Filename}.{Culture.Name}.json");

    private static CultureInfo Culture => _culture ??= ResolveCulture();

    private static JsonSerializerSettings Settings => _settings ??= new JsonSerializerSettings
    {
        ContractResolver = new CultureContractResolver(Culture.Name),
        Formatting = Formatting.Indented
    };

    // 与 TShock 自身语言保持一致，读不到时回退英文
    private static CultureInfo ResolveCulture()
    {
        var info = typeof(TShock).Assembly.GetType("TShockAPI.I18n")
            ?.GetProperty("TranslationCultureInfo", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as CultureInfo;
        return string.IsNullOrEmpty(info?.Name) ? new CultureInfo("en-US") : info!;
    }

    private static T GetConfig()
    {
        var config = new T();
        var file = config.FullFilename;
        if (File.Exists(file))
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(file), Settings) ?? config;
            }
            catch (Exception ex)
            {
                // 文件损坏时不阻塞插件加载，直接重建为默认配置
                TShock.Log.ConsoleError(GetString($"[AutoResetPlus]读取配置 {file} 失败，已重建默认配置：{ex.Message}"));
                config.SetDefault();
                config.SaveTo();
                return config;
            }
        }

        config.SetDefault();
        config.SaveTo();
        return config;
    }

    public virtual void SaveTo(string? path = null)
    {
        var filepath = path ?? FullFilename;
        var dirPath = Path.GetDirectoryName(filepath);
        if (!string.IsNullOrEmpty(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        File.WriteAllText(filepath, JsonConvert.SerializeObject(this, Settings));
    }

    public static void Save() => Instance.SaveTo();

    // 加载配置并注册 /reload 重载回调
    public static void Load()
    {
        GeneralHooks.ReloadEvent += args =>
        {
            _instance = GetConfig();
            args.Player.SendSuccessMessage(GetString($"[{_instance.Filename}.{Culture.Name}.json] 配置已重载"));
        };
        _ = Instance;
    }

    public static T Instance => _instance ??= GetConfig();
}