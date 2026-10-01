using System.Collections;
using System.Reflection;
using TerrariaApi.Server;
using TShockAPI;

namespace ZSEBot.Common;

/// <summary>
/// 通过反射桥接 ProgressControls 插件（不硬引用其程序集）。
/// 未安装 ProgressControls 时进度包里的锁定 BOSS 为空。
/// </summary>
public static class ProgressControlSupport
{
    public static bool Support { get; private set; }

    private static FieldInfo? _configField;
    private static FieldInfo? _enableField;
    private static FieldInfo? _lockTimeField;
    private static FieldInfo? _startDateField;

    public static void Init()
    {
        var pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "ProgressControls");
        if (pluginContainer is null)
        {
            return;
        }

        try
        {
            var assembly = pluginContainer.Plugin.GetType().Assembly;
            var pcontrolType = assembly.GetType("ProgressControl.PControl");
            _configField = pcontrolType?.GetField("config", BindingFlags.Public | BindingFlags.Static);
            var configType = _configField?.FieldType;
            _enableField = configType?.GetField("OpenAutoControlProgressLock", BindingFlags.Public | BindingFlags.Instance);
            _lockTimeField = configType?.GetField("ProgressLockTimeForStartServerDate", BindingFlags.Public | BindingFlags.Instance);
            _startDateField = configType?.GetField("StartServerDate", BindingFlags.Public | BindingFlags.Instance);
            Support = _configField is not null && _enableField is not null
                && _lockTimeField is not null && _startDateField is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化ProgressControls反射失败:\n{ex}");
            Support = false;
        }

        if (!Support)
        {
            TShock.Log.ConsoleError("[starZSEbot]检测到ProgressControls插件但反射绑定失败，进度锁信息不可用");
        }
    }

    public static Dictionary<string, string> GetLockBosses()
    {
        if (!Support)
        {
            throw new NotSupportedException("没有安装ProgressControls插件!");
        }

        var config = _configField!.GetValue(null)!;
        var enable = (bool)_enableField!.GetValue(config)!;
        var lockedBosses = _lockTimeField!.GetValue(config) as IDictionary;
        var initDate = (DateTime)_startDateField!.GetValue(config)!;
        var result = new Dictionary<string, string>();

        if (!enable || lockedBosses is null)
        {
            return result;
        }

        var bossIdNameToIdentity = new Dictionary<string, string>
        {
            { "史莱姆王", "King Slime" },
            { "克苏鲁之眼", "Eye of Cthulhu" },
            { "世界吞噬者", "Eater of Worlds" },
            { "克苏鲁之脑", "Brain of Cthulhu" },
            { "蜂后", "Queen Bee" },
            { "巨鹿", "Deerclops" },
            { "骷髅王", "Skeletron" },
            { "血肉墙", "Wall of Flesh" },
            { "史莱姆皇后", "Queen Slime" },
            { "双子魔眼", "The Twins" },
            { "毁灭者", "The Destroyer" },
            { "机械骷髅王", "Skeletron Prime" },
            { "世纪之花", "Plantera" },
            { "石巨人", "Golem" },
            { "猪龙鱼公爵", "Duke Fishron" },
            { "光之女皇", "Empress of Light" },
            { "拜月教教徒", "Lunatic Cultist" },
            { "月亮领主", "Moon Lord" }
        };


        foreach (DictionaryEntry lockedBoss in lockedBosses)
        {
            if (lockedBoss.Key is not string bossKey || lockedBoss.Value is not double lockHours)
            {
                continue;
            }

            if (!bossIdNameToIdentity.TryGetValue(bossKey, out var bossName))
            {
                continue;
            }

            var unlockTime = initDate + TimeSpan.FromHours(lockHours);
            if (unlockTime <= DateTime.Now)
            {
                continue;
            }

            result[bossName] = TimeFormat(unlockTime);
        }

        return result;
    }

    public static string TimeFormat(DateTime dateTime)
    {
        var today = DateTime.Today;
        var inputDateWithoutTime = dateTime.Date;

        var daysDifference = (inputDateWithoutTime - today).Days;

        if (daysDifference > 365)
        {
            return "已锁定";
        }

        // 获取本周的开始日期(周一)
        var startOfWeek = today.AddDays(-(int) today.DayOfWeek + (int) DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday)
        {
            startOfWeek = today.AddDays(-6);
        }

        // 获取下周的开始日期
        var startOfNextWeek = startOfWeek.AddDays(7);

        switch (daysDifference)
        {
            // 今天/明天/后天
            case 0:
                return dateTime.ToString("今天HH:mm");
            case 1:
                return dateTime.ToString("明天HH:mm");
            case 2:
                return dateTime.ToString("后天HH:mm");
            // 昨天/前天
            case -1:
                return dateTime.ToString("昨天HH:mm");
            case -2:
                return dateTime.ToString("前天HH:mm");
            // 本周内(周一到周日)
            case >= 0 when inputDateWithoutTime < startOfNextWeek:
                return dateTime.ToString($"周{ConvertToChineseWeekDay(dateTime.DayOfWeek)}HH:mm");
        }

        // 下周内
        if (inputDateWithoutTime >= startOfNextWeek && inputDateWithoutTime < startOfNextWeek.AddDays(7))
        {
            return dateTime.ToString($"下周{ConvertToChineseWeekDay(dateTime.DayOfWeek)}HH:mm");
        }

        // 其他情况
        return dateTime.ToString("M月d日HH:mm");
    }

// 辅助方法：将DayOfWeek转换为中文
    private static string ConvertToChineseWeekDay(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "日",
            DayOfWeek.Monday => "一",
            DayOfWeek.Tuesday => "二",
            DayOfWeek.Wednesday => "三",
            DayOfWeek.Thursday => "四",
            DayOfWeek.Friday => "五",
            DayOfWeek.Saturday => "六",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}