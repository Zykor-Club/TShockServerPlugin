using System.Threading;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;

namespace ZSEBot.Common;

/// <summary>
/// 定时存档备份：每 N 小时静默导出一次（zip 落到 tshock/starZSEBot/Exports，只保留最近若干份）。
/// OnGameUpdate 里只做轻量计时（几次比较），真正的导出丢到后台线程，避免卡游戏主循环。
/// 间隔与保留份数来自配置：自动备份开关 / 自动备份间隔小时 / 备份保留份数。
/// </summary>
internal static class BackupScheduler
{
    private static DateTime _last = DateTime.MinValue;
    private static int _running;

    /// <summary>OnGameUpdate 调用（约每 0.25 秒一次）</summary>
    internal static void Tick()
    {
        try
        {
            var cfg = Config.Settings;
            if (!cfg.AutoBackup || cfg.BackupIntervalHours <= 0)
            {
                return;
            }

            // 没进世界不备份（世界文件还不存在/正在切换）
            if (Main.gameMenu || string.IsNullOrEmpty(Main.worldName))
            {
                return;
            }

            var now = DateTime.Now;
            if (_last == DateTime.MinValue)
            {
                _last = now;   // 启动/换世界后先等一个完整间隔，避免刚开服就备份
                return;
            }

            if ((now - _last).TotalHours < cfg.BackupIntervalHours)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return;   // 上一轮还没跑完（大存档导出慢），不叠加
            }

            _last = now;
            Task.Run(() =>
            {
                try
                {
                    var (name, _, size, err) = ArchiveExport.Export(needBase64: false);
                    if (err != null)
                    {
                        TShock.Log.ConsoleError($"[starZSEbot]定时备份失败: {err}");
                    }
                    else
                    {
                        TShock.Log.ConsoleInfo($"[starZSEbot]定时备份完成: {name} ({size / 1024 / 1024} MB)"
                            + $"，保留最近 {cfg.BackupKeep} 份");
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[starZSEbot]定时备份异常: {ex}");
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            });
        }
        catch (Exception ex)
        {
            // 备份只是附加能力，绝不能影响游戏主循环
            TShock.Log.ConsoleError($"[starZSEbot]备份调度异常: {ex.Message}");
        }
    }
}
