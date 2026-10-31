using System;

namespace AIAgent.Core;

/// <summary>TShock 不一定会替我们先初始化 SQLitePCL，用之前自己保证一次。</summary>
public static class SqliteBoot
{
    private static readonly object Gate = new();
    private static bool _done;

    public static void Ensure()
    {
        if (_done) return;

        lock (Gate)
        {
            if (_done) return;

            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch (Exception ex)
            {
                PluginLog.Warn("SQLite 初始化失败: " + ex.Message);
            }

            _done = true;
        }
    }
}
