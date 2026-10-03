using Terraria;

namespace AutoResetPlus;

internal static class SeedRegistry
{
    public static readonly Dictionary<string[], Action> SeedActions = new()
    {
        [SeedConsts.NoTraps] = () => WorldGen.noTrapsWorldGen = true,
        [SeedConsts.NotTheBees] = () => WorldGen.notTheBees = true,
        [SeedConsts.ForTheWorthy] = () => WorldGen.getGoodWorldGen = true,
        [SeedConsts.Remix] = () => WorldGen.remixWorldGen = true,
        [SeedConsts.CelebrationMk10] = () => WorldGen.tenthAnniversaryWorldGen = true,
        [SeedConsts.TheConstant] = () => WorldGen.dontStarveWorldGen = true,

        [SeedConsts.GetFixedBoi] = () =>
        {
            WorldGen.noTrapsWorldGen = true;
            WorldGen.notTheBees = true;
            WorldGen.getGoodWorldGen = true;
            WorldGen.tenthAnniversaryWorldGen = true;
            WorldGen.dontStarveWorldGen = true;
            WorldGen.remixWorldGen = true;
            WorldGen.everythingWorldGen = true;
        }
    };
}