using System.Reflection;
using TerrariaApi.Server;
using TShockAPI;

namespace ZSEBot.Common;

/// <summary>
/// 通过反射桥接 GenerateMap 插件（不硬引用其程序集）。
/// 未安装 GenerateMap 时地图相关功能不可用。
/// </summary>
internal static class MapGeneratorSupport
{
    public static bool Support { get; private set; }

    private static MethodInfo? _creatMapImgBytes;
    private static MethodInfo? _creatMapFile;
    private static FieldInfo? _mapFileField;
    private static FieldInfo? _mapFileName;

    internal static void Init()
    {
        var pluginContainer = ServerApi.Plugins.FirstOrDefault(x => x.Plugin.Name == "GenerateMap");
        if (pluginContainer is null)
        {
            return;
        }

        try
        {
            var assembly = pluginContainer.Plugin.GetType().Assembly;
            var generatorType = assembly.GetType("GenerateMap.MapGenerator");
            _creatMapImgBytes = generatorType?.GetMethod("CreatMapImgBytes", BindingFlags.Public | BindingFlags.Static);
            _creatMapFile = generatorType?.GetMethod("CreatMapFile", BindingFlags.Public | BindingFlags.Static);
            var mapFileType = _creatMapFile?.ReturnType;
            _mapFileField = mapFileType?.GetField("File", BindingFlags.Public | BindingFlags.Instance);
            _mapFileName = mapFileType?.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
            Support = _creatMapImgBytes is not null && _creatMapFile is not null
                && _mapFileField is not null && _mapFileName is not null;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]初始化GenerateMap反射失败:\n{ex}");
            Support = false;
        }

        if (!Support)
        {
            TShock.Log.ConsoleError("[starZSEbot]检测到GenerateMap插件但反射绑定失败，地图功能不可用");
        }
    }

    private static void ThrowIfNotSupported()
    {
        if (!Support)
        {
            throw new NotSupportedException("需要安装GenerateMap插件");
        }
    }


    internal static byte[] CreatMapImgBytes()
    {
        ThrowIfNotSupported();
        return (byte[])_creatMapImgBytes!.Invoke(null, null)!;
    }

    internal static (byte[], string) CreateMapFile()
    {
        ThrowIfNotSupported();
        var mapFile = _creatMapFile!.Invoke(null, null)!;
        return ((byte[])_mapFileField!.GetValue(mapFile)!, (string)_mapFileName!.GetValue(mapFile)!);
    }
}