using System.Text;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace ItemPool;

/// <summary>所有命令处理方法</summary>
public static class ItemPoolCommands
{
    private const int ItemsPerPage = 30;

    /// <summary>命令路由</summary>
    public static void XzRoute(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            ShowHelp(args);
            return;
        }

        var subCommand = args.Parameters[0].ToLowerInvariant();
        var subArgs = args.Parameters.Skip(1).ToList();

        switch (subCommand)
        {
            case "list":
                if (subArgs.Count > 0 && int.TryParse(subArgs[0], out int poolNum) && poolNum > 0)
                    ShowPoolByIndex(args, poolNum);
                else
                    XzList(args);
                break;
            case "info":
                XzInfo(args, subArgs);
                break;
            case "pick":
                XzPick(args, subArgs);
                break;
            case "on":
                CheckAdmin(args, () => XzOn(args, subArgs));
                break;
            case "off":
                CheckAdmin(args, () => XzOff(args, subArgs));
                break;
            case "reload":
                CheckAdmin(args, () => XzReload(args));
                break;
            case "reset":
                CheckAdmin(args, () => XzReset(args, subArgs));
                break;
            case "resetall":
                CheckAdmin(args, () => XzResetAll(args, subArgs));
                break;
            default:
                if (int.TryParse(subCommand, out int num) && num > 0)
                    ShowPoolByIndex(args, num);
                else
                    args.Player.SendErrorMessage($"未知子命令: /xz {subCommand}\n输入 /xz 查看帮助");
                break;
        }
    }

    /// <summary>显示帮助页面（渐变色）</summary>
    private static void ShowHelp(CommandArgs args)
    {
        var isAdmin = args.Player.HasPermission("xz.admin");
        var lines = new List<string>
        {
            $"[i:3453] ItemPool 物品自选池 [i:3455]",
            "                     — 开发 by星梦 —",
            ""
        };

        lines.Add("[i:3456] 玩家命令 [i:3456]");
        lines.Add("/xz                   查看此帮助");
        lines.Add("/xz list              浏览所有物品池");
        lines.Add("/xz list <编号>        直接查看指定池物品");
        lines.Add("/xz info <池名或编号>   查看池内物品详情");
        lines.Add("/xz pick <池名或编号> <编号>  领取物品");

        if (isAdmin)
        {
            lines.Add("");
            lines.Add("[i:3459] 管理命令 [i:3459]");
            lines.Add("/xz on <池名或编号>      开启物品池");
            lines.Add("/xz off <池名或编号>     关闭物品池");
            lines.Add("/xz reload             热重载配置文件");
            lines.Add("/xz reset <玩家> [池名或编号]   重置玩家记录");
            lines.Add("/xz resetall [池名或编号]      重置全部记录");
        }

        SendGradient(args.Player, lines, Color.Cyan, Color.MediumPurple);
    }

    /// <summary>检查管理员权限</summary>
    private static void CheckAdmin(CommandArgs args, Action action)
    {
        if (!args.Player.HasPermission("xz.admin"))
        {
            args.Player.SendErrorMessage("你没有权限执行此命令！");
            return;
        }
        action();
    }

    /// <summary>按名称或编号查找池（名称优先，再试编号）</summary>
    private static ItemPoolEntry? FindPoolByInput(TSPlayer player, string input)
    {
        var pool = ItemPoolConfig.FindPool(input);
        if (pool != null)
            return pool;

        var isAdmin = player.HasPermission("xz.admin");
        var visiblePools = ItemPoolConfig.Instance.物品池列表
            .Where(p => isAdmin || p.运行时启用)
            .ToList();

        if (int.TryParse(input, out int index) && index >= 1 && index <= visiblePools.Count)
            return visiblePools[index - 1];

        return null;
    }

    /// <summary>按名称或编号解析池名</summary>
    private static string? ResolvePoolName(TSPlayer player, string input)
    {
        var pool = FindPoolByInput(player, input);
        return pool?.池名称;
    }

    /// <summary>/xz list — 列出所有可用物品池（带全局编号）</summary>
    private static void XzList(CommandArgs args)
    {
        var isAdmin = args.Player.HasPermission("xz.admin");
        var enabledPools = ItemPoolConfig.Instance.物品池列表
            .Select((p, i) => new { Pool = p, Index = i + 1 })
            .Where(x => isAdmin || x.Pool.运行时启用)
            .ToList();

        if (enabledPools.Count == 0)
        {
            args.Player.SendInfoMessage("当前没有可用的物品池！");
            return;
        }

        var lines = new List<string> { "物品池列表:" };
        for (int i = 0; i < enabledPools.Count; i++)
        {
            var entry = enabledPools[i];
            var pool = entry.Pool;
            var status = pool.运行时启用 ? "[开]" : "[关]";
            lines.Add($"  {entry.Index}. {status} {pool.池名称} — {pool.说明} ({pool.模式描述})");
        }
        lines.Add("输入 /xz list <编号> 查看池内物品");

        SendGradient(args.Player, lines, Color.LimeGreen, Color.Cyan);
    }

    /// <summary>按全局编号显示池物品</summary>
    private static void ShowPoolByIndex(CommandArgs args, int poolNum)
    {
        var isAdmin = args.Player.HasPermission("xz.admin");
        var visiblePools = ItemPoolConfig.Instance.物品池列表
            .Where(p => isAdmin || p.运行时启用)
            .ToList();

        if (poolNum < 1 || poolNum > visiblePools.Count)
        {
            args.Player.SendErrorMessage($"编号无效！当前共 {visiblePools.Count} 个可用物品池。");
            return;
        }

        ShowPoolInfo(args.Player, visiblePools[poolNum - 1], 1);
    }

    /// <summary>/xz info <池名或编号> [页码]</summary>
    private static void XzInfo(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count < 1 || subArgs.Count > 2)
        {
            args.Player.SendErrorMessage("用法: /xz info <池名或编号> [页码]");
            return;
        }

        var pool = FindPoolByInput(args.Player, subArgs[0]);
        if (pool == null)
        {
            args.Player.SendErrorMessage("指定物品池不存在！");
            return;
        }

        int page = 1;
        if (subArgs.Count == 2 && (!int.TryParse(subArgs[1], out page) || page < 1))
        {
            args.Player.SendErrorMessage("页码无效！");
            return;
        }

        ShowPoolInfo(args.Player, pool, page);
    }

    /// <summary>显示单个池的物品信息</summary>
    private static void ShowPoolInfo(TSPlayer player, ItemPoolEntry pool, int page)
    {
        var allItems = pool.物品列表;
        int totalPages = (int)Math.Ceiling((double)allItems.Count / ItemsPerPage);
        if (page > totalPages) page = totalPages;

        if (allItems.Count == 0)
        {
            player.SendMessage($"[{pool.池名称}] 该池暂无物品！", Color.LimeGreen);
            return;
        }

        var pageItems = allItems.Skip((page - 1) * ItemsPerPage).Take(ItemsPerPage).ToList();
        var pickedItems = ItemPoolDatabase.GetPickedItems(player.Name, pool.池名称);

        if (pool.是否按物品模式 && allItems.All(it => pickedItems.Contains(it.物品ID)))
        {
            player.SendMessage($"[{pool.池名称}] 该池所有物品已被领完！", Color.LimeGreen);
            return;
        }

        if (!pool.是否按物品模式 && pool.最大领取次数 > 0)
        {
            int count = ItemPoolDatabase.GetPickCount(player.Name, pool.池名称);
            if (count >= pool.最大领取次数)
            {
                player.SendMessage($"[{pool.池名称}] 你已用完该物品池的领取次数！({count}/{pool.最大领取次数})", Color.LimeGreen);
                return;
            }
        }

        var lines = new List<string>();
        if (totalPages > 1)
            lines.Add($"[{pool.池名称}] 物品列表 (第 {page}/{totalPages} 页):");
        else
            lines.Add($"[{pool.池名称}] 物品列表:");

        int startIndex = (page - 1) * ItemsPerPage;
        for (int i = 0; i < pageItems.Count; i++)
        {
            var item = pageItems[i];
            int num = startIndex + i + 1;
            var tag = item.数量 > 1
                ? $"[i/s{item.数量}:{item.物品ID}]"
                : $"[i:{item.物品ID}]";
            string prefixStr = item.前缀 > 0 ? $" (前缀:{item.前缀})" : "";
            string pickedStr = pickedItems.Contains(item.物品ID) ? " [已领取]" : "";
            lines.Add($"{num}. {tag}{prefixStr}{pickedStr}");
        }

        if (totalPages > 1)
            lines.Add($"输入 /xz info {pool.池名称} <页码> 翻页");

        SendGradient(player, lines, Color.LimeGreen, Color.Cyan);
    }

    /// <summary>/xz pick <池名或编号> <编号></summary>
    private static void XzPick(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count != 2)
        {
            args.Player.SendErrorMessage("用法: /xz pick <池名或编号> <编号>");
            return;
        }

        var pool = FindPoolByInput(args.Player, subArgs[0]);
        if (pool == null)
        {
            args.Player.SendErrorMessage("指定物品池不存在！");
            return;
        }

        if (!pool.运行时启用 && !args.Player.HasPermission("xz.admin"))
        {
            args.Player.SendErrorMessage("该物品池暂未开放！");
            return;
        }

        if (!int.TryParse(subArgs[1], out int itemNumber) || itemNumber < 1 || itemNumber > pool.物品列表.Count)
        {
            args.Player.SendErrorMessage("物品编号无效！请输入正确的编号。");
            return;
        }

        var selectedItem = pool.物品列表[itemNumber - 1];
        var playerName = args.Player.Name;

        if (pool.是否按物品模式)
        {
            if (ItemPoolDatabase.HasPicked(playerName, pool.池名称, selectedItem.物品ID))
            {
                args.Player.SendErrorMessage("你已经领取过该物品！");
                return;
            }
        }
        else if (pool.最大领取次数 > 0)
        {
            int count = ItemPoolDatabase.GetPickCount(playerName, pool.池名称);
            if (count >= pool.最大领取次数)
            {
                args.Player.SendErrorMessage("你已用完该物品池的领取次数！");
                return;
            }
        }

        if (!args.Player.InventorySlotAvailable)
        {
            args.Player.SendErrorMessage("背包已满，请清理后再领取！");
            return;
        }

        args.Player.GiveItem(selectedItem.物品ID, selectedItem.数量, selectedItem.前缀);

        int recordItemId = pool.是否按物品模式 ? selectedItem.物品ID : 0;
        ItemPoolDatabase.RecordPick(playerName, pool.池名称, recordItemId);

        string prefixStr = selectedItem.前缀 > 0 ? $" (前缀:{selectedItem.前缀})" : "";
        string tag = selectedItem.数量 > 1
            ? $"[i/s{selectedItem.数量}:{selectedItem.物品ID}]"
            : $"[i:{selectedItem.物品ID}]";
        args.Player.SendSuccessMessage($"成功领取 {tag}{prefixStr}！");
    }

    private static void XzOn(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count != 1)
        {
            args.Player.SendErrorMessage("用法: /xz on <池名或编号>");
            return;
        }

        var pool = FindPoolByInput(args.Player, subArgs[0]);
        if (pool == null)
        {
            args.Player.SendErrorMessage("指定物品池不存在！");
            return;
        }

        pool.运行时启用 = true;
        args.Player.SendSuccessMessage($"物品池 [{pool.池名称}] 已开启！");
    }

    private static void XzOff(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count != 1)
        {
            args.Player.SendErrorMessage("用法: /xz off <池名或编号>");
            return;
        }

        var pool = FindPoolByInput(args.Player, subArgs[0]);
        if (pool == null)
        {
            args.Player.SendErrorMessage("指定物品池不存在！");
            return;
        }

        pool.运行时启用 = false;
        args.Player.SendSuccessMessage($"物品池 [{pool.池名称}] 已关闭！");
    }

    private static void XzReload(CommandArgs args)
    {
        ItemPoolConfig.Read();
        args.Player.SendSuccessMessage("配置文件已重新加载！");
    }

    private static void XzReset(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count < 1 || subArgs.Count > 2)
        {
            args.Player.SendErrorMessage("用法: /xz reset <玩家名> [池名或编号]");
            return;
        }

        string targetPlayer = subArgs[0];
        string? poolName = null;
        if (subArgs.Count == 2)
        {
            poolName = ResolvePoolName(args.Player, subArgs[1]);
            if (poolName == null)
            {
                args.Player.SendErrorMessage("指定物品池不存在！");
                return;
            }
        }

        int affected = ItemPoolDatabase.ResetPlayer(targetPlayer, poolName);

        if (poolName != null)
            args.Player.SendSuccessMessage($"已重置玩家 [{targetPlayer}] 在物品池 [{poolName}] 的领取记录！(影响 {affected} 条)");
        else
            args.Player.SendSuccessMessage($"已重置玩家 [{targetPlayer}] 的所有领取记录！(影响 {affected} 条)");
    }

    private static void XzResetAll(CommandArgs args, List<string> subArgs)
    {
        if (subArgs.Count > 1)
        {
            args.Player.SendErrorMessage("用法: /xz resetall [池名或编号]");
            return;
        }

        string? poolName = null;
        if (subArgs.Count == 1)
        {
            poolName = ResolvePoolName(args.Player, subArgs[0]);
            if (poolName == null)
            {
                args.Player.SendErrorMessage("指定物品池不存在！");
                return;
            }
        }

        int affected = ItemPoolDatabase.ResetAll(poolName);

        if (poolName != null)
            args.Player.SendSuccessMessage($"已重置物品池 [{poolName}] 的所有玩家领取记录！(影响 {affected} 条)");
        else
            args.Player.SendSuccessMessage($"已重置所有玩家的全部领取记录！(影响 {affected} 条)");
    }

    /// <summary>渐变色发送多行消息</summary>
    private static void SendGradient(TSPlayer player, List<string> lines, Color startColor, Color endColor)
    {
        int count = lines.Count;
        for (int i = 0; i < count; i++)
        {
            float t = count > 1 ? (float)i / (count - 1) : 0f;
            var color = new Color(
                (int)(startColor.R + (endColor.R - startColor.R) * t),
                (int)(startColor.G + (endColor.G - startColor.G) * t),
                (int)(startColor.B + (endColor.B - startColor.B) * t));
            player.SendMessage(lines[i], color);
        }
    }
}
