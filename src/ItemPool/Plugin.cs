using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace ItemPool;

/// <summary>
/// ItemPool 物品自选池插件 — 让玩家从预设物品池中自选领取物品
/// </summary>
[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public override string Name => Assembly.GetExecutingAssembly().GetName().Name!;
    public override Version Version => Assembly.GetExecutingAssembly().GetName().Version!;
    public override string Author => "星梦XM";
    public override string Description => "物品自选池插件，让玩家从预设物品池中自选领取物品";

    public Plugin(Main game) : base(game) { }

    public override void Initialize()
    {
        // 加载配置文件
        ItemPoolConfig.Read();

        // 初始化数据库表
        ItemPoolDatabase.Initialize();

        // 注册主命令 /xz，通过路由分发到各子命令
        Commands.ChatCommands.Add(new Command("xz.use", ItemPoolCommands.XzRoute, "xz", "选择"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 使用反射批量移除本程序集注册的所有命令
            var asm = Assembly.GetExecutingAssembly();
            Commands.ChatCommands.RemoveAll(c =>
                c.CommandDelegate.Method?.DeclaringType?.Assembly == asm);
        }
        base.Dispose(disposing);
    }
}
