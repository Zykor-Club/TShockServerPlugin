using System.Reflection;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace CreepBlockFix;

// 修复 TShock 6.1.0 中 Allow*Creep 配置不生效的 Bug。
//
// 实测结论：TShock 只阻止了"邪/神草皮的蔓延"（即把普通泥土染成邪/神草这一步，
// 走 WorldGrassSpread / SpreadGrass 钩子）；对已存在的草皮、石头、沙子在 hardUpdateWorld
// → WorldGen.Convert 被批量替换成感染块这条真正的"大蔓延"路径完全没有拦截
// （其底层 GameHardmodeTileUpdate / InvokeHardmodeTileUpdate 在 OTAPI 中无调用点，钩子失效）。
//
// 本插件补挂 hardUpdateWorld：在蔓延源格处直接切断，使 Convert 不再执行，从而
// 连同 TShock 拦不住的石/沙/草皮批量转化一起封堵；同时保留 SpreadGrass 拦染草。
[ApiVersion(2, 1)]
public class CreepBlockFix : TerrariaPlugin
{
    public override string Name => "CreepBlockFix";
    public override Version Version => new Version(1, 0, 0);
    public override string Author => "星梦XM";
    public override string Description => "修复 TShock 猩红/腐化/神圣蔓延配置不生效的问题";

    public CreepBlockFix(Main game) : base(game) { }

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

    // SpreadGrass（慢路径，TShock 已挂钩）：把泥土染成草。这里仅拦"染草"这一步，
    // 复判目标草类型，禁用对应蔓延时置 ContinueExecution=false 取消本次染草。
    private static void OnSpreadGrass(
        object? sender,
        HookEvents.Terraria.WorldGen.SpreadGrassEventArgs args)
    {
        if (WorldGen.generatingWorld)
            return;
        // 判据对齐真实蔓延草集合 TileID.Sets.SpreadsCorruption/SpreadsCrimson/SpreadsHallow。
        // 只在禁用对应蔓延时才拦截。
        int grass = args.grass;
        var config = TShock.Config.Settings;

        bool block =
            (!config.AllowCorruptionCreep && TileID.Sets.SpreadsCorruption[grass]) ||
            (!config.AllowCrimsonCreep && TileID.Sets.SpreadsCrimson[grass]) ||
            (!config.AllowHallowCreep && TileID.Sets.SpreadsHallow[grass]);

        if (block)
            args.ContinueExecution = false;
    }

    // hardUpdateWorld（快路径，TShock 未挂钩）：蔓延源格的主循环入口。
    // 命中被禁蔓延的源格时置 ContinueExecution=false，使后续 WorldGen.Convert 不执行，
    // 从而封堵 TShock 拦不住的草皮/石/沙批量转化。
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