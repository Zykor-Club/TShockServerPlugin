using System.Reflection;
using TShockAPI;
using Terraria;

namespace ZSEBot.Common;

/// <summary>
/// 世界生成参数（难度 / 世界大小 / 邪恶环境）：
///  - 由机器人的 /世界设置 指令读写，持久化在 starZSEbot.json
///  - 重置（AutoResetPlus 生成新世界）前由 Apply() 写入生成参数 →
///    所以「种子投票出来的世界」默认也按这里的设置走
///  - 读/写都遵循「能直连就直连、成员名不确定就反射」的原则，
///    避免依赖 OTAPI 里可能改名的内部成员（与本插件其它地方做法一致）
/// </summary>
internal static class WorldSettings
{
    internal static readonly Dictionary<string, int> DifficultyIds = new()
    {
        ["经典"] = 0, ["普通"] = 0, ["classic"] = 0, ["normal"] = 0,
        ["专家"] = 1, ["expert"] = 1,
        ["大师"] = 2, ["master"] = 2,
        ["旅行"] = 3, ["旅途"] = 3, ["journey"] = 3, ["creative"] = 3,
    };

    /// <summary>世界大小 → (maxTilesX, maxTilesY)，与原版创建菜单一致</summary>
    internal static readonly Dictionary<string, (int X, int Y)> SizeIds = new()
    {
        ["小"] = (4200, 1200), ["small"] = (4200, 1200),
        ["中"] = (6400, 1800), ["medium"] = (6400, 1800),
        ["大"] = (8400, 2400), ["large"] = (8400, 2400),
    };

    internal static readonly Dictionary<string, bool> EvilIds = new()
    {
        ["腐化"] = false, ["紫"] = false, ["corruption"] = false,
        ["猩红"] = true, ["红"] = true, ["crimson"] = true,
    };

    internal static string DifficultyLabel(int mode) => mode switch
    {
        1 => "专家",
        2 => "大师",
        3 => "旅行",
        _ => "经典",
    };

    internal static string SizeLabel(int maxTilesX) =>
        maxTilesX <= 4200 ? "小" : maxTilesX <= 6400 ? "中" : "大";

    internal static string EvilLabel() => WorldGen.crimson ? "猩红" : "腐化";

    /// <summary>当前世界参数 + 配置里"下次重置用"的设置，供 /世界设置 展示</summary>
    internal static Dictionary<string, object> Snapshot()
    {
        var cur = new Dictionary<string, object>
        {
            ["difficulty"] = DifficultyLabel(GetGameMode()),
            ["size"] = SizeLabel(Main.maxTilesX),
            ["evil"] = EvilLabel(),
            ["world_name"] = Main.worldName ?? "",
            ["max_x"] = Main.maxTilesX,
            ["max_y"] = Main.maxTilesY,
            ["hardmode"] = false,
            ["seed"] = "",
            ["set_difficulty"] = Config.Settings.WorldDifficulty ?? "",
            ["set_size"] = Config.Settings.WorldSize ?? "",
            ["set_evil"] = Config.Settings.WorldEvil ?? "",
        };
        try
        {
            var wfd = Main.ActiveWorldFileData;
            if (wfd != null)
            {
                // 注意：不同版本 Seed 可能是 int 或 string，统一转字符串
                cur["seed"] = Convert.ToString(wfd.Seed) ?? "";
                cur["hardmode"] = wfd.IsHardMode;
            }
        }
        catch
        {
            // 世界未加载时忽略
        }

        try
        {
            // AutoResetPlus 里保存的"文本种子"（支持 drunk|bee 这类组合写法），比数字种子更直观
            var snap = AutoResetSupport.GetConfig();
            if (!string.IsNullOrWhiteSpace(snap.CurrentSeed))
            {
                cur["text_seed"] = snap.CurrentSeed;
            }
        }
        catch
        {
            // 未安装 AutoResetPlus 时忽略
        }

        return cur;
    }

    /// <summary>保存 /世界设置 的改动（空串 = 跟随当前世界，不干预）</summary>
    internal static (bool Ok, string? Error) Update(string difficulty, string size, string evil)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var key = Normalize(difficulty);
                if (!DifficultyIds.ContainsKey(key))
                {
                    return (false, $"难度只能是 经典/专家/大师/旅行（收到「{difficulty}」）");
                }

                Config.Settings.WorldDifficulty = key;
            }

            if (!string.IsNullOrWhiteSpace(size))
            {
                var key = Normalize(size);
                if (!SizeIds.ContainsKey(key))
                {
                    return (false, $"世界大小只能是 小/中/大（收到「{size}」）");
                }

                Config.Settings.WorldSize = key;
            }

            if (!string.IsNullOrWhiteSpace(evil))
            {
                var key = Normalize(evil);
                if (!EvilIds.ContainsKey(key))
                {
                    return (false, $"邪恶环境只能是 腐化/猩红（收到「{evil}」）");
                }

                Config.Settings.WorldEvil = key;
            }

            Config.Settings.Write();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"保存失败: {ex.Message}");
        }
    }

    /// <summary>重置前把配置里的地图参数写进生成状态（留空则不动）</summary>
    internal static void Apply()
    {
        try
        {
            var cfg = Config.Settings;
            var notes = new List<string>();

            if (!string.IsNullOrWhiteSpace(cfg.WorldDifficulty)
                && DifficultyIds.TryGetValue(Normalize(cfg.WorldDifficulty), out var mode))
            {
                SetGameMode(mode);
                notes.Add($"难度={DifficultyLabel(mode)}");
            }

            if (!string.IsNullOrWhiteSpace(cfg.WorldSize)
                && SizeIds.TryGetValue(Normalize(cfg.WorldSize), out var wh))
            {
                Main.maxTilesX = wh.X;
                Main.maxTilesY = wh.Y;
                TrySetMember(Main.ActiveWorldFileData, "WorldSizeX", wh.X);
                TrySetMember(Main.ActiveWorldFileData, "WorldSizeY", wh.Y);
                notes.Add($"大小={SizeLabel(wh.X)}({wh.X}×{wh.Y})");
            }

            if (!string.IsNullOrWhiteSpace(cfg.WorldEvil)
                && EvilIds.TryGetValue(Normalize(cfg.WorldEvil), out var crimson))
            {
                WorldGen.crimson = crimson;
                TrySetMember(Main.ActiveWorldFileData, "HasCorruption", !crimson);
                TrySetMember(Main.ActiveWorldFileData, "HasCrimson", crimson);
                notes.Add($"邪恶={(crimson ? "猩红" : "腐化")}");
            }

            if (notes.Count > 0)
            {
                TShock.Log.ConsoleInfo("[starZSEbot]重置前应用地图设置：" + string.Join("，", notes));
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[starZSEbot]应用地图设置失败: {ex.Message}");
        }
    }

    private static string Normalize(string v) => (v ?? "").Trim().ToLowerInvariant();

    private static int GetGameMode()
    {
        try
        {
            // Main.GameMode 是原版世界难度（0 经典 / 1 专家 / 2 大师 / 3 旅行）
            var f = typeof(Main).GetField("GameMode", BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.GetValue(null) is int i)
            {
                return i;
            }
        }
        catch
        {
            // 忽略，退回按 WorldFileData 推断
        }

        var wfd = Main.ActiveWorldFileData;
        if (wfd != null)
        {
            if (GetBool(wfd, "IsCreativeMode"))
            {
                return 3;
            }

            if (GetBool(wfd, "IsMasterMode"))
            {
                return 2;
            }

            if (GetBool(wfd, "IsExpertMode"))
            {
                return 1;
            }
        }

        return 0;
    }

    private static void SetGameMode(int mode)
    {
        try
        {
            var f = typeof(Main).GetField("GameMode", BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.FieldType == typeof(int))
            {
                f.SetValue(null, mode);
            }
        }
        catch
        {
            // 忽略
        }

        var wfd = Main.ActiveWorldFileData;
        TrySetMember(wfd, "IsExpertMode", mode == 1);
        TrySetMember(wfd, "IsMasterMode", mode == 2);
        TrySetMember(wfd, "IsCreativeMode", mode == 3);
    }

    private static bool GetBool(object obj, string name)
    {
        try
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.GetValue(obj) is bool bp)
            {
                return bp;
            }

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.GetValue(obj) is bool bf)
            {
                return bf;
            }
        }
        catch
        {
            // 忽略
        }

        return false;
    }

    private static void TrySetMember(object? obj, string name, object value)
    {
        if (obj == null)
        {
            return;
        }

        try
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, value);
                return;
            }

            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            f?.SetValue(obj, value);
        }
        catch
        {
            // 成员不存在/只读：忽略（不同版本 Terraria 字段名可能不同）
        }
    }
}
