using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Terraria;
using Terraria.ID;
using AIAgent.Core;

namespace AIAgent.Crafting;

public sealed record Ingredient(int Id, string Name, int Stack);

public sealed record RecipeView(int ResultStack, IReadOnlyList<Ingredient> Ingredients, IReadOnlyList<string> Stations);

/// <summary>
/// 从游戏运行时（Main.recipe）建立的合成配方索引。
/// 这是权威数据：物品名用 Lang.GetItemNameValue（服务端语言为中文时就是中文名）。
/// </summary>
public sealed class RecipeIndex
{
    private static readonly Dictionary<string, string> StationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WorkBenches"] = "工作台",
        ["Furnaces"] = "熔炉",
        ["Anvils"] = "铁砧",
        ["MythrilAnvil"] = "秘银砧",
        ["Hellforge"] = "地狱熔炉",
        ["AdamantiteForge"] = "精金熔炉",
        ["DemonAltars"] = "恶魔祭坛",
        ["Loom"] = "织布机",
        ["LivingLoom"] = "生命织布机",
        ["Table"] = "桌子",
        ["CookingPot"] = "烹饪锅",
        ["Bottle"] = "瓶子",
        ["Sawmill"] = "锯木机",
        ["Solidifier"] = "固化机",
        ["DyeVat"] = "染缸",
        ["ImbuingStation"] = "灌注站",
        ["Bookcase"] = "书架",
        ["CrystalBall"] = "水晶球",
        ["IceMachine"] = "冰雪机",
        ["SkyMill"] = "天磨",
        ["BoneWelder"] = "骨头焊接机",
        ["GlassKiln"] = "玻璃窑",
        ["HoneyDispenser"] = "蜂蜜分配器",
        ["FleshCloningVat"] = "血肉克隆桶",
        ["Autohammer"] = "自动锤",
        ["SteampunkBoiler"] = "蒸汽朋克锅炉",
        ["BlendOMatic"] = "混合机",
        ["MeatGrinder"] = "绞肉机",
        ["DecayChamber"] = "腐化室",
        ["AncientManipulator"] = "远古操纵机",
        ["LunarCraftingStation"] = "远古操纵机",
    };

    private readonly Dictionary<int, List<RecipeView>> _byResult;
    private readonly Dictionary<string, int> _byName;
    private readonly Dictionary<int, string> _nameOf;

    private RecipeIndex(
        Dictionary<int, List<RecipeView>> byResult,
        Dictionary<string, int> byName,
        Dictionary<int, string> nameOf)
    {
        _byResult = byResult;
        _byName = byName;
        _nameOf = nameOf;
    }

    public int RecipeCount => _byResult.Sum(pair => pair.Value.Count);

    public static RecipeIndex Build()
    {
        var watch = Stopwatch.StartNew();
        var recipes = Main.recipe ?? Array.Empty<Recipe>();

        var byResult = new Dictionary<int, List<RecipeView>>();
        foreach (var recipe in recipes)
        {
            if (recipe?.createItem is null) continue;

            var resultId = recipe.createItem.type;
            if (resultId <= 0) continue;

            var ingredients = new List<Ingredient>();
            foreach (var required in recipe.requiredItem ?? Array.Empty<Item>())
            {
                if (required is null || required.type <= 0 || required.stack <= 0) continue;
                ingredients.Add(new Ingredient(required.type, LocalizedName(required.type), required.stack));
            }

            if (ingredients.Count == 0) continue;

            var stations = new List<string>();
            var tile = recipe.requiredTile;
            if (tile >= 0) stations.Add(StationName(tile));

            if (!byResult.TryGetValue(resultId, out var list))
            {
                list = new List<RecipeView>();
                byResult[resultId] = list;
            }

            list.Add(new RecipeView(recipe.createItem.stack, ingredients, stations));
        }

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nameOf = new Dictionary<int, string>();

        for (var id = 1; id < ItemID.Count; id++)
        {
            var localized = Lang.GetItemNameValue(id);
            if (string.IsNullOrWhiteSpace(localized)) continue;
            nameOf[id] = localized;
            byName.TryAdd(localized, id);

            var internalName = ItemID.Search.GetName(id);
            if (!string.IsNullOrWhiteSpace(internalName)) byName.TryAdd(internalName, id);
        }

        watch.Stop();
        PluginLog.Info($"配方索引已建立: {byResult.Count} 种产物 / {byResult.Sum(p => p.Value.Count)} 条配方 | 物品名 {byName.Count} | {watch.ElapsedMilliseconds}ms");

        return new RecipeIndex(byResult, byName, nameOf);
    }

    public string ItemName(int id) =>
        _nameOf.TryGetValue(id, out var name) ? name : LocalizedName(id);

    private static string LocalizedName(int id)
    {
        var localized = Lang.GetItemNameValue(id);
        return string.IsNullOrWhiteSpace(localized) ? ItemID.Search.GetName(id) : localized;
    }

    public bool TryResolve(string query, out int id, out string name)
    {
        id = 0;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return false;

        var text = query.Trim();
        if (int.TryParse(text, out var parsed) && parsed > 0 && parsed < ItemID.Count)
        {
            id = parsed;
            name = ItemName(parsed);
            return true;
        }

        if (_byName.TryGetValue(text, out id))
        {
            name = ItemName(id);
            return true;
        }

        var candidate = _byName.Keys
            .Where(key => key.Contains(text, StringComparison.OrdinalIgnoreCase) && key.Length >= 2)
            .OrderBy(key => key.Length)
            .FirstOrDefault();

        if (candidate is null) return false;

        id = _byName[candidate];
        name = ItemName(id);
        return true;
    }

    public IReadOnlyList<RecipeView> RecipesFor(int id) =>
        _byResult.TryGetValue(id, out var list) ? list : Array.Empty<RecipeView>();

    public bool IsCraftable(int id) => _byResult.ContainsKey(id);

    /// <summary>反查：这个材料能用来做什么。</summary>
    public IReadOnlyList<(int ResultId, string ResultName, string Station, int ResultStack)> FindUses(int ingredientId, int limit = 10)
    {
        var results = new List<(int, string, string, int)>();

        foreach (var pair in _byResult)
        {
            foreach (var recipe in pair.Value)
            {
                if (!recipe.Ingredients.Any(i => i.Id == ingredientId)) continue;
                results.Add((pair.Key, ItemName(pair.Key), recipe.Stations.FirstOrDefault() ?? "徒手", recipe.ResultStack));
                break;
            }

            if (results.Count >= limit) break;
        }

        return results;
    }

    private static string StationName(int tileId)
    {
        var internalName = TileID.Search.GetName(tileId);
        if (string.IsNullOrWhiteSpace(internalName)) return $"tile#{tileId}";
        return StationNames.TryGetValue(internalName, out var localized) ? localized : internalName;
    }
}
