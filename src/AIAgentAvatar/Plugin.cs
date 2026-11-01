extern alias TrAlias;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using static FakePlayer.Utils;

namespace FakePlayer;

[ApiVersion(2, 1)]
public class Plugin(Main game) : TerrariaPlugin(game)
{
    #region 插件信息
    public static string PluginName => "假人插件";
    public override string Name => PluginName;
    public override string Author => "少司命、羽学（AIAgent 分身改造：星梦）";
    public override Version Version => new(1, 1, 0);
    public override string Description => "在你的服务器中放置假人并实现跟随战斗与工作";
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        LoadConfig();
        GeneralHooks.ReloadEvent += ReloadConfig;
        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
        ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
        ServerApi.Hooks.ProjectileAIUpdate.Register(this, AutoAttack.UpdateProj);
        Commands.ChatCommands.Add(new Command(MyCmd.prem, MyCmd.MainCmd, MyCmd.cmd, "f"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= ReloadConfig;
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
            ServerApi.Hooks.ProjectileAIUpdate.Deregister(this, AutoAttack.UpdateProj);
            Commands.ChatCommands.RemoveAll(static x => x.CommandDelegate == MyCmd.MainCmd);

            // 1. 断开所有假人连接并清空数组
            for (int i = 0; i < Fakes.Length; i++)
            {
                if (Fakes[i] != null)
                {
                    Fakes[i]?.Close();
                    Fakes[i] = null;
                }
            }

        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new();
    private static void ReloadConfig(ReloadEventArgs args)
    {
        LoadConfig();
        args.Player.SendMessage($"[{PluginName}]重新加载配置完毕。", color);
    }
    private static void LoadConfig()
    {
        try
        {
            RoleMgr.Init();
            AutoAttack.Init();     // 内部已包含：目录创建、默认配置生成、名称补全、绑定加载
            AutoAttack.Reload();   // 清空缓存，重新加载绑定（不重复创建文件）
            RoleMgr.SetDefaultRoles();
            RoleMgr.SaveAll();
            DummyInfo.Init();
            DummyInfo.SetDefault();
            DummyInfo.SaveAll();
            Config = Configuration.Read();
            Config.Write();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 配置文件加载失败：\n{ex.Message}");
        }
    }
    #endregion

    #region 加入服务器事件（只对假人自动注册，优先使用文件自带密码）
    private void OnServerJoin(JoinEventArgs args)
    {
        // 没开启自动注册 不处理
        if (!Config.AutoRegister) return;

        var plr = TShock.Players[args.Who];
        if (plr is null) return;

        // 只对名字以"假人"开头的玩家进行自动注册
        if (!plr.Name.StartsWith(Config.Names)) return;

        // 加载所有假人配置，查找同名假人
        var all = DummyInfo.LoadAll();
        var info = all.FirstOrDefault(f => f.Name == plr.Name);

        // 检查是否已存在账号
        var user = TShock.UserAccounts.GetUserAccountByName(plr.Name);
        if (user is not null)
        {
            var team = info?.Team ?? Config.DefTeam;
            plr.SetTeam(team);
            return;
        }

        // 密码优先级：文件中的密码 > 默认密码 "123456"
        string password = info?.Password ?? Config.DefPass;
        var group = TShock.Config.Settings.DefaultRegistrationGroupName;
        var newUser = new UserAccount(plr.Name, password, plr.UUID, group,
                                      DateTime.UtcNow.ToString("s"),
                                      DateTime.UtcNow.ToString("s"), "");
        try
        {
            newUser.CreateBCryptHash(password);
            TShock.UserAccounts.AddUserAccount(newUser);
            TShock.Log.ConsoleInfo($"{plr.Name} 注册密码: {password}");
            var team = info?.Team ?? Config.DefTeam;
            plr.SetTeam(team);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] {plr.Name} 自动注册失败: {ex.Message}");
        }
    }
    #endregion

    #region 玩家离开服务器清理数据方法
    internal static void OnLeave(LeaveEventArgs args)
    {
        // 清理弹幕命中数据
        AutoAttack.MyProj.RemoveAll(p => p != null && p.Owner == args.Who);

        if (Fakes != null && Fakes.Length != 0)
        {
            for (int i = 0; i < Fakes.Length; i++)
            {
                var f = Fakes[i];
                if (f == null) continue;

                if (f.PlayerSlot == args.Who)
                {
                    f.Close();
                    Fakes[i] = null;
                }

                if (f.Follow?.Index == args.Who)
                {
                    f.Follow = null;
                    f.TSPlayer.TeleportToWorldSpawn();
                }
            }
        }
    }
    #endregion

    #region NPC生成、死亡事件
    internal static List<NPC> ActiveNPCs = new();
    private void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        var npc = Main.npc[args.NpcId];
        if (npc == null) return;
        if (npc.friendly || npc.townNPC || npc.catchItem > 0) return;
        if (npc.type == NPCID.TargetDummy || npc.SpawnedFromStatue) return;

        AddActiveNPC(npc);
    }

    private void OnNpcKilled(NpcKilledEventArgs args)
    {
        if (ActiveNPCs.Contains(args.npc) && args.npc.boss)
        {
            // 1. 获取所有角色模板
            var allRoles = new List<(string name, FakeRole role)>();
            foreach (var name in RoleMgr.ListNames())
            {
                var role = RoleMgr.Load(name);
                if (role != null)
                    allRoles.Add((name, role));
            }

            // 2. 筛选进度条件满足的模板，并按 Tier 降序排序
            var matched = allRoles.Where(x => Utils.CheckConds(x.role.Limit)).
                          OrderByDescending(x => x.role.Tier).ToList();

            if (matched.Count == 0) return;

            // 3. 取第一个（Tier 最大的）
            var sel = matched.First();
            var roleName = sel.name;
            var data = sel.role;

            // 4. 应用到所有活跃假人
            int apCout = 0;
            for (int i = 0; i < Fakes.Length; i++)
            {
                var dp = Fakes[i];
                if (dp != null && dp.Active && dp.IsPlaying &&
                   (data.Tier > dp.RoleTier))
                {
                    MyCmd.ApplyRole(dp, data.Items);
                    dp.RoleTier = data.Tier;
                    apCout++;
                }
            }

            if (apCout > 0)
            {
                var text = $"已为 {apCout} 个假人加载角色「{roleName}」";
                TSPlayer.All.SendMessage(Utils.TextGrad(text), Utils.color);
            }
        }

        ActiveNPCs.RemoveAll(n => n == args.npc);
    }
    #endregion

    #region 游戏更新(行为触发器)
    public static long Tick = 0;

    /// <summary>希望存在的分身名字；为空表示不自动重连。</summary>
    internal static string? DesiredAvatar;

    /// <summary>下次允许重连的 Tick。</summary>
    internal static long NextReconnectTick;

    /// <summary>当前重连退避（帧），成功或手动创建后重置。</summary>
    internal static int ReconnectBackoff = 600;

    /// <summary>正在连接中的保护时间：防止同一帧重复创建。</summary>
    internal static long ConnectingUntil;
    internal static readonly List<Point> BadSpots = new();
    internal static readonly DummyPlayer?[] Fakes = new DummyPlayer[Main.maxPlayers];
    private void OnGameUpdate(EventArgs args)
    {
        Tick++;

        // 服务器起来 10 秒后自动创建一个分身（只创建一次）
        if (Config.AutoSpawn && Tick == 600 && FakeApi.Get() is null && !Fakes.Any(f => f is not null))
        {
            FakeApi.Spawn();
        }

        // 清理已经断开的残留槽位，否则重连会被"半死"的记录挡住
        for (var i = 0; i < Fakes.Length; i++)
        {
            var dead = Fakes[i];
            if (dead is null || dead.Active) continue;
            dead.Close();
            Fakes[i] = null;
        }

        // 分身真正上线后，解除连接保护
        if (FakeApi.Get() is not null)
        {
            ConnectingUntil = 0;
        }

        // 掉线自动重连（只重连"曾经创建过/想要"的那个分身）
        if (Config.AutoReconnect &&
            DesiredAvatar is not null &&
            Tick >= NextReconnectTick &&
            Tick >= ConnectingUntil &&
            FakeApi.Get() is null &&
            !Fakes.Any(f => f is not null))
        {
            ConnectingUntil = Tick + 600;
            var dp = MyCmd.CreateAvatar(DesiredAvatar);
            if (dp is null)
            {
                NextReconnectTick = Tick + ReconnectBackoff;
                ReconnectBackoff = Math.Min(ReconnectBackoff * 2, 3600);
                TShock.Log.ConsoleError($"[{PluginName}] 分身重连失败，{ReconnectBackoff / 60} 秒后重试");
            }
            else
            {
                TShock.Log.ConsoleInfo($"[{PluginName}] 分身已重新连接：{DesiredAvatar}");
                NextReconnectTick = 0;
                ReconnectBackoff = 600;
            }
        }

        // 每秒检测一次是否卡住（有目标却不位移）
        if (Tick % 60 == 0)
        {
            for (var i = 0; i < Fakes.Length; i++)
            {
                var dp = Fakes[i];
                if (dp is null || !dp.Active || !dp.IsPlaying) continue;

                var plr = dp.TSPlayer?.TPlayer;
                if (plr is null) continue;

                var hasGoal = dp.HoldPos || dp.Follow is not null || dp.TargetNpc >= 0;
                var moved = plr.position.Distance(dp.StuckCheckPos);
                dp.StuckCheckPos = plr.position;

                if (!hasGoal || moved > 8f)
                {
                    dp.StuckSeconds = 0;
                    dp.Stuck = false;
                    continue;
                }

                // 已经在目标点附近就不算卡
                if (dp.HoldPos && dp.RoamPos != Microsoft.Xna.Framework.Vector2.Zero &&
                    plr.position.Distance(dp.RoamPos) < 32f)
                {
                    dp.StuckSeconds = 0;
                    dp.Stuck = false;
                    continue;
                }

                dp.StuckSeconds++;
                dp.Stuck = dp.StuckSeconds >= 5;
            }
        }

        // 每10秒清理弹幕记录
        if (Tick % 600 == 0)
        {
            ClearProj();
        }

        for (int i = 0; i < Fakes.Length; i++)
        {
            var dp = Fakes[i];
            if (dp == null || !dp.Active || !dp.IsPlaying || dp.TSPlayer == null || !dp.TSPlayer.Active) continue;

            // 递减武器冷却
            if (dp.UseTime > 0) dp.UseTime--;

            // 躲避冷却
            if (dp.DodgeCD > 0) dp.DodgeCD--;

            // 防抖：控制更新频率
            if (Tick - dp.LastAction < Config.UpdateTime) continue;

            DummyWork.RepelFake(dp);   // 排斥其他假人
            DummyWork.RepelNpc(dp);    // 排斥npc
            DummyWork.DoWork(dp);      // 开始工作

            dp.LastAction = Tick;
        }
    }
    #endregion

    #region 限制静态列表大小
    public static void AddBadSpot(Point p)
    {
        // 避免重复添加
        if (BadSpots.Contains(p)) return;
        BadSpots.Add(p);

        // 移除最早添加的坏点
        while (BadSpots.Count > 50)
            BadSpots.RemoveAt(0);
    }

    public static void AddActiveNPC(NPC npc)
    {
        // 避免重复添加
        if (ActiveNPCs.Contains(npc)) return;
        ActiveNPCs.Add(npc);

        // 超过 50 个时，移除最早添加的npc
        while (ActiveNPCs.Count > 50)
            ActiveNPCs.RemoveAt(0);
    }

    public static void ClearProj()
    {
        AutoAttack.MyProj.RemoveAll(rec =>
        {
            if (rec == null) return true;
            // 获取弹幕
            if (rec.Idx < 0 || rec.Idx >= Main.maxProjectiles) return true;
            var proj = Main.projectile[rec.Idx];

            // 弹幕不存在、不活跃、或所有者不匹配 -> 清理
            if (proj == null || !proj.active || proj.owner != rec.Owner) return true;
            // 假人已断开也不保留
            if (rec.fake == null || !rec.fake.Active) return true;

            return false;
        });
    }
    #endregion
}