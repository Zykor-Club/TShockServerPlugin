using System.Reflection;
using TerrariaApi.Server;
using TShockAPI;

namespace ZSEBot.Common;

/// <summary>
/// 运行时反射桥接 AutoResetPlus，未安装时全部降级为失败/未安装，不抛异常。
/// </summary>
internal static class AutoResetSupport
{
    private const string PluginTypeName = "AutoResetPlus.AutoResetPlugin";
    private const string ConfigTypeName = "AutoResetPlus.ResetConfig";

    internal static bool Support => FindType(ConfigTypeName) != null;

    private static Type? FindType(string fullName)
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // 个别动态程序集可能抛异常，忽略继续
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static object? GetConfigInstance()
    {
        var configType = FindType(ConfigTypeName);
        return configType?
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?
            .GetValue(null);
    }

    internal static ResetConfigSnapshot GetConfig()
    {
        var snapshot = new ResetConfigSnapshot();
        try
        {
            var configType = FindType(ConfigTypeName);
            if (configType == null)
            {
                return snapshot;
            }

            var instance = GetConfigInstance();
            if (instance == null)
            {
                return snapshot;
            }

            snapshot.Installed = true;

            var setWorld = configType.GetField("SetWorld")?.GetValue(instance);
            if (setWorld != null)
            {
                var setWorldType = setWorld.GetType();
                snapshot.WorldName = setWorldType.GetField("Name")?.GetValue(setWorld) as string ?? "";
                snapshot.CurrentSeed = setWorldType.GetField("Seed")?.GetValue(setWorld) as string ?? "";
            }

            var randomSeed = configType.GetField("RandomSeed")?.GetValue(instance);
            if (randomSeed != null)
            {
                var randomSeedType = randomSeed.GetType();
                snapshot.RandomEnable = randomSeedType.GetField("Enable")?.GetValue(randomSeed) as bool? ?? false;
                snapshot.Min = randomSeedType.GetField("Min")?.GetValue(randomSeed) as int? ?? 0;
                snapshot.Max = randomSeedType.GetField("Max")?.GetValue(randomSeed) as int? ?? 0;
                snapshot.SeedList = randomSeedType.GetField("SeedList")?.GetValue(randomSeed) as string[] ?? [];
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]读取 AutoResetPlus 配置失败: {ex}");
        }

        return snapshot;
    }

    internal static (bool Ok, string? Error) SetSeed(string seed)
    {
        try
        {
            var configType = FindType(ConfigTypeName);
            var instance = configType == null ? null : GetConfigInstance();
            if (configType == null || instance == null)
            {
                return (false, "未检测到 AutoResetPlus 插件");
            }

            var setWorld = configType.GetField("SetWorld")?.GetValue(instance);
            if (setWorld == null)
            {
                return (false, "未检测到 AutoResetPlus 插件");
            }

            setWorld.GetType().GetField("Seed")?.SetValue(setWorld, seed);

            configType
                .GetMethod("SaveTo", BindingFlags.Public | BindingFlags.Instance)?
                .Invoke(instance, [null]);

            return (true, null);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]写入 AutoResetPlus 种子失败: {ex}");
            return (false, $"写入种子失败: {ex.Message}");
        }
    }

    internal static (bool Ok, string? Error) DoReset()
    {
        try
        {
            // 先把 /世界设置 里的难度/大小/邪恶写进生成参数（此刻世界即将重建，改动安全），
            // 这样"种子投票 → 重置"出来的新世界默认就按这些设置生成
            WorldSettings.Apply();

            // 主方案：从聊天命令表中定位 AutoResetPlus 的 /reset 命令，直接调用其委托，绕过聊天权限。
            Command? resetCommand = null;
            object? pluginInstance = null;
            foreach (var command in Commands.ChatCommands)
            {
                var names = command.Names;
                if (names == null || !(names.Contains("reset") || names.Contains("重置世界")))
                {
                    continue;
                }

                var target = command.CommandDelegate.Target;
                if (target != null && target.GetType().FullName == PluginTypeName)
                {
                    resetCommand = command;
                    pluginInstance = target;
                    break;
                }
            }

            if (resetCommand != null && pluginInstance != null)
            {
                if (!TryGetStatus(pluginInstance, out var status))
                {
                    return (false, $"AutoResetPlus 当前状态不可用（{status}）");
                }

                resetCommand.CommandDelegate.DynamicInvoke([null]);
                return DoneReset();
            }

            // 备选方案：从 ServerApi.Plugins 找到插件实例，反射调用私有 ResetCmd。
            var pluginType = FindType(PluginTypeName);
            var container = pluginType == null
                ? null
                : ServerApi.Plugins.FirstOrDefault(p => p.Plugin.GetType().FullName == PluginTypeName);
            if (pluginType == null || container == null)
            {
                return (false, "未检测到 AutoResetPlus 插件");
            }

            var method = pluginType.GetMethod("ResetCmd", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                return (false, "未检测到 AutoResetPlus 插件");
            }

            if (!TryGetStatus(container.Plugin, out var pluginStatus))
            {
                return (false, $"AutoResetPlus 当前状态不可用（{pluginStatus}）");
            }

            method.Invoke(container.Plugin, [null]);
            return DoneReset();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]触发 AutoResetPlus 重置失败: {ex}");
            return (false, $"触发重置失败: {ex.Message}");
        }
    }

    /// <summary>重置已成功触发：立即清零排行统计（换世界也会被 WorldResetGuard 再兜一次，幂等）</summary>
    private static (bool, string?) DoneReset()
    {
        ZSEBot.Models.ZSECharacterInfo.CleanAll();
        return (true, null);
    }

    private static bool TryGetStatus(object pluginInstance, out string status)
    {
        status = "Unknown";
        try
        {
            var field = pluginInstance.GetType().GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance);
            var value = field?.GetValue(pluginInstance);
            if (value == null)
            {
                return false;
            }

            status = value.ToString() ?? "Unknown";
            return status == "Available";
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]读取 AutoResetPlus 状态失败: {ex}");
            return false;
        }
    }
}

internal sealed class ResetConfigSnapshot
{
    public bool Installed;
    public string WorldName = "";
    public string CurrentSeed = "";
    public bool RandomEnable;
    public string[] SeedList = [];
    public int Min;
    public int Max;
}