extern alias TrAlias;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TShockAPI;
using static FakePlayer.Plugin;

namespace FakePlayer;

internal class Configuration
{
    #region 配置项成员
    [JsonProperty("使用说明", Order = -100)]
    public List<string> UsageNotes { get; set; } = new List<string>();
    [JsonProperty("修复Shock层内存泄露", Order = 0)]
    public bool UsePoolSock { get; set; } = true;
    [JsonProperty("名称前缀", Order = 1)] 
    public string Names { get; set; } = "假人";
    [JsonProperty("自动注册", Order = 2)] 
    public bool AutoRegister { get; set; } = true;
    [JsonProperty("注册密码", Order = 3)] 
    public string DefPass { get; set; } = "123456";
    [JsonProperty("默认队伍", Order = 4)] 
    public int DefTeam { get; set; } = 4;
    [JsonProperty("人物版本", Order = 5)] 
    public string Version { get; set; } = "Terraria326";
    [JsonProperty("更新频率(帧)", Order = 6)] 
    public int UpdateTime { get; set; } = 20;
    [JsonProperty("触发移动格数", Order = 7)] 
    public int StopDist { get; set; } = 2;
    [JsonProperty("横向移动速度(格/秒)", Order = 8)]
    public float MoveSpeed { get; set; } = 12f;  // 单位：格/秒
    [JsonProperty("跟随玩家超距离传送格数", Order = 9)]
    public int TpPlayer { get; set; } = 100;
    [JsonProperty("传送秒数", Order = 10)]
    public int TpCD { get; set; } = 3;

    [JsonProperty("寻敌需要视线", Order = 11)]
    public bool CanSeeNpc { get; set; } = false;
    [JsonProperty("寻敌检查半径", Order = 12)]
    public int NpcRange { get; set; } = 40;
    [JsonProperty("漫游随机范围格数", Order = 14)]
    public int RoamDist { get; set; } = 500;
    [JsonProperty("漫游攻击敌怪", Order = 15)]
    public bool RoamAttack { get; set; } = true;
    [JsonProperty("漫游垂直比例", Order = 18)]
    public float RoamVert { get; set; } = 0.5f;

    [JsonProperty("排斥怪物", Order = 19)]
    public bool RepelNpc { get; set; } = true;
    [JsonProperty("排斥怪物力度(像素/帧)", Order = 20)]
    public float RepelNpcForce { get; set; } = 4f;

    [JsonProperty("攻击距离缩放", Order = 23)] 
    public float AtkDistMul { get; set; } = 0.8f;
    [JsonProperty("远程攻击范围格数", Order = 24)] 
    public int RangeRng { get; set; } = 50;

    [JsonProperty("躲避弹幕开关", Order = 53)]
    public bool DodgeProj { get; set; } = true;
    [JsonProperty("躲避弹幕检查半径", Order = 54)]
    public float DodgeCheckRadius { get; set; } = 30f;
    [JsonProperty("躲避弹幕范围(格)", Order = 55)]
    public float DodgeRange { get; set; } = 25f;
    [JsonProperty("躲避弹幕预判帧数", Order = 56)]
    public int DodgeLook { get; set; } = 8;
    [JsonProperty("躲避弹幕冷却帧数", Order = 57)]
    public int DodgeCool { get; set; } = 20;
    [JsonProperty("躲避弹幕碰撞箱扩大(格)", Order = 58)]
    public float DgExpand { get; set; } = 4f;

    [JsonProperty("最大假人数", Order = 60)]
    public int MaxFakes { get; set; } = 1;

    [JsonProperty("默认发言", Order = 61)]
    public bool Speak { get; set; } = true;

    [JsonProperty("自动创建分身", Order = 62)]
    public bool AutoSpawn { get; set; } = false;

    [JsonProperty("分身名称", Order = 63)]
    public string AvatarName { get; set; } = "香草";

    [JsonProperty("断线重连", Order = 64)]
    public bool AutoReconnect { get; set; } = true;

    [JsonProperty("禁止靠近NPC", Order = 59)]
    public List<int> BlockNpc { get; set; } = new();     // 黑名单NPC ID
    #endregion

    #region 预设参数方法

    public void SetDefault()
    {
        UsageNotes ??= new List<string>();
        UsageNotes.Clear();
        UsageNotes.AddRange(
        [
            "=== 假人插件使用说明 ===",
            "1.把TrProtocol.dll与假人插件放ServerPlugin文件夹并重启" ,
            "2.使用指令/f re 创建一个假人",
            "3.用/f ap me all 把自身装备复制给假人",
            "4.再用/f me all 让所有假人跟随自己】",
            "《如果发现假人站在原地不动》：",
            "请使用指令修改假人密码:/user password “名字” 123456",
            "可能有插件干扰自动注册，请确保假人与以下密码一致：",
            "tshock/假人插件/假人/**.json文件中的【密码】",
            
        ]);
    }
    #endregion

    #region 读取与创建配置文件方法
    public static readonly string CfgPath = Path.Combine(TShock.SavePath, PluginName, $"配置文件.json"); // 配置文件路径
    public void Write()
    {
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(CfgPath, json);
    }
    public static Configuration Read()
    {
        if (!File.Exists(CfgPath))
        {
            var json = new Configuration();
            json.SetDefault();
            json.Write();
            return json;
        }
        else
        {
            try
            {
                string json = File.ReadAllText(CfgPath);
                var config = JsonConvert.DeserializeObject<Configuration>(json)!;
                return config;
            }
            catch (JsonReaderException ex)
            {
                string json = File.ReadAllText(CfgPath);
                string[] lines = json.Split('\n');
                int line = ex.LineNumber;
                int idx = Math.Max(0, Math.Min(line - 2, lines.Length - 1));
                string text = lines[idx].Trim();
                throw new Exception($"位置: 第 {line - 1} 行\n" +
                                    $"内容: {text ?? string.Empty}\n" +
                                    $"路径: {FormatPath(ex.Path ?? string.Empty)}", ex);
            }
        }
    }

    public static string FormatPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // 使用正则表达式匹配 "[数字]"
        return Regex.Replace(path, @"\[(\d+)\]", match =>
        {
            int index = int.Parse(match.Groups[1].Value);
            return $":第{index + 1}项";
        });
    }
    #endregion
}