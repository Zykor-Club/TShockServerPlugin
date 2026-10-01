using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using TerrariaApi.Server;
using TShockAPI;
using ZSEBot.Enums;

namespace ZSEBot.Common;

/// <summary>
/// 通过反射桥接 Economics.Core / Economics.RPG / Economics.Skill（不硬引用其程序集）。
/// 未安装对应插件时相关功能自动降级。
/// </summary>
public static class EconomicSupport
{
    public static bool GetCoinsSupport { get; private set; }
    public static bool GetLevelNameSupport { get; private set; }
    public static bool GetSkillSupport { get; private set; }

    // ---- Economics.Core ----
    private static PropertyInfo? _settingInstance;
    private static FieldInfo? _currenciesField;
    private static PropertyInfo? _currencyName;
    private static PropertyInfo? _currencyService;
    private static MethodInfo? _getBalance;
    private static PropertyInfo? _balanceIsSuccess;
    private static PropertyInfo? _balanceValue;
    private static MethodInfo? _getAllCurrencyRecords;
    private static PropertyInfo? _recordCurrencyType;
    private static PropertyInfo? _recordNumber;
    private static PropertyInfo? _recordPlayerName;

    // ---- Economics.RPG ----
    private static PropertyInfo? _playerLevelManager;
    private static MethodInfo? _getLevel;
    private static PropertyInfo? _levelName;

    // ---- Economics.Skill ----
    private static PropertyInfo? _playerSkillManager;
    private static MethodInfo? _querySkill;
    private static PropertyInfo? _playerSkillSkill;
    private static PropertyInfo? _skillContextName;

    public static void Init()
    {
        var pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "Economics.Core");
        if (pluginContainer is not null)
        {
            GetCoinsSupport = TryBindCore(pluginContainer.Plugin.GetType().Assembly);
        }

        pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "Economics.RPG");
        if (pluginContainer is not null)
        {
            GetLevelNameSupport = TryBindRpg(pluginContainer.Plugin.GetType().Assembly);
        }

        pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "Economics.Skill");
        if (pluginContainer is not null)
        {
            GetSkillSupport = TryBindSkill(pluginContainer.Plugin.GetType().Assembly);
        }

        if (GetCoinsSupport)
        {
            Rank.RankTypeMappings.Add("货币", RankTypes.EconomicCoin);
        }
    }

    private static bool TryBindCore(Assembly assembly)
    {
        try
        {
            var settingType = FindType(assembly, "Economics.Core.ConfigFiles.Setting", "Setting");
            var currencyType = FindType(assembly, "Economics.Core.ConfigFiles.CurrencyDefinition", "CurrencyDefinition");
            var economicsType = FindType(assembly, "Economics.Core.Economics", "Economics");
            var serviceType = FindType(assembly, "Economics.Core.Services.ICurrencyService", "ICurrencyService");

            _settingInstance = FindStaticProperty(settingType, "Instance");
            _currenciesField = settingType?.GetField("Currencies", BindingFlags.Public | BindingFlags.Instance);
            _currencyName = FindProperty(currencyType, "Name");
            _currencyService = FindStaticProperty(economicsType, "CurrencyService");
            _getBalance = serviceType?.GetMethod("GetBalance", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(string) }, null);
            _getAllCurrencyRecords = serviceType?.GetMethod("GetAllCurrencyRecords", BindingFlags.Public | BindingFlags.Instance);

            var resultType = _getBalance?.ReturnType;
            _balanceIsSuccess = FindProperty(resultType, "IsSuccess");
            _balanceValue = FindProperty(resultType, "Value");

            var recordType = _getAllCurrencyRecords?.ReturnType?.GetGenericArguments().FirstOrDefault()
                             ?? FindType(assembly, "Economics.Core.Model.PlayerCurrencyInfo", "PlayerCurrencyInfo");
            _recordCurrencyType = FindProperty(recordType, "CurrencyType");
            _recordNumber = FindProperty(recordType, "Number");
            _recordPlayerName = FindProperty(recordType, "PlayerName");

            return _settingInstance is not null && _currenciesField is not null && _currencyName is not null
                   && _currencyService is not null && _getBalance is not null && _balanceIsSuccess is not null
                   && _balanceValue is not null && _getAllCurrencyRecords is not null
                   && _recordCurrencyType is not null && _recordNumber is not null && _recordPlayerName is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化Economics.Core反射失败:\n{ex}");
            return false;
        }
    }

    private static bool TryBindRpg(Assembly assembly)
    {
        try
        {
            var rpgType = FindType(assembly, "Economics.RPG.RPG", "RPG");
            var managerType = FindType(assembly, "Economics.RPG.PlayerLevelManager", "PlayerLevelManager");

            _playerLevelManager = FindStaticProperty(rpgType, "PlayerLevelManager");
            _getLevel = managerType?.GetMethod("GetLevel", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            _levelName = FindProperty(_getLevel?.ReturnType, "Name");

            return _playerLevelManager is not null && _getLevel is not null && _levelName is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化Economics.RPG反射失败:\n{ex}");
            return false;
        }
    }

    private static bool TryBindSkill(Assembly assembly)
    {
        try
        {
            var skillType = FindType(assembly, "Economics.Skill.Skill", "Skill",
                t => t.GetProperty("PlayerSKillManager", BindingFlags.Public | BindingFlags.Static) is not null);
            var managerType = FindType(assembly, "Economics.Skill.DB.PlayerSKillManager", "PlayerSKillManager");

            _playerSkillManager = FindStaticProperty(skillType, "PlayerSKillManager");
            _querySkill = managerType?.GetMethod("QuerySkill", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);

            var playerSkillType = _querySkill?.ReturnType?.GetGenericArguments().FirstOrDefault()
                                  ?? FindType(assembly, "Economics.Skill.DB.PlayerSKillManager+PlayerSkill", "PlayerSkill");
            _playerSkillSkill = FindProperty(playerSkillType, "Skill");
            _skillContextName = FindProperty(_playerSkillSkill?.PropertyType, "Name");

            return _playerSkillManager is not null && _querySkill is not null
                   && _playerSkillSkill is not null && _skillContextName is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化Economics.Skill反射失败:\n{ex}");
            return false;
        }
    }

    public static bool IsSupported(string feature)
    {
        return feature switch
        {
            nameof(GetCoins) => GetCoinsSupport,
            nameof(GetLevelName) => GetLevelNameSupport,
            nameof(GetSkill) => GetSkillSupport,
            nameof(GetCoinRank) => GetCoinsSupport,
            nameof(SupportCoins) => GetCoinsSupport,
            _ => false
        };
    }

    public static string GetCoins(string name)
    {
        ThrowIfNotSupported();
        return GetNewCoins(name);
    }

    public static List<string> SupportCoins
    {
        get
        {
            ThrowIfNotSupported();
            var setting = _settingInstance!.GetValue(null);
            if (setting is null || _currenciesField!.GetValue(setting) is not IEnumerable currencies)
            {
                return [];
            }

            return currencies.Cast<object>()
                .Select(x => (string?)_currencyName!.GetValue(x) ?? "")
                .ToList();
        }
    }

    public static Rank GetCoinRank(string type)
    {
        ThrowIfNotSupported();
        var service = _currencyService!.GetValue(null);
        var records = service is null ? null : _getAllCurrencyRecords!.Invoke(service, null) as IEnumerable;
        if (records is null)
        {
            return new Rank($"{type}排行", new Dictionary<string, string>());
        }

        var rows = new List<(string PlayerName, long Number, string CurrencyType)>();
        foreach (var record in records)
        {
            var currencyType = (string?)_recordCurrencyType!.GetValue(record) ?? "";
            if (currencyType != type)
            {
                continue;
            }

            var number = Convert.ToInt64(_recordNumber!.GetValue(record));
            var playerName = (string?)_recordPlayerName!.GetValue(record) ?? "";
            rows.Add((playerName, number, currencyType));
        }

        var rankLines = new Dictionary<string, string>();
        foreach (var row in rows.OrderByDescending(x => x.Number))
        {
            rankLines[row.PlayerName] = row.Number + row.CurrencyType;
        }

        return new Rank($"{type}排行", rankLines);
    }

    private static string GetNewCoins(string name)
    {
        var setting = _settingInstance!.GetValue(null);
        var service = _currencyService!.GetValue(null);
        if (setting is null || service is null || _currenciesField!.GetValue(setting) is not IEnumerable currencies)
        {
            return "";
        }

        var lines = new List<string>();
        foreach (var currency in currencies)
        {
            var currencyName = (string?)_currencyName!.GetValue(currency) ?? "";
            var balanceResult = _getBalance!.Invoke(service, new object[] { name, currencyName })!;
            var isSuccess = (bool)_balanceIsSuccess!.GetValue(balanceResult)!;
            var balance = isSuccess ? Convert.ToInt64(_balanceValue!.GetValue(balanceResult)) : 0;
            lines.Add($"{currencyName}x{balance}");
        }

        return string.Join('\n', lines);
    }

    public static string GetLevelName(string name)
    {
        ThrowIfNotSupported();
        var manager = _playerLevelManager!.GetValue(null);
        var level = manager is null ? null : _getLevel!.Invoke(manager, new object[] { name });
        var levelName = level is null ? null : (string?)_levelName!.GetValue(level);
        return $"职业:{(string.IsNullOrEmpty(levelName) ? "无" : levelName)}";
    }

    public static string GetSkill(string name)
    {
        ThrowIfNotSupported();
        var manager = _playerSkillManager!.GetValue(null);
        var skills = manager is null ? null : _querySkill!.Invoke(manager, new object[] { name }) as IEnumerable;
        if (skills is null)
        {
            return "技能:无";
        }

        var names = new List<string>();
        foreach (var playerSkill in skills)
        {
            var skill = _playerSkillSkill!.GetValue(playerSkill);
            names.Add(skill is null ? "无效技能" : (string?)_skillContextName!.GetValue(skill) ?? "");
        }

        return names.Count == 0 ? "技能:无" : string.Join(',', names);
    }

    private static void ThrowIfNotSupported([CallerMemberName] string memberName = "")
    {
        if (!IsSupported(memberName))
        {
            throw new NotSupportedException(memberName);
        }
    }

    #region 反射辅助

    private static Type? FindType(Assembly assembly, string fullName, string simpleName, Func<Type, bool>? predicate = null)
    {
        var type = assembly.GetType(fullName);
        if (type is not null)
        {
            return type;
        }

        return assembly.GetTypes().FirstOrDefault(t => t.Name == simpleName && (predicate is null || predicate(t)));
    }

    private static PropertyInfo? FindProperty(Type? type, string name)
    {
        return type?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
    }

    private static PropertyInfo? FindStaticProperty(Type? type, string name)
    {
        while (type is not null)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return property;
            }

            type = type.BaseType;
        }

        return null;
    }

    #endregion
}