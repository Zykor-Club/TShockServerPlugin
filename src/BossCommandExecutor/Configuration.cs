using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;
using Microsoft.Xna.Framework;

namespace BossCommandExecutor
{
    /// <summary>
    /// 插件配置类
    /// 使用Json.NET进行序列化/反序列化
    /// </summary>
    public class Configuration
    {
        #region 配置属性
        /// <summary>
        /// 插件开关
        /// </summary>
        [JsonProperty("插件开关", Order = 0)]
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// 命令执行延迟（毫秒）
        /// 防止命令执行过快导致刷屏
        /// </summary>
        [JsonProperty("命令执行延迟(毫秒)", Order = 1)]
        public int CommandDelay { get; set; } = 100;
        
        /// <summary>
        /// 是否广播执行结果
        /// </summary>
        [JsonProperty("广播执行结果", Order = 2)]
        public bool BroadcastEnabled { get; set; } = true;
        
        /// <summary>
        /// 广播消息格式
        /// 支持占位符：{boss} - Boss名称, {count} - 执行命令数量
        /// </summary>
        [JsonProperty("广播消息格式", Order = 3)]
        public string BroadcastFormat { get; set; } = "[c/32CD32:Boss命令] {boss} 已被击败，已自动执行 {count} 条命令。";
        
        /// <summary>
        /// 广播颜色配置
        /// </summary>
        [JsonProperty("广播颜色", Order = 4)]
        public ColorConfig BroadcastColor { get; set; } = new ColorConfig(50, 205, 50);
        
        /// <summary>
        /// 是否在控制台显示执行成功提示
        /// </summary>
        [JsonProperty("控制台提示", Order = 5)]
        public bool ConsoleSuccessPrompt { get; set; } = true;
        
        /// <summary>
        /// 是否自动广播BOSS伤害排行
        /// </summary>
        [JsonProperty("自动广播BOSS伤害排行", Order = 6)]
        public bool AutoBroadcastDamageRanking { get; set; } = true;
        
        /// <summary>
        /// Boss命令配置列表
        /// 支持为多个Boss配置不同的命令
        /// </summary>
        [JsonProperty("Boss命令配置", Order = 7)]
        public List<BossCommandConfig> BossCommands { get; set; } = new List<BossCommandConfig>();
        
        /// <summary>
        /// 浮动文本配置列表
        /// 为每个Boss配置击杀后显示的浮动文本
        /// </summary>
        [JsonProperty("浮动文本配置", Order = 8)]
        public List<FloatingTextConfig> FloatingTexts { get; set; } = new List<FloatingTextConfig>();
        #endregion

        #region 数据类
        /// <summary>
        /// Boss命令配置
        /// </summary>
        public class BossCommandConfig
        {
            /// <summary>
            /// Boss名称（显示用）
            /// </summary>
            [JsonProperty("Boss名称", Order = 0)]
            public string Name { get; set; } = "";
            
            /// <summary>
            /// Boss ID列表
            /// 一个Boss可能有多个形态，可以配置多个ID
            /// </summary>
            [JsonProperty("Boss ID列表", Order = 1)]
            public List<int> BossIDs { get; set; } = new List<int>();
            
            /// <summary>
            /// 是否需要被召唤
            /// true: 只对玩家召唤的Boss生效
            /// false: 对所有Boss生效（包括自然生成的）
            /// </summary>
            [JsonProperty("需要被召唤", Order = 2)]
            public bool RequireSummoned { get; set; } = true;
            
            /// <summary>
            /// 是否记录执行次数
            /// </summary>
            [JsonProperty("记录执行次数", Order = 3)]
            public bool RecordExecutionCount { get; set; } = true;
            
            /// <summary>
            /// 当前执行次数
            /// </summary>
            [JsonProperty("执行次数", Order = 4)]
            public int ExecutionCount { get; set; } = 0;
            
            /// <summary>
            /// 是否已首次击杀
            /// </summary>
            [JsonProperty("已首次击杀", Order = 5)]
            public bool FirstKillDone { get; set; } = false;
            
            /// <summary>
            /// 首次击杀执行的命令列表
            /// 支持占位符，只在首次击杀时执行一次
            /// </summary>
            [JsonProperty("首次击杀命令", Order = 6)]
            public List<string> FirstKillCommands { get; set; } = new List<string>();
            
            /// <summary>
            /// 常规执行的命令列表
            /// 支持占位符，每次击杀都会执行
            /// </summary>
            [JsonProperty("常规执行命令", Order = 7)]
            public List<string> Commands { get; set; } = new List<string>();
            
            /// <summary>
            /// 是否广播此Boss的命令执行结果
            /// </summary>
            [JsonProperty("广播结果", Order = 8)]
            public bool BroadcastResult { get; set; } = true;
            
            /// <summary>
            /// 构造函数（供Json.NET反序列化使用）
            /// </summary>
            [JsonConstructor]
            public BossCommandConfig() { }
            
            /// <summary>
            /// 完整构造函数
            /// </summary>
            public BossCommandConfig(string name, int[] bossIds, bool requireSummoned, 
                string[] firstKillCommands, string[] commands, bool broadcastResult = true, 
                bool recordExecutionCount = true)
            {
                Name = name;
                BossIDs = new List<int>(bossIds);
                RequireSummoned = requireSummoned;
                FirstKillCommands = new List<string>(firstKillCommands);
                Commands = new List<string>(commands);
                BroadcastResult = broadcastResult;
                RecordExecutionCount = recordExecutionCount;
                ExecutionCount = 0;
                FirstKillDone = false;
            }
        }
        
        /// <summary>
        /// 浮动文本配置
        /// </summary>
        public class FloatingTextConfig
        {
            /// <summary>
            /// Boss名称
            /// </summary>
            [JsonProperty("Boss名称", Order = 0)]
            public string BossName { get; set; } = "";
            
            /// <summary>
            /// Boss ID列表
            /// </summary>
            [JsonProperty("Boss ID列表", Order = 1)]
            public List<int> BossIDs { get; set; } = new List<int>();
            
            /// <summary>
            /// 浮动文本内容
            /// 支持占位符：{boss} - Boss名称
            /// </summary>
            [JsonProperty("文本内容", Order = 2)]
            public string Text { get; set; } = "";
            
            /// <summary>
            /// 文本颜色
            /// </summary>
            [JsonProperty("文本颜色", Order = 3)]
            public ColorConfig TextColor { get; set; } = new ColorConfig(255, 255, 255);
            
            /// <summary>
            /// 显示时长（秒）
            /// </summary>
            [JsonProperty("显示时长(秒)", Order = 4)]
            public int Duration { get; set; } = 5;
            
            /// <summary>
            /// 是否启用
            /// </summary>
            [JsonProperty("是否启用", Order = 5)]
            public bool Enabled { get; set; } = true;
            
            /// <summary>
            /// 构造函数
            /// </summary>
            [JsonConstructor]
            public FloatingTextConfig() { }
            
            /// <summary>
            /// 完整构造函数
            /// </summary>
            public FloatingTextConfig(string bossName, int[] bossIds, string text, 
                ColorConfig textColor, int duration = 5, bool enabled = true)
            {
                BossName = bossName;
                BossIDs = new List<int>(bossIds);
                Text = text;
                TextColor = textColor;
                Duration = duration;
                Enabled = enabled;
            }
        }
        
        /// <summary>
        /// 颜色配置类
        /// 用于定义广播消息的颜色
        /// </summary>
        public class ColorConfig
        {
            /// <summary>
            /// 红色分量 (0-255)
            /// </summary>
            [JsonProperty("R")]
            public byte R { get; set; }
            
            /// <summary>
            /// 绿色分量 (0-255)
            /// </summary>
            [JsonProperty("G")]
            public byte G { get; set; }
            
            /// <summary>
            /// 蓝色分量 (0-255)
            /// </summary>
            [JsonProperty("B")]
            public byte B { get; set; }
            
            /// <summary>
            /// 转换为XNA颜色对象
            /// </summary>
            [JsonIgnore]
            public Color XnaColor => new Color(R, G, B);
            
            /// <summary>
            /// 构造函数（供Json.NET反序列化使用）
            /// </summary>
            [JsonConstructor]
            public ColorConfig() { }
            
            /// <summary>
            /// RGB构造函数
            /// </summary>
            public ColorConfig(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }
            
            /// <summary>
            /// 从XNA颜色构造
            /// </summary>
            public ColorConfig(Color color)
            {
                R = color.R;
                G = color.G;
                B = color.B;
            }
        }
        #endregion

        #region 默认配置
        /// <summary>
        /// 设置默认配置
        /// 当配置文件不存在时，使用此配置
        /// </summary>
        public void SetDefaults()
        {
            // 清空现有配置
            BossCommands.Clear();
            FloatingTexts.Clear();
            
            // 配置所有Terraria Boss
            ConfigureAllBosses();
            
            // 设置浮动文本
            SetDefaultFloatingTexts();
            
            // 写入配置
            Write();
        }
        
        /// <summary>
        /// 配置所有Terraria Boss
        /// </summary>
        private void ConfigureAllBosses()
        {
            // 史莱姆王
            BossCommands.Add(new BossCommandConfig(
                "史莱姆王",
                new[] { 50 },
                false,
                new[]
                {
                    "/bc 关注[c/66CCFF:星梦]喵，关注[c/66CCFF:星梦]谢谢喵！",
                    "/bc 恭喜玩家首次击杀史莱姆王，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 克苏鲁之眼
            BossCommands.Add(new BossCommandConfig(
                "克苏鲁之眼",
                new[] { 4 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀克苏鲁之眼，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 克苏鲁之脑
            BossCommands.Add(new BossCommandConfig(
                "克苏鲁之脑",
                new[] { 266 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀克苏鲁之脑，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 世界吞噬者
            BossCommands.Add(new BossCommandConfig(
                "世界吞噬者",
                new[] { 13, 14, 15 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀世界吞噬者，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 蜂后
            BossCommands.Add(new BossCommandConfig(
                "蜂后",
                new[] { 222 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀蜂后，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 巨鹿
            BossCommands.Add(new BossCommandConfig(
                "巨鹿",
                new[] { 668 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀巨鹿，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 骷髅王
            BossCommands.Add(new BossCommandConfig(
                "骷髅王",
                new[] { 35 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀骷髅王，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 血肉墙
            BossCommands.Add(new BossCommandConfig(
                "血肉墙",
                new[] { 113, 114 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀血肉墙，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[]
                {
                    "time 0",
                    "spawnmob 134 5",
                    "say 困难模式已开启！",
                    "bc 世界已进入困难模式！"
                } // 移除/bossdamage命令
            ));
            
            // 史莱姆之皇
            BossCommands.Add(new BossCommandConfig(
                "史莱姆之皇",
                new[] { 657 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀史莱姆之皇，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 毁灭者
            BossCommands.Add(new BossCommandConfig(
                "毁灭者",
                new[] { 134, 135, 136 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀毁灭者，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 机械骷髅王
            BossCommands.Add(new BossCommandConfig(
                "机械骷髅王",
                new[] { 127 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀机械骷髅王，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 机械魔眼
            BossCommands.Add(new BossCommandConfig(
                "机械魔眼",
                new[] { 125, 126 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀机械魔眼，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 世纪之花
            BossCommands.Add(new BossCommandConfig(
                "世纪之花",
                new[] { 262 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀世纪之花，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 石巨人
            BossCommands.Add(new BossCommandConfig(
                "石巨人",
                new[] { 245 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀石巨人，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 猪鱼龙公爵
            BossCommands.Add(new BossCommandConfig(
                "猪鱼龙公爵",
                new[] { 370 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀猪鱼龙公爵，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 光之女皇
            BossCommands.Add(new BossCommandConfig(
                "光之女皇",
                new[] { 636 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀光之女皇，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 拜月教徒
            BossCommands.Add(new BossCommandConfig(
                "拜月教徒",
                new[] { 440 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀拜月教徒，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[] { } // 移除/bossdamage命令
            ));
            
            // 月球领主
            BossCommands.Add(new BossCommandConfig(
                "月球领主",
                new[] { 398 },
                true,
                new[]
                {
                    "/bc 恭喜玩家首次击杀月球领主，击杀boss后可使用[c/66CCFF:/bag rall]领取相应的boss进度礼包！",
                    "/give 74 *all* 1"
                },
                new string[]
                {
                    "/bc 感谢使用本插件喵，有问题请在Tshock群找星梦反馈喵！",
                    "giveall 74 10",
                    "time 0",
                    "spawnmob 1 20",
                    "bc 月亮领主已被击败！服务器将在一分钟后重启！",
                    "settimer 60 restart"
                } // 移除/bossdamage命令
            ));
        }
        
        /// <summary>
        /// 设置默认浮动文本
        /// </summary>
        private void SetDefaultFloatingTexts()
        {
            // 为所有Boss添加通用浮动文本
            foreach (var boss in BossCommands)
            {
                FloatingTexts.Add(new FloatingTextConfig(
                    boss.Name,
                    boss.BossIDs.ToArray(),
                    "Use /bag rall to receive the Progressbag",
                    new ColorConfig(102, 204, 255), // 浅蓝色
                    10,
                    true
                ));
            }
        }
        #endregion

        #region 配置文件读写
        /// <summary>
        /// 配置文件路径
        /// 位于TShock的SavePath目录下
        /// </summary>
        public static readonly string FilePath = Path.Combine(TShock.SavePath, "BossCommands.json");

        /// <summary>
        /// 将配置写入文件
        /// </summary>
        public void Write()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(FilePath, json);
                TShock.Log.ConsoleDebug($"[Boss命令执行器] 配置已保存到: {FilePath}");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Boss命令执行器] 保存配置失败: {ex}");
            }
        }

        /// <summary>
        /// 从文件读取配置
        /// 如果文件不存在，则创建默认配置
        /// </summary>
        /// <returns>配置对象</returns>
        public static Configuration Read()
        {
            if (!File.Exists(FilePath))
            {
                TShock.Log.ConsoleInfo($"[Boss命令执行器] 配置文件不存在，创建默认配置: {FilePath}");
                
                var config = new Configuration();
                config.SetDefaults();
                config.Write();
                return config;
            }
            
            try
            {
                var json = File.ReadAllText(FilePath);
                var config = JsonConvert.DeserializeObject<Configuration>(json);
                
                if (config == null)
                {
                    TShock.Log.ConsoleError($"[Boss命令执行器] 配置文件格式错误，使用默认配置");
                    return new Configuration();
                }
                
                return config;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Boss命令执行器] 读取配置失败: {ex}");
                return new Configuration();
            }
        }
        #endregion
    }
}
