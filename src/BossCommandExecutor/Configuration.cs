using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;
using Microsoft.Xna.Framework;

namespace BossCommandExecutor
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Configuration
    {
        #region 全局设置
        [JsonProperty("开发者信息", Order = -2)]
        public List<string> DeveloperInfo { get; set; } = new()
        {
            "[开发者所在群]QQ群：Tshock:816771079",
            "[问题反馈] 问题询问前，请将报错截图，配置文件一同向群内发送"
        };

        [JsonProperty("进度名称", Order = -1)]
        public List<string> ProgressNames { get; set; } = new()
        {
            "无 0 | 史莱姆王 1 | 克眼 2 | 世吞 3 | 克脑 4 | 蜂王 5 | 骷髅王 6 | 鹿角怪 7 | 困难模式(肉山) 8 | 史莱姆皇后 9 |",
            "| 毁灭者 10 | 双子魔眼 11 | 机械骷髅王 12 | 世纪之花 13 | 石巨人 14 | 猪鲨 15 | 光女 16 |",
            "教徒 17 | 日耀柱 18 | 星云柱 19 | 星璇柱 20 | 星尘柱 21 | 月总 22 |"
        };

        private static readonly Dictionary<int, List<List<int>>> ProgressIdToBossIdGroups = new()
        {
            { 0, new List<List<int>> { new List<int>() } },
            { 1, new List<List<int>> { new List<int> { 50 } } },
            { 2, new List<List<int>> { new List<int> { 4 } } },
            { 3, new List<List<int>> { new List<int> { 13, 14, 15 } } },
            { 4, new List<List<int>> { new List<int> { 266 } } },
            { 5, new List<List<int>> { new List<int> { 222 } } },
            { 6, new List<List<int>> { new List<int> { 35 } } },
            { 7, new List<List<int>> { new List<int> { 668 } } },
            { 8, new List<List<int>> { new List<int> { 113, 114 } } },
            { 9, new List<List<int>> { new List<int> { 657 } } },
            { 10, new List<List<int>> { new List<int> { 134, 135, 136 } } },
            { 11, new List<List<int>> { new List<int> { 125, 126 } } },
            { 12, new List<List<int>> { new List<int> { 127 } } },
            { 13, new List<List<int>> { new List<int> { 262 } } },
            { 14, new List<List<int>> { new List<int> { 245 } } },
            { 15, new List<List<int>> { new List<int> { 370 } } },
            { 16, new List<List<int>> { new List<int> { 636 } } },
            { 17, new List<List<int>> { new List<int> { 440 } } },
            { 18, new List<List<int>> { new List<int> { 390 } } },
            { 19, new List<List<int>> { new List<int> { 391 } } },
            { 20, new List<List<int>> { new List<int> { 392 } } },
            { 21, new List<List<int>> { new List<int> { 393 } } },
            { 22, new List<List<int>> { new List<int> { 398 } } }
        };

        [JsonIgnore]
        public static readonly Dictionary<int, List<int>> ProgressIdToBossIds = new()
        {
            { 0, new List<int>() },
            { 1, new List<int> { 50 } },
            { 2, new List<int> { 4 } },
            { 3, new List<int> { 13, 14, 15 } },
            { 4, new List<int> { 266 } },
            { 5, new List<int> { 222 } },
            { 6, new List<int> { 35 } },
            { 7, new List<int> { 668 } },
            { 8, new List<int> { 113, 114 } },
            { 9, new List<int> { 657 } },
            { 10, new List<int> { 134, 135, 136 } },
            { 11, new List<int> { 125, 126 } },
            { 12, new List<int> { 127 } },
            { 13, new List<int> { 262 } },
            { 14, new List<int> { 245 } },
            { 15, new List<int> { 370 } },
            { 16, new List<int> { 636 } },
            { 17, new List<int> { 440 } },
            { 18, new List<int> { 390 } },
            { 19, new List<int> { 391 } },
            { 20, new List<int> { 392 } },
            { 21, new List<int> { 393 } },
            { 22, new List<int> { 398 } }
        };

        [JsonProperty("插件开关", Order = 0)]
        public bool Enabled { get; set; } = true;

        [JsonProperty("命令执行延迟(毫秒)", Order = 1)]
        public int CommandDelay { get; set; } = 100;

        [JsonProperty("广播执行结果", Order = 2)]
        public bool BroadcastEnabled { get; set; } = true;

        [JsonProperty("广播消息格式", Order = 3)]
        public string BroadcastFormat { get; set; } = "[c/32CD32:Boss命令] {boss} 已被击败，已自动执行 {count} 条命令。";

        [JsonProperty("广播颜色", Order = 4)]
        public ColorConfig BroadcastColor { get; set; } = new(50, 205, 50);

        [JsonProperty("控制台提示", Order = 5)]
        public bool ConsoleSuccessPrompt { get; set; } = true;

        [JsonProperty("自动广播BOSS伤害排行", Order = 6)]
        public bool AutoBroadcastDamageRanking { get; set; } = true;
        #endregion

        #region Boss配置列表
        [JsonProperty("Boss命令配置", Order = 7)]
        public List<BossCommandConfig> BossCommands { get; set; } = new();
        #endregion

        #region 数据子类
        public class BossCommandConfig
        {
            [JsonProperty("Boss名称", Order = 0)]
            public string Name { get; set; } = "";

            [JsonProperty("Boss ID列表", Order = 1)]
            public List<int> BossIDs { get; set; } = new();

            [JsonProperty("进度名称ID", Order = 2)]
            public int ProgressId { get; set; } = -1;

            [JsonProperty("需要被召唤", Order = 3)]
            public bool RequireSummoned { get; set; } = true;

            [JsonProperty("记录执行次数", Order = 4)]
            public bool RecordExecutionCount { get; set; } = true;

            [JsonProperty("执行次数", Order = 5)]
            public int ExecutionCount { get; set; } = 0;

            [JsonProperty("已首次击杀", Order = 6)]
            public bool FirstKillDone { get; set; } = false;

            [JsonProperty("首次击杀命令", Order = 7)]
            public List<string> FirstKillCommands { get; set; } = new();

            [JsonProperty("常规执行命令", Order = 8)]
            public List<string> Commands { get; set; } = new();

            [JsonProperty("广播结果", Order = 9)]
            public bool BroadcastResult { get; set; } = true;

            [JsonProperty("浮动文本内容", Order = 10)]
            public string FloatingText { get; set; } = "";

            [JsonProperty("浮动文本颜色", Order = 11)]
            public ColorConfig FloatingTextColor { get; set; } = new(102, 204, 255);

            [JsonProperty("浮动文本显示时长(秒)", Order = 12)]
            public int FloatingTextDuration { get; set; } = 10;

            [JsonProperty("启用浮动文本", Order = 13)]
            public bool FloatingTextEnabled { get; set; } = true;

            [JsonConstructor]
            public BossCommandConfig() { }

            public BossCommandConfig(string name, int progressId, bool requireSummon,
                string[] firstCmds, string[] cmds, bool broadcast = true, bool record = true)
            {
                Name = name;
                ProgressId = progressId;
                BossIDs = new();
                RequireSummoned = requireSummon;
                FirstKillCommands = new(firstCmds);
                Commands = new(cmds);
                BroadcastResult = broadcast;
                RecordExecutionCount = record;
                FloatingText = "Use /bag rall to receive the Progressbag";
                FloatingTextColor = new ColorConfig(102, 204, 255);
                FloatingTextDuration = 10;
                FloatingTextEnabled = true;
            }

            public BossCommandConfig(string name, int[] ids, bool requireSummon,
                string[] firstCmds, string[] cmds, bool broadcast = true, bool record = true)
                : this(name, ids, -1, requireSummon, firstCmds, cmds, broadcast, record) { }

            public BossCommandConfig(string name, int[] ids, int progressId, bool requireSummon,
                string[] firstCmds, string[] cmds, bool broadcast = true, bool record = true)
            {
                Name = name;
                BossIDs = new(ids);
                ProgressId = progressId;
                RequireSummoned = requireSummon;
                FirstKillCommands = new(firstCmds);
                Commands = new(cmds);
                BroadcastResult = broadcast;
                RecordExecutionCount = record;
                FloatingText = "Use /bag rall to receive the Progressbag";
                FloatingTextColor = new ColorConfig(102, 204, 255);
                FloatingTextDuration = 10;
                FloatingTextEnabled = true;
            }
        }

        [JsonObject(MemberSerialization.OptOut)]
        public class ColorConfig
        {
            [JsonProperty("R")]
            public byte R { get; set; }

            [JsonProperty("G")]
            public byte G { get; set; }

            [JsonProperty("B")]
            public byte B { get; set; }

            [JsonIgnore]
            public Color XnaColor => new(R, G, B);

            [JsonConstructor]
            public ColorConfig() { }

            public ColorConfig(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }
        }
        #endregion

        #region 默认配置生成
        public void SetDefaults()
        {
            BossCommands.Clear();

            ConfigureVanillaBosses();

            Write();
        }

        private void ConfigureVanillaBosses()
        {
            BossCommands.Add(new BossCommandConfig("史莱姆王", new[] { 50 }, 1, false,
                new[] { "/bc 恭喜玩家首次击杀史莱姆王！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("克苏鲁之眼", new[] { 4 }, 2, true,
                new[] { "/bc 恭喜玩家首次击杀克苏鲁之眼！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("世界吞噬者", new[] { 13, 14, 15 }, 3, true,
                new[] { "/bc 恭喜玩家首次击杀世界吞噬者！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("克苏鲁之脑", new[] { 266 }, 4, true,
                new[] { "/bc 恭喜玩家首次击杀克苏鲁之脑！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("蜂后", new[] { 222 }, 5, true,
                new[] { "/bc 恭喜玩家首次击杀蜂后！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("骷髅王", new[] { 35 }, 6, true,
                new[] { "/bc 恭喜玩家首次击杀骷髅王！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("鹿角怪", new[] { 668 }, 7, true,
                new[] { "/bc 恭喜玩家首次击杀鹿角怪！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("血肉墙", new[] { 113, 114 }, 8, true,
                new[] { "/bc 恭喜玩家首次击杀血肉墙！", "/give 74 *all* 1" },
                new[] { "time 0", "bc 困难模式已开启！" }));

            BossCommands.Add(new BossCommandConfig("史莱姆皇后", new[] { 657 }, 9, true,
                new[] { "/bc 恭喜玩家首次击杀史莱姆皇后！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("毁灭者", new[] { 134, 135, 136 }, 10, true,
                new[] { "/bc 恭喜玩家首次击杀毁灭者！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("双子魔眼", new[] { 125, 126 }, 11, true,
                new[] { "/bc 恭喜玩家首次击杀双子魔眼！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("机械骷髅王", new[] { 127 }, 12, true,
                new[] { "/bc 恭喜玩家首次击杀机械骷髅王！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("世纪之花", new[] { 262 }, 13, true,
                new[] { "/bc 恭喜玩家首次击杀世纪之花！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("石巨人", new[] { 245 }, 14, true,
                new[] { "/bc 恭喜玩家首次击杀石巨人！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("猪龙鱼公爵", new[] { 370 }, 15, true,
                new[] { "/bc 恭喜玩家首次击杀猪龙鱼公爵！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("光之女皇", new[] { 636 }, 16, true,
                new[] { "/bc 恭喜玩家首次击杀光之女皇！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("教徒", new[] { 440 }, 17, true,
                new[] { "/bc 恭喜玩家首次击杀教徒！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("日耀柱", new[] { 390 }, 18, true,
                new[] { "/bc 恭喜玩家击破日耀柱！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("星云柱", new[] { 391 }, 19, true,
                new[] { "/bc 恭喜玩家击破星云柱！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("星璇柱", new[] { 392 }, 20, true,
                new[] { "/bc 恭喜玩家击破星璇柱！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("星尘柱", new[] { 393 }, 21, true,
                new[] { "/bc 恭喜玩家击破星尘柱！", "/give 74 *all* 1" },
                Array.Empty<string>()));

            BossCommands.Add(new BossCommandConfig("月亮领主", new[] { 398 }, 22, true,
                new[] { "/bc 恭喜玩家首次击杀月亮领主！", "/give 74 *all* 1", "giveall 74 10" },
                new[] { "time 0", "bc 服务器将在1分钟后重启", "settimer 60 restart" }));
        }
        #endregion

        #region 文件IO
        [JsonIgnore]
        public static readonly string FilePath = Path.Combine(TShock.SavePath, "BossCommands.json");

        public void Write()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BossCommand] 保存配置失败: {ex.Message}");
            }
        }

        public static Configuration Read()
        {
            if (!File.Exists(FilePath))
            {
                var newConfig = new Configuration();
                newConfig.SetDefaults();
                return newConfig;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var config = JsonConvert.DeserializeObject<Configuration>(json);

                if (config == null) throw new Exception("配置文件解析为空");

                config.BossCommands ??= new();

                config.SynchronizeConfig();

                return config;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[BossCommand] 读取配置失败: {ex.Message}，使用默认配置");
                return new Configuration();
            }
        }

        private void SynchronizeConfig()
        {
            bool configChanged = false;

            if (DeveloperInfo == null || DeveloperInfo.Count == 0)
            {
                DeveloperInfo = new()
                {
                    "[开发者所在群]QQ群：Tshock:816771079",
                    "[问题反馈] 问题询问前，请将报错截图，配置文件一同向群内发送"
                };
                configChanged = true;
            }

            if (ProgressNames == null || ProgressNames.Count == 0)
            {
                ProgressNames = new()
                {
                    "无 0 | 史莱姆王 1 | 克眼 2 | 世吞 3 | 克脑 4 | 蜂王 5 | 骷髅王 6 | 鹿角怪 7 | 困难模式(肉山) 8 | 史莱姆皇后 9 |",
                    "| 毁灭者 10 | 双子魔眼 11 | 机械骷髅王 12 | 世纪之花 13 | 石巨人 14 | 猪鲨 15 | 光女 16 |",
                    "教徒 17 | 日耀柱 18 | 星云柱 19 | 星璇柱 20 | 星尘柱 21 | 月总 22 |"
                };
                configChanged = true;
            }

            foreach (var bossConfig in BossCommands)
            {
                bossConfig.BossIDs ??= new();
                bossConfig.FloatingTextColor ??= new ColorConfig(102, 204, 255);

                if (bossConfig.ProgressId >= 0 && bossConfig.BossIDs.Count == 0)
                {
                    if (ProgressIdToBossIdGroups.TryGetValue(bossConfig.ProgressId, out var groups))
                    {
                        if (groups.Count == 1)
                        {
                            bossConfig.BossIDs = new List<int>(groups[0]);
                            configChanged = true;
                        }
                    }
                }
                else if (bossConfig.BossIDs.Count > 0 && bossConfig.ProgressId == -1)
                {
                    var progressId = ResolveProgressIdFromBossIds(bossConfig.BossIDs);
                    if (progressId >= 0)
                    {
                        bossConfig.ProgressId = progressId;
                        configChanged = true;
                    }
                }
            }

            if (configChanged)
            {
                Write();
                TShock.Log.ConsoleInfo("[BossCommand] 配置已自动同步并保存");
            }
        }

        private static int ResolveProgressIdFromBossIds(List<int> bossIds)
        {
            foreach (var kvp in ProgressIdToBossIdGroups)
            {
                foreach (var group in kvp.Value)
                {
                    if (BossIdListsEqual(bossIds, group))
                    {
                        return kvp.Key;
                    }
                }
            }
            return -1;
        }

        private static bool BossIdListsEqual(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            var sortedA = new List<int>(a);
            var sortedB = new List<int>(b);
            sortedA.Sort();
            sortedB.Sort();
            for (int i = 0; i < sortedA.Count; i++)
            {
                if (sortedA[i] != sortedB[i]) return false;
            }
            return true;
        }
        #endregion
    }
}