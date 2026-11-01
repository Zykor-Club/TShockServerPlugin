extern alias TrAlias;

using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Linq;
using Terraria;
using Terraria.ID;
using TrAlias::TrProtocol.NetPackets;
using TShockAPI;
using TShockAPI.DB;
using static FakePlayer.DummyWork;
using static FakePlayer.Plugin;
using static FakePlayer.Utils;

namespace FakePlayer;

internal class MyCmd
{
    #region 指令参数
    public static string cmd => "fake";
    public static string prem => $"{cmd}.use";
    public static bool IsAdmin(TSPlayer plr) => plr.HasPermission($"{cmd}.admin");

    public static bool InGame(TSPlayer plr)
    {
        if (!plr.RealPlayer)
        {
            plr.SendMessage($"请进入游戏后再使用{PluginName}的{cmd}指令", 240, 250, 150);
            return false;
        }
        return true;
    }
    #endregion

    #region 菜单指令
    private static void Help(TSPlayer plr)
    {
        var sb = new StringBuilder();
        if (!plr.RealPlayer)
            sb.AppendLine($"\n《{PluginName}》");
        else
            sb.AppendLine($"\n{ItemIcon(ItemID.NebulaPickup3)}[c/AD89D5:假][c/D68ACA:人][c/DF909A:插][c/E5A894:件]{ItemIcon(ItemID.NebulaPickup2)} {ItemIcon(ItemID.FragmentVortex)}[c/F2F2C7:开发] [c/BFDFEA:by] [c/00FFFF:少司命、羽学] {ItemIcon(ItemID.FragmentStardust)}");

        sb.AppendLine($"/{cmd} add [索引/all] --创建重连(re)");
        sb.AppendLine($"/{cmd} del [索引/all] --移除假人(rm)");
        sb.AppendLine($"/{cmd} me [索引/all] --跟随自己(fm)");
        sb.AppendLine($"/{cmd} list --假人列表(ls)");
        sb.AppendLine($"/{cmd} item [索引] --查询背包(i)");
        sb.AppendLine($"/{cmd} set [索引] --修改背包(s)");
        sb.AppendLine($"/{cmd} save --将自己保存角色(sv)");
        sb.AppendLine($"/{cmd} load --将角色设给假人(ap)");
        sb.AppendLine($"/{cmd} role --列出所有角色(rl)");
        if (IsAdmin(plr))
            sb.AppendLine($"/{cmd} reset --重置所有假人数据和角色模板");

        SendMess(plr, sb.ToString());
    }
    #endregion

    #region 主指令
    internal static void MainCmd(CommandArgs args)
    {
        var plr = args.Player;
        if (args.Parameters.Count == 0)
        {
            Help(plr);
            return;
        }

        switch (args.Parameters[0].ToLower())
        {
            case "re": case "add": AddFake(args); break;
            case "rm": case "del": Remove(args); break;
            case "ls": case "list": ListFake(args); break;
            case "fm": case "me": Follow(args); break;
            case "i": case "item": ShowInv(args); break;
            case "s": case "set": SetItem(args); break;
            case "sv": case "save": SaveRole(args); break;
            case "ap": case "load": LoadRole(args); break;
            case "rl": case "role": ListRoles(args); break;
            case "rs": case "reset": ResetData(args); break;
            default: Help(plr); break;
        }
    }
    #endregion

    #region 创建与重连
    private static void AddFake(CommandArgs args)
    {
        var plr = args.Player;

        // 无参数：只创建一个假人
        if (args.Parameters.Count == 1)
        {
            CreateOne();
            SendMess(plr, "已创建一个新假人");

            SendMess(plr, "\n《如果发现假人不动》");
            SendMess(plr, "1.修改假人密码:/user password “名字” 123456");
            SendMess(plr, "2.确认文件内的密码一致，以确保其正常登录：");
            SendMess(plr, "登录密码位置：tshock/假人插件/假人/**.json");
            return;
        }

        // 两个参数：检查是否为 "all"
        if (args.Parameters.Count == 2 && args.Parameters[1].ToLower() == "all")
        {
            // 1. 关闭所有现有假人
            for (int i = 0; i < Fakes.Length; i++)
            {
                Fakes[i]?.Close();
                Fakes[i] = null;
            }

            // 2. 获取所有假人配置
            var fakes = DummyInfo.LoadAll();
            int success = 0;
            foreach (var info in fakes)
            {
                // 创建前检查同名玩家是否已在线，若在线则跳过
                if (TShock.Players.Any(p => p?.Name == info.Name))
                {
                    SendMess(plr, $"{info.Name} 已在线，跳过创建");
                    continue; // 或记录失败次数
                }

                AddDummy(info);
                success++;
            }
            SendMess(plr, $"成功创建 {success} / {fakes.Count} 个假人");
            return;
        }

        // 指定索引重连
        if (args.Parameters.Count == 2 && int.TryParse(args.Parameters[1], out var index))
        {
            if (index < 0 || index >= Fakes.Length)
            {
                SendMess(plr, "假人索引无效");
                return;
            }

            var fake = Fakes[index];
            if (fake == null || fake.Active)
            {
                SendMess(plr, "假人不存在或已经激活");
                return;
            }

            fake.GameLoop("127.0.0.1", Netplay.ListenPort, TShock.Config.Settings.ServerPassword);
            SendMess(plr, $"已重连假人 #{index}");
            return;
        }

        // 第二个参数为自定义名字（仅登录已存在的配置文件）
        if (args.Parameters.Count == 2)
        {
            string name = args.Parameters[1];

            // 检查名字是否已被占用（其他假人或真实玩家）
            if (TShock.Players.Any(p => p != null && p.Name == name) ||
                Fakes.Any(f => f?.TSPlayer?.Name == name && f.Active))
            {
                SendMess(plr, $"{name} 已在线");
                return;
            }

            // 查找是否已有该名字的假人配置文件
            var info = DummyInfo.Load(name);
            if (info == null)
            {
                SendMess(plr, $"未找到名为 [{name}] 的假人配置文件");
                return;
            }

            AddDummy(info);
            SendMess(plr, $"{name} 正在连接...");
            return;
        }

        SendMess(plr, $"创建单个: /{cmd} add \n" +
                      $"创建全部: /{cmd} add all \n" +
                      $"指定重连: /{cmd} re [索引] \n" +
                      $"指定登录: /{cmd} add [名字]");
    }
    #endregion

    #region 列出假人
    private static void ListFake(CommandArgs args)
    {
        if (Fakes.Length == 0)
        {
            SendMess(args.Player, $"没有找到活跃的假人");
            SendMess(args.Player, $"创建单个: /{cmd} add \n" +
                                  $"创建全部: /{cmd} add all \n" +
                                  $"指定重连: /{cmd} re [索引] \n" +
                                  $"指定登录: /{cmd} add [名字]");
            return;
        }

        for (var i = 0; i < Fakes.Length; i++)
        {
            var dp = Fakes[i];
            if (dp?.TSPlayer != null && dp.TSPlayer.Active)
                SendMess(args.Player, $"[{i}].{dp.TSPlayer.Name} 索引:{dp.PlayerSlot} 状态:{dp.GetStatus()}");
        }
    }
    #endregion

    #region 移除假人
    private static void Remove(CommandArgs args)
    {
        DesiredAvatar = null;

        if (args.Parameters.Count < 2)
        {
            SendMess(args.Player, $"用法: /{cmd} del 索引\n" +
                                  $"或者: /{cmd} del all");
            return;
        }
        string param = args.Parameters[1].ToLower();

        if (param == "all")
        {
            // 关闭所有现有假人并清空数组
            var count = 0;
            for (int i = 0; i < Fakes.Length; i++)
            {
                if (Fakes[i] != null)
                {
                    Fakes[i]?.Close();   // 关闭假人连接
                    Fakes[i] = null;    // 清除引用
                    count++;
                }
            }
            SendMess(args.Player, $"已移除{count}个假人");
            return;
        }

        if (!int.TryParse(param, out var index))
        {
            SendMess(args.Player, "请输入正确的序号或 'all'!");
            return;
        }
        if (index < 0 || index >= Fakes.Length)
        {
            SendMess(args.Player, "假人不存在!");
            return;
        }

        var fake = Fakes[index];
        if (fake == null || !fake.Active || !fake.IsPlaying)
        {
            SendMess(args.Player, "假人未激活或未进入游戏!");
            return;
        }

        fake.Close();
        Fakes[index] = null;
        SendMess(args.Player, "假人移除成功!");
    }
    #endregion

    #region 跟随指令
    private static void Follow(CommandArgs args)
    {
        var plr = args.Player;
        if (args.Parameters.Count < 2)
        {
            SendMess(plr, $"用法: /{cmd} me [索引|all]");
            return;
        }

        string param = args.Parameters[1].ToLower();

        // 处理 all：切换所有活跃假人的跟随状态（跟随自己 ↔ 停止）
        if (param == "all")
        {
            List<string> names = new();
            for (int i = 0; i < Fakes.Length; i++)
            {
                var d = Fakes[i];
                if (d == null || !d.Active || !d.IsPlaying) continue;

                if (d.Follow == plr)
                {
                    d.Follow = null;
                }
                else
                {
                    d.Goal = "none";
                    d.PatrolPoints.Clear();
                    d.PatrolIndex = 0;
                    d.HoldPos = false;
                    d.TargetNpc = -1;
                    d.Follow = plr;
                }
                names.Add(d.TSPlayer.Name);
            }

            SendMess(plr, names.Count <= 0 ? "没有可操作的假人" :
                    $"已切换 {names.Count} 个跟随状态:\n{string.Join(",", names)}");
            return;
        }

        // 处理索引：切换指定假人的跟随状态
        if (!int.TryParse(param, out int idx) || idx < 0 || idx >= Fakes.Length || Fakes[idx] == null)
        {
            SendMess(plr, "无效的假人索引");
            return;
        }

        var dummy = Fakes[idx];
        if (dummy == null || !dummy.Active || !dummy.IsPlaying)
        {
            SendMess(plr, "假人未激活或未进入游戏");
            return;
        }

        if (dummy.Follow == plr)
        {
            dummy.Follow = null;
        }
        else
        {
            dummy.Goal = "none";
            dummy.PatrolPoints.Clear();
            dummy.PatrolIndex = 0;
            dummy.HoldPos = false;
            dummy.TargetNpc = -1;
            dummy.Follow = plr;
        }
        SendMess(plr, $"假人 {dummy.TSPlayer.Name} 现在{(dummy.Follow == plr ? "跟随你" : "停止跟随")}");
    }
    #endregion

    #region 查询假人背包（只显示基础槽位）
    private static void ShowInv(CommandArgs args)
    {
        var plr = args.Player;
        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out var idx))
        {
            SendMess(plr, $"用法: /{cmd} i [索引]");
            return;
        }

        if (idx < 0 || idx >= Fakes.Length)
        {
            SendMess(plr, $"假人 #{idx} 不存在");
            return;
        }

        var fake = Fakes[idx];
        if (fake == null || !fake.Active || !fake.IsPlaying)
        {
            SendMess(plr, "假人未进入游戏");
            return;
        }

        var p = fake.TSPlayer.TPlayer;
        var sb = new StringBuilder();
        sb.AppendLine($"{p.name} 的全部装备:");

        void AddRange(string title, int startSlot, int count, int maxPerLine = 8)
        {
            // 先收集所有非空物品的显示字符串
            var items = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int slot = startSlot + i;
                Item itm;
                try { itm = new PlayerItemSlotID.SlotReference(p, slot).Item; }
                catch { continue; }
                if (itm != null && !itm.IsAir && itm.stack > 0)
                {
                    items.Add($"{slot}.{ItemIcon(itm.type, itm.stack)}");
                }
            }

            // 没有任何物品则跳过标题
            if (items.Count == 0) return;

            // 输出标题
            sb.AppendLine($"\n[c/AD89D5:{title}:]");

            // 按行输出物品
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(items[i] + " ");
                if ((i + 1) % maxPerLine == 0) sb.AppendLine();
            }
            if (items.Count % maxPerLine != 0) sb.AppendLine();
        }

        AddRange("主要背包", PlayerItemSlotID.Inventory0, 58, 10);
        AddRange("盔甲饰品", PlayerItemSlotID.Armor0, 20, 8);
        AddRange("工具栏位", PlayerItemSlotID.Misc0, 5, 5);

        var sel = p.inventory[p.selectedItem];
        if (!sel.IsAir)
            sb.Append($"\n当前手持: {ItemIcon(sel.type, sel.stack)}");

        SendMess(plr, sb.ToString());
    }
    #endregion

    #region 创建单个假人
    public static DummyPlayer? CreateOne()
    {
        var all = DummyInfo.LoadAll();
        var names = new HashSet<string>();
        foreach (var f in Fakes)
        {
            if (f?.TSPlayer != null && f.Active)
                names.Add(f.TSPlayer.Name);
        }

        FInfo? newInfo = null;
        foreach (var info in all)
        {
            if (!names.Contains(info.Name))
            {
                newInfo = info;
                break;
            }
        }

        if (newInfo == null)
        {
            newInfo = new FInfo();
            newInfo.SetRandom(all.Count + 1);
            DummyInfo.Save(newInfo);
        }

        return AddDummy(newInfo);
    }

    internal static DummyPlayer? AddDummy(FInfo f)
    {
        var activeCount = Fakes.Count(x => x is not null && x.Active);
        if (activeCount >= Math.Max(1, Config.MaxFakes))
        {
            TShock.Log.ConsoleError($"假人插件: 已达到上限 {Config.MaxFakes} 个，拒绝创建");
            return null;
        }

        int slot = FindEmptySlot();
        if (slot == -1)
        {
            TShock.Log.ConsoleError("假人插件: 服务器已满人，无法创建新假人");
            return null;
        }

        var syncPlayer = new SyncPlayer
        {
            Hair = f.Hair,
            HairColor = f.HairColor,
            EyeColor = f.EyeColor,
            ShirtColor = f.ShirtColor,
            ShoeColor = f.ShoeColor,
            SkinColor = f.SkinColor,
            HairDye = f.HairDye,
            Name = f.Name,
            SkinVariant = f.SkinVariant,
            UnderShirtColor = f.UnderShirtColor,
            HideMisc = f.HideMisc,
            VoiceVariant = f.VoiceVariant,
            VoicePitchOffset = f.VoicePitchOffset,
        };

        var dp = new DummyPlayer(syncPlayer, f.UUID, f.Password, f.Team)
        {
            SpeakEnabled = Config.Speak,
        };

        // 必须先注册处理器，再建立连接；否则 LoadPlayer 可能早于注册到达
        dp.On<LoadPlayer>(slotInfo =>
        {
            Fakes[slotInfo.PlayerSlot] = dp;
        });

        dp.On<StartPlaying>(_ =>
        {
            var defaultRole = RoleMgr.Load("默认");
            if (defaultRole != null)
                ApplyRole(dp, defaultRole.Items);
            RepelFake(dp);
        });

        ConnectingUntil = Tick + 600;

        try
        {
            dp.GameLoop("127.0.0.1", Netplay.ListenPort, TShock.Config.Settings.ServerPassword);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"假人插件: 连接失败 {f.Name} - {ex.Message}");
            dp.Close();
            return null;
        }

        return dp;
    }

    /// <summary>创建/登录指定名字的分身（不存在则按名字建模板）。</summary>
    public static DummyPlayer? CreateAvatar(string name)
    {
        var info = DummyInfo.Load(name);
        if (info is null)
        {
            info = new FInfo { Name = name, Team = 4 };
            info.SetRandom(0, name);
            DummyInfo.Save(info);
        }

        return AddDummy(info);
    }

    #endregion

    #region 设置假人指定槽位
    private static void SetItem(CommandArgs args)
    {
        var plr = args.Player;
        if (!InGame(plr)) return;
        if (args.Parameters.Count < 2)
        {
            SendMess(plr, $"用法: /{cmd} s [索引] [槽位] \n或者: /{cmd} s all [槽位]");
            ItemInfo(plr.TPlayer, plr.SelectedItem);
            return;
        }

        string firstParam = args.Parameters[1].ToLower();
        bool setAll = false;
        int idx = -1;
        int slot = -1;

        // 情况1：所有假人
        if (firstParam == "all")
        {
            setAll = true;
            if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out slot))
            {
                SendMess(plr, $"用法: /{cmd} s all [槽位]");
                return;
            }
        }
        else
        {
            // 情况2：指定索引
            if (!int.TryParse(args.Parameters[1], out idx))
            {
                SendMess(plr, "无效的假人索引");
                return;
            }
            if (idx < 0 || idx >= Fakes.Length || Fakes[idx] == null)
            {
                SendMess(plr, $"假人 #{idx} 不存在");
                return;
            }
            if (args.Parameters.Count >= 3 && !int.TryParse(args.Parameters[2], out slot))
            {
                SendMess(plr, "无效的槽位值");
                return;
            }
        }

        // 槽位默认值：如果没指定，使用玩家当前选中槽位
        if (slot == -1)
            slot = PlayerItemSlotID.Inventory0 + plr.TPlayer.selectedItem;

        int maxBaseSlot = PlayerItemSlotID.MiscDye0 + 5; // 98
        if (slot < 0 || slot >= maxBaseSlot)
        {
            SendMess(plr, $"槽位 {slot} 不可同步（仅支持主背包/盔甲/染料/饰品/工具栏）");
            return;
        }

        var item = plr.SelectedItem;
        if (item == null || item.IsAir)
        {
            SendMess(plr, "你手上没有物品");
            return;
        }

        if (setAll)
        {
            int cnt = 0;
            for (int i = 0; i < Fakes.Length; i++)
            {
                var fake = Fakes[i];
                if (fake != null && fake.Active && fake.IsPlaying)
                {
                    SetSlotItem(fake, slot, item);
                    cnt++;
                }
            }
            SendMess(plr, $"已将物品 {ItemIcon(item.type, item.stack)} 设置到 {cnt} 个假人槽位 {slot}");
            return;
        }
        else
        {
            var dp = Fakes[idx];
            if (dp == null || !dp.Active || !dp.IsPlaying)
            {
                SendMess(plr, "假人未激活或未进入游戏");
                return;
            }
            SetSlotItem(dp, slot, item);
            SendMess(plr, $"已将 {dp.TSPlayer.Name} 的槽位 [c/FA6866:{slot}] 设置为 {ItemIcon(item.type, item.stack)}");
        }
    }
    #endregion

    #region 保存角色模板（只保存基础槽位）
    private static void SaveRole(CommandArgs args)
    {
        var plr = args.Player;
        if (!InGame(plr)) return;
        if (args.Parameters.Count < 2)
        {
            SendMess(plr, $"用法: /{cmd} sv [角色名]");
            SendMess(plr, $"用法: /{cmd} sv [角色名] [阶级数]");
            return;
        }
        string name = args.Parameters[1];
        var data = new FakeRole();

        // 解析阶级数参数
        int tier = -1; // 默认值
        if (args.Parameters.Count >= 3 && int.TryParse(args.Parameters[2], out int Tier))
        {
            tier = Tier;
            data.Tier = tier;
        }

        var t = plr.TPlayer;

        void CopyToRole(Item[] src, int startSlotId)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int slot = startSlotId + i;
                if (slot < data.Items.Length)
                {
                    var itm = src[i];
                    data.Items[slot] = new NetItem(itm.type, itm.stack, itm.prefix, itm.favorited);
                }
            }
        }

        // 只保存基础槽位（0~97）
        CopyToRole(t.inventory, PlayerItemSlotID.Inventory0);
        CopyToRole(t.armor, PlayerItemSlotID.Armor0);
        CopyToRole(t.dye, PlayerItemSlotID.Dye0);
        CopyToRole(t.miscEquips, PlayerItemSlotID.Misc0);
        CopyToRole(t.miscDyes, PlayerItemSlotID.MiscDye0);

        // 垃圾槽单独处理
        if (PlayerItemSlotID.TrashItem < data.Items.Length)
            data.Items[PlayerItemSlotID.TrashItem] = new NetItem(t.trashItem.type, t.trashItem.stack, t.trashItem.prefix, t.trashItem.favorited);

        // 其他槽位（虚空袋、时装等）不保存，避免加载时越界
        if (RoleMgr.Save(name, data))
            SendMess(plr, $"角色 [{name}] 已保存！");
        else
            SendMess(plr, "保存失败，请检查日志。");
    }
    #endregion

    #region 加载角色到假人
    private static void LoadRole(CommandArgs args)
    {
        var plr = args.Player;
        if (!InGame(plr)) return;
        if (args.Parameters.Count < 2)
        {
            SendMess(plr, $"设置指定假人: /{cmd} ap 角色名 索引");
            SendMess(plr, $"设置所有假人: /{cmd} ap 角色名 all");
            SendMess(plr, $"将自己设指定: /{cmd} ap me 索引");
            SendMess(plr, $"将自己设所有: /{cmd} ap me all");
            return;
        }
        string roleName = args.Parameters[1];
        string idxStr = args.Parameters.Count >= 3 ? args.Parameters[2] : "";

        FakeRole? data;
        if (roleName.ToLower() == "me")
        {
            data = new FakeRole();
            var t = plr.TPlayer;
            void CopyToRole(Item[] src, int startSlotId)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    int slot = startSlotId + i;
                    if (slot < data.Items.Length)
                    {
                        var itm = src[i];
                        data.Items[slot] = new NetItem(itm.type, itm.stack, itm.prefix, itm.favorited);
                    }
                }
            }
            CopyToRole(t.inventory, PlayerItemSlotID.Inventory0);
            CopyToRole(t.armor, PlayerItemSlotID.Armor0);
            CopyToRole(t.dye, PlayerItemSlotID.Dye0);
            CopyToRole(t.miscEquips, PlayerItemSlotID.Misc0);
            CopyToRole(t.miscDyes, PlayerItemSlotID.MiscDye0);
            if (PlayerItemSlotID.TrashItem < data.Items.Length)
                data.Items[PlayerItemSlotID.TrashItem] = new NetItem(t.trashItem.type, t.trashItem.stack, t.trashItem.prefix, t.trashItem.favorited);
        }
        else
        {
            data = RoleMgr.Load(roleName);
            if (data == null)
            {
                SendMess(plr, $"角色 [{roleName}] 不存在");
                return;
            }

            // 检查进度条件
            if (!CheckConds(data.Limit, plr.TPlayer))
            {
                SendMess(plr, $"角色 [{roleName}] 的进度/环境限制未满足，无法加载！");
                return;
            }
        }

        if (string.IsNullOrEmpty(idxStr))
        {
            SendMess(plr, "请指定假人索引或 'all'");
            return;
        }

        if (idxStr.ToLower() == "all")
        {
            int cnt = 0;
            for (int i = 0; i < Fakes.Length; i++)
            {
                var fake = Fakes[i];
                if (fake != null && fake.Active && fake.IsPlaying)
                {
                    ApplyRole(fake, data.Items);
                    cnt++;
                }
            }
            SendMess(plr, $"已将角色 {roleName} 应用到 {cnt} 个假人");
        }
        else
        {
            if (!int.TryParse(idxStr, out var idx) || idx < 0 || idx >= Fakes.Length || Fakes[idx] == null)
            {
                SendMess(plr, $"假人 #{idxStr} 不存在");
                return;
            }
            var fake = Fakes[idx];
            if (fake == null || !fake.Active || !fake.IsPlaying)
            {
                SendMess(plr, "假人未激活或未进入游戏");
                return;
            }

            ApplyRole(fake, data.Items);
            SendMess(plr, $"{fake.TSPlayer.Name} 已加载角色{(roleName == "me" ? "(你的当前装备)" : $" [{roleName}]")}");
        }
    }
    #endregion

    #region 列出角色模板
    private static void ListRoles(CommandArgs args)
    {
        var plr = args.Player;
        var names = RoleMgr.ListNames();
        if (names.Count == 0)
            SendMess(plr, "暂无角色");
        else
            SendMess(plr, $"角色列表: \n{string.Join(", ", names)}");
    }
    #endregion

    #region 应用角色给假人（只同步基础槽位）
    public static void ApplyRole(DummyPlayer dp, NetItem[] items)
    {
        if (items == null) return;
        // 只同步基础槽位：主背包(0-57)、盔甲与饰品(59-77)、染料(78-87)、饰品(88-92)、工具栏染料(93-97)、垃圾槽(单独)
        int maxSlot = PlayerItemSlotID.MiscDye0 + 5; // 98
        if (maxSlot > items.Length) maxSlot = items.Length;

        for (int slot = 0; slot < maxSlot; slot++)
        {
            var itm = items[slot];
            if (!ContentSamples.ItemsByType.TryGetValue(itm.NetId, out var item) || item is null)
            {
                dp.SendPacket(new SyncEquipment
                {
                    PlayerSlot = dp.PlayerSlot,
                    ItemSlot = (byte)slot,
                    Stack = 0,
                    Prefix = 0,
                    ItemType = 0,
                });
                continue;
            }

            // 禁止空物品 与 召唤仆从武器
            if (itm.NetId <= 0 || item.sentry ||
                (item.summon && !item.noMelee && item.useStyle == 1))
            {
                dp.SendPacket(new SyncEquipment
                {
                    PlayerSlot = dp.PlayerSlot,
                    ItemSlot = (byte)slot,
                    Stack = 0,
                    Prefix = 0,
                    ItemType = 0,
                });
                continue;
            }

            dp.SendPacket(new SyncEquipment
            {
                PlayerSlot = dp.PlayerSlot,
                ItemSlot = (byte)slot,
                Stack = (short)itm.Stack,
                Prefix = (byte)itm.PrefixId,
                ItemType = (short)itm.NetId,
            });
        }

        SetNoPick(dp); // 设置负重石
    }
    #endregion

    #region 设置指定槽位物品
    public static void SetSlotItem(DummyPlayer dp, int slot, Item item)
    {
        // 排除空物品与鞭子
        if (item == null || item.IsAir || item.sentry ||
           (item.summon && !item.noMelee && item.useStyle == 1))
        {
            return;
        }

        // 只允许基础槽位
        int maxBaseSlot = PlayerItemSlotID.MiscDye0 + 5;
        if (slot < 0 || slot >= maxBaseSlot) return;

        var plr = dp.TSPlayer.TPlayer;
        if (plr != null)
        {
            try
            {
                var slotRef = new PlayerItemSlotID.SlotReference(plr, slot);
                slotRef.Item = item.Clone();
            }
            catch { /* 忽略同步错误 */ }
        }

        var pkt = new SyncEquipment
        {
            PlayerSlot = dp.PlayerSlot,
            ItemSlot = (byte)slot,
            Stack = (short)item.stack,
            Prefix = item.prefix,
            ItemType = (short)item.type
        };
        dp.SendPacket(pkt);
        SetNoPick(dp);
    }
    #endregion

    #region 重置数据
    private static void ResetData(CommandArgs args)
    {
        var plr = args.Player;
        if (!IsAdmin(plr))
        {
            SendMess(plr, "你没有权限执行此操作！");
            return;
        }

        // 1. 断开所有假人连接并清空数组
        for (int i = 0; i < Fakes.Length; i++)
        {
            var f = Fakes[i];
            if (f != null)
            {
                f.Close();
                Fakes[i] = null;
            }
        }

        // 2. 删除假人账户（用户名以“假人”开头）
        var allAcc = TShock.UserAccounts.GetUserAccounts();
        var accs = allAcc.Where(a => a.Name.StartsWith(Config.Names)).ToList();
        foreach (var acc in accs)
            TShock.UserAccounts.RemoveUserAccount(acc);

        // 3. 删除假人外貌配置文件
        string dummyDir = Path.Combine(TShock.SavePath, PluginName, "假人");
        if (Directory.Exists(dummyDir))
            foreach (var f in Directory.GetFiles(dummyDir, "*.json")) File.Delete(f);

        // 4. 删除角色模板文件
        string roleDir = Path.Combine(TShock.SavePath, PluginName, "角色模板");
        if (Directory.Exists(roleDir))
            foreach (var f in Directory.GetFiles(roleDir, "*.json")) File.Delete(f);

        // 5. 重新生成默认假人配置文件
        DummyInfo.SetDefault();

        // 6. 清理列表
        BadSpots.Clear();
        ActiveNPCs.Clear();
        AutoAttack.MyProj.Clear();

        // 7. 清理弹幕系统缓存和绑定映射
        AutoAttack.SpawnCache.Clear();
        AutoAttack.UpdCache.Clear();
        AutoAttack.BindMap.Clear();

        // 8. 重新初始化弹幕系统（重新生成默认配置并加载）
        AutoAttack.Init();   // 会重新创建默认配置（如果文件被删除）
        AutoAttack.Reload(); // 重新加载绑定和缓存

        SendMess(plr, "已重置所有假人数据和角色模板，并清理了假人账户");
    }
    #endregion

    #region 查找空槽位
    /// <summary>
    /// 从 254 向下查找第一个未激活的玩家槽位
    /// </summary>
    /// <returns>槽位索引，若没有空位则返回 -1</returns>
    private static int FindEmptySlot()
    {
        for (int i = 254; i > 0; i--)
            if (Main.player[i] == null || !Main.player[i].active)
                return i;

        return -1;
    }
    #endregion

}