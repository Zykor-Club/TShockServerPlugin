using System.Reflection;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace CreepBlockFix;

// 修复 TShock 6.1.0 中 Allow*Creep 配置不生效的 Bug。
// 感染蔓延有两条路径，TShock 只挂了慢路径 SpreadGrass；
// 快路径 hardUpdateWorld → WorldGen.Convert（批量转化邻块）未挂钩，导致"关了还蔓延"。
// 本插件补挂快路径，双管齐下完整封堵感染蔓延。
[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public override string Name => Assembly.GetExecutingAssembly().GetName().Name!;
    public override Version Version => Assembly.GetExecutingAssembly().GetName().Version!;
    public override string Author => "星梦XM";
    public override string Description => "修复 TShock 猩红/腐化/神圣蔓延配置不生效的问题";

    public Plugin(Main game) : base(game) { }

    public override void Initialize()
    {
        HookEvents.Terraria.WorldGen.SpreadGrass += OnSpreadGrass;
        HookEvents.Terraria.WorldGen.hardUpdateWorld += OnHardUpdateWorld;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HookEvents.Terraria.WorldGen.SpreadGrass -= OnSpreadGrass;
            HookEvents.Terraria.WorldGen.hardUpdateWorld -= OnHardUpdateWorld;
        }
        base.Dispose(disposing);
    }

    // 慢路径：每次草转换触发一次。按目标草类型 e.grass 判断是否拦截被禁蔓延的群系。
    private static void OnSpreadGrass(
        object? sender,
        HookEvents.Terraria.WorldGen.SpreadGrassEventArgs args)
    {
        if (WorldGen.generatingWorld)
            return;
        // 判据对齐真实蔓延源集合 TileID.Sets.Spreads*（腐化草/猩红草/神圣草等全部蔓延草类型）。
        // 只在禁用对应蔓延时才拦截；拦截通过置 ContinueExecution=false 让本次草转换不再执行。
        int grass = args.grass;
        var config = TShock.Config.Settings;

        bool block =
            (!config.AllowCorruptionCreep && TileID.Sets.SpreadsCorruption[grass]) ||
            (!config.AllowCrimsonCreep && TileID.Sets.SpreadsCrimson[grass]) ||
            (!config.AllowHallowCreep && TileID.Sets.SpreadsHallow[grass]);

        if (block)
            args.ContinueExecution = false;
    }

    // 快路径：在蔓延源格处切断。命中被禁蔓延群系时置 ContinueExecution=false，使 Convert 不执行。
    private static void OnHardUpdateWorld(
        object? sender,
        HookEvents.Terraria.WorldGen.hardUpdateWorldEventArgs args)
    {
        if (WorldGen.generatingWorld)
            return;

        if (args.i < 0 || args.i >= Main.maxTilesX || args.j < 0 || args.j >= Main.maxTilesY)
            return;

        int type = Main.tile[args.i, args.j].type;
        var config = TShock.Config.Settings;

        bool block =
            (!config.AllowCorruptionCreep && TileID.Sets.SpreadsCorruption[type]) ||
            (!config.AllowCrimsonCreep && TileID.Sets.SpreadsCrimson[type]) ||
            (!config.AllowHallowCreep && TileID.Sets.SpreadsHallow[type]);

        if (block)
            args.ContinueExecution = false;
    }
}