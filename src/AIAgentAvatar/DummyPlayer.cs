extern alias TrAlias;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using Terraria;
using TrAlias.TrProtocol.NetPackets;
using TShockAPI;
using static FakePlayer.Plugin;
using ClientHello = TrAlias.TrProtocol.NetPackets.ClientHello;
using ClientUUID = TrAlias.TrProtocol.NetPackets.ClientUUID;
using INetPacket = TrAlias.TrProtocol.INetPacket;
using IPlayerSlot = TrAlias.TrProtocol.Models.Interfaces.IPlayerSlot;
using Kick = TrAlias.TrProtocol.NetPackets.Kick;
using LoadPlayer = TrAlias.TrProtocol.NetPackets.LoadPlayer;
using NetTextModule = TrAlias.TrProtocol.NetPackets.Modules.NetTextModule;
using NetworkText = TrAlias.Terraria.Localization.NetworkText;
using PlayerActive = TrAlias.TrProtocol.NetPackets.PlayerActive;
using PlayerHealth = TrAlias.TrProtocol.NetPackets.PlayerHealth;
using PlayerMana = TrAlias.TrProtocol.NetPackets.PlayerMana;
using Point16 = TrAlias.Terraria.DataStructures.Point16;
using ProjectileKey = TrAlias.Terraria.DataStructures.ProjectileKey;
using RequestPassword = TrAlias.TrProtocol.NetPackets.RequestPassword;
using RequestTileData = TrAlias.TrProtocol.NetPackets.RequestTileData;
using RequestWorldInfo = TrAlias.TrProtocol.NetPackets.RequestWorldInfo;
using SendPassword = TrAlias.TrProtocol.NetPackets.SendPassword;
using SmartTextMessage = TrAlias.TrProtocol.NetPackets.SmartTextMessage;
using SpawnPlayer = TrAlias.TrProtocol.NetPackets.SpawnPlayer;
using StartPlaying = TrAlias.TrProtocol.NetPackets.StartPlaying;
using StatusText = TrAlias.TrProtocol.NetPackets.StatusText;
using SyncPlayer = TrAlias.TrProtocol.NetPackets.SyncPlayer;
using TextC2S = TrAlias.TrProtocol.NetPackets.Modules.TextC2S;
using TextS2C = TrAlias.TrProtocol.NetPackets.Modules.TextS2C;
using TrColor = TrAlias.Microsoft.Xna.Framework.Color;
using TrPlayerSpawnContext = TrAlias.Terraria.PlayerSpawnContext;
using TrPoint = TrAlias.Microsoft.Xna.Framework.Point;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using WorldData = TrAlias.TrProtocol.NetPackets.WorldData;
using TrVector2 = TrAlias.Microsoft.Xna.Framework.Vector2;
using BitsByte = TrAlias.Terraria.BitsByte;

namespace FakePlayer;

/// <summary>
/// 假人玩家，模拟真实玩家的网络行为
/// </summary>
internal class DummyPlayer
{
    // 假人占用的槽位（0~255）
    public byte PlayerSlot { get; private set; }
    // 客户端版本标识，需与服务器匹配
    public string CurRelease = Config.Version;

    // 是否已进入世界
    public bool IsPlaying { get; private set; }
    // 假人是否活跃（已连接且未关闭）
    public bool Active { get; private set; }

    // TShock 假人玩家对象
    public TSPlayer TSPlayer => TShock.Players[this.PlayerSlot];
    // 跟随的目标玩家
    public TSPlayer? Follow { get; set; }

    public byte Team;                     // 假人队伍

    /// <summary>定点模式：为 true 时不再随机漫游，停在 RoamPos。</summary>
    public bool HoldPos { get; set; }

    /// <summary>指定攻击目标（NPC 槽位），-1 表示自动。</summary>
    public int TargetNpc { get; set; } = -1;

    /// <summary>是否允许说话（默认开）。</summary>
    public bool SpeakEnabled { get; set; } = true;

    private readonly object _sendGate = new();
    public long LastTP;                   // 上次超距离传送帧
    public long LastJump;                 // 上次跳跃
    public long LastAction;               // 行动间隔帧

    public Vector2 RoamPos { get; set; } = Vector2.Zero;  // 漫游坐标
    public DateTime NextRoamTime { get; set; } = DateTime.MinValue; // 漫游时间

    public long DodgeCD = 0;           // 躲避弹幕冷却剩余帧数

    public int BlockedTimer;            // 连续受阻帧数
    public Vector2 StuckPos;            // 上次记录的位置

    public int UseTime { get; set; }      // 武器使用冷却剩余帧数
    public Dictionary<int, int> ProjCycleIdx { get; set; }     // 弹幕循环索引

    public int RoleTier { get; set; } = -1;  // -1 表示未加载任何角色

    private readonly string UUID;     // 假人设备ID
    private readonly string Password;     // 假人密码
    private readonly SyncPlayer PlayerInfo;    // 假人外观
    private readonly Dictionary<Type, Action<object>> handlers = [];
    private readonly TrClient client;
    private Timer timer = null!;
    public Func<bool> shouldExit = () => false;

    public event Action<DummyPlayer, NetworkText, TrColor>? OnChat;
    public event Action<DummyPlayer, string>? OnMessage;

    public DummyPlayer(SyncPlayer playerInfo, string uuid, string password = "", byte team = 0)
    {
        this.PlayerInfo = playerInfo;
        this.UUID = uuid;
        this.Password = password;
        this.client = new TrClient();
        this.InternalOn(); // 注册包处理器

        this.Team = team;

        this.StuckPos = Vector2.Zero;
        this.RoamPos = Vector2.Zero;
        this.NextRoamTime = DateTime.MinValue;
        this.ProjCycleIdx = new Dictionary<int, int>();
    }

    #region 发送网络包（自动填充玩家索引）
    public void SendPacket(INetPacket packet)
    {
        if (packet is IPlayerSlot ips)
            ips.PlayerSlot = this.PlayerSlot;
        this.client.Send(packet);
    }
    #endregion

    #region 关闭假人连接并释放资源
    public void Close()
    {
        AutoAttack.MyProj.RemoveAll(p => p.fake == this);

        this.IsPlaying = false;
        this.Active = false;
        this.client?.Close();
        this.timer?.Dispose();
    }
    #endregion

    #region 连接与断连
    public void Hello(string m) => this.SendPacket(new ClientHello { Version = m });
    public void KillServer() => this.client?.KillServer();
    #endregion

    #region 请求加载指定区域的地图数据
    public void TileGetSection(int x, int y)
    {
        SendPacket(new RequestTileData
        {
            Position = new TrPoint(x, y)
        });
    }
    #endregion

    #region 生成假人（指定坐标）
    public void Spawn(short x, short y, TrPlayerSpawnContext context = TrPlayerSpawnContext.SpawningIntoWorld)
    {
        this.SendPacket(new SpawnPlayer
        {
            Team = this.Team,
            Position = new Point16 { X = x, Y = y },
            Context = context
        });
    }
    #endregion

    #region 发送玩家数据（UUID、外观、生命值、空背包等）
    public void SendPlayer(string uuid)
    {
        this.SendPacket(new ClientUUID() { UUID = uuid });
        this.SendPacket(this.PlayerInfo);
        var ssc = TShock.ServerSideCharacterConfig.Settings;
        short life = (short)ssc.StartingHealth;
        short mana = (short)ssc.StartingMana;
        this.SendPacket(new PlayerHealth { StatLifeMax = life, StatLife = life });
        this.SendPacket(new PlayerMana { StatMana = mana, StatManaMax = mana });
    }
    #endregion

    /// <summary>让假人说话（受 SpeakEnabled 控制）。</summary>
    public void Say(string text)
    {
        if (!SpeakEnabled || string.IsNullOrWhiteSpace(text)) return;
        ChatText(text);
    }

    #region 发送聊天消息（支持命令）
    public void ChatText(string message)
    {
        var packet = new NetTextModule
        {
            TextC2S = new TextC2S
            {
                Command = "Say",
                Text = message
            }
        };
        this.SendPacket(packet);
    }
    #endregion

    #region 发送心跳包,通知服务器假人仍活跃
    private void OnFrame(object? state)
    {
        if (!this.Active) return;
        this.SendPacket(new PlayerActive() { PlayerSlot = this.PlayerSlot, Active = true });
    }
    #endregion

    #region 注册指定类型包的处理器
    public void On<T>(Action<T> handler)
    {
        void Handler(object p)
        {
            if (p is T t)
                handler(t);
        }
        if (handlers.TryGetValue(typeof(T), out var val))
            handlers[typeof(T)] = val + Handler;
        else
            handlers.Add(typeof(T), Handler);
    }
    #endregion

    #region 监听所有包处理器
    public void InternalOn()
    {
        this.On<StatusText>(s => OnChat?.Invoke(this, s.Text, new TrColor()));
        this.On<TextS2C>(t => OnChat?.Invoke(this, t.Text, t.Color));
        this.On<SmartTextMessage>(t => OnChat?.Invoke(this, t.Text, t.Color));
        this.On<Kick>(k => { OnMessage?.Invoke(this, "Kicked: " + k.Reason); Close(); });

        this.On<LoadPlayer>(p =>
        {
            this.PlayerSlot = p.PlayerSlot;
            this.SendPlayer(this.UUID);
            this.SendPacket(new RequestWorldInfo());
        });

        this.On<WorldData>(i =>
        {
            if (!this.IsPlaying)
            {
                this.TileGetSection(i.SpawnX, i.SpawnY);
                this.Spawn(i.SpawnX, i.SpawnY);
                this.IsPlaying = true;
                if (!string.IsNullOrEmpty(Password))
                    ChatText($"/login {Password}");
            }
        });

        // 已登录发送请求世界信息
        this.On<StartPlaying>(_ =>
        {
            SendPacket(new RequestWorldInfo());
            DummyWork.SetNoPick(this);
        });

        // 忽略拾取相关的入站包
        this.On<ItemOwner>(p =>
        {
            SendPacket(new SyncItemDespawn()
            {
                ItemSlot = p.ItemSlot
            });
        });
    }
    #endregion

    #region 启动主循环（TCP 连接 + 接收线程）
    public void GameLoop(string host, int port, string password)
    {
        this.client.Connect(host, port);
        this.GameLoopInternal(password);
    }
    public void GameLoop(IPEndPoint endPoint, string password, IPEndPoint? proxy = null)
    {
        this.client.Connect(endPoint, proxy);
        this.GameLoopInternal(password);
    }
    #endregion

    #region 发送握手、处理密码请求、启动接收线程
    private void GameLoopInternal(string password)
    {
        this.Hello(this.CurRelease);
        this.On<RequestPassword>(_ => this.SendPacket(new SendPassword { Password = password }));
        this.Active = true;
        this.timer = new Timer(this.OnFrame, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1000));
        Task.Run(() =>
        {
            while (this.Active && !this.shouldExit())
            {
                INetPacket packet = this.client.Receive();
                try
                {
                    if (this.handlers.TryGetValue(packet.GetType(), out var act))
                        act(packet);
                }
                catch { }
            }
            this.Close();
        });
    }
    #endregion

    #region 获取假人状态信息
    /// <summary>
    /// 获取假人当前状态（跟随、工作、战斗、漫游、空闲）
    /// </summary>
    public string GetStatus()
    {
        if (!Active)
            return "未连接";
        if (UseTime > 0)  // 武器冷却中，表示最近攻击过
            return "战斗";
        if (RoamPos != Vector2.Zero && NextRoamTime > DateTime.UtcNow)
            return "漫游";
        if (Follow != null)
            return "跟随";
        return "空闲";
    }
    #endregion

    #region 发送同步弹幕包
    internal void SyncProj(short whoAmI, Vector2 pos, Vector2 vel, int type, int dmg = 0, float kb = 0)
    {
        this.SendPacket(new SyncProjectile
        {
            Key = new ProjectileKey(PlayerSlot, whoAmI, Projectile.slotGenerations[whoAmI]),
            Position = new TrVector2(pos.X,pos.Y),
            Velocity = new TrVector2(vel.X,vel.Y),
            ProjType = (short)type,
            Bit1 = new BitsByte { [0] = true, [1] = true, [4] = true },
            Damage = (short)dmg,
            Knockback = kb
        });
    }
    #endregion

    #region 发送移除弹幕包
    internal void KillProj(short whoAmI)
    {
        this.SendPacket(new KillProjectile
        {
            Key = new ProjectileKey(PlayerSlot, whoAmI, Projectile.slotGenerations[whoAmI]),
            FinalPosition = new TrVector2(Main.projectile[whoAmI].position.X, Main.projectile[whoAmI].position.Y)
        });
    }
    #endregion

    #region 发送伤害Npc包
    internal void StrikeNPC(Terraria.NPC npc, int da = 0, float kb = 0f, bool crit = false)
    {
        this.SendPacket(new StrikeNPC
        {
            NPCSlot = (byte)npc.whoAmI,
            NPCGeneration = npc.generation,
            Damage = (short)da,
            Knockback = kb,
            HitDirection = (byte)(this.TSPlayer.TPlayer.Center.X < npc.Center.X ? 1 : 0),
            Crit = crit
        });
    }
    #endregion
}