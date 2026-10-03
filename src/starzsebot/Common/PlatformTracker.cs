using Terraria;

namespace ZSEBot.Common;

// 玩家设备平台识别（读取逻辑移植自 Platform 插件的 On.OTAPI 钩子方案）：
// ConnectRequest 阶段先按 PC 记录，收到 PlayerPlatformInfo 包时再覆盖为真实平台
internal static class PlatformTracker
{
    // 与 Platform 插件的 PlatformType 枚举一致（数值即包内上报的平台 ID）
    internal enum PlatformType : byte
    {
        PE = 0,
        Stadia = 1,
        XBO = 2,
        PSN = 3,
        Editor = 4,
        Nintendo = 5,
        Steam = 6,
        GameCenter = 7,
        PC = 10
    }

    // 按玩家槽位记录平台，槽位与 Terraria 玩家索引一一对应
    private static readonly PlatformType[] Platforms = new PlatformType[Main.maxPlayers];

    // 记录平台；槽位越界时忽略，避免异常包导致索引越界
    internal static void Set(int whoAmI, PlatformType platform)
    {
        if (whoAmI >= 0 && whoAmI < Platforms.Length)
        {
            Platforms[whoAmI] = platform;
        }
    }

    // 读取平台字符串；槽位越界时按 PC 兜底（与连接默认值一致）
    internal static string Get(int whoAmI)
    {
        if (whoAmI < 0 || whoAmI >= Platforms.Length)
        {
            return PlatformType.PC.ToString();
        }

        return Platforms[whoAmI].ToString();
    }
}