using System.Collections.Concurrent;
using ZSEBot.Enums;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace ZSEBot.Common;

/// <summary>
/// 白名单进服判定 + 免注册登录（机制移植自 CaiBotLite）：
/// 1) MessageBuffer 层只读钩子：读取 ConnectRequest / PlayerPlatformInfo / ClientUUID，
///    【永不拦截/改动报文】；等到「UUID + 平台包」齐备后才向机器人发白名单请求
///    （1.5 秒兜底：平台包迟迟不到则按当前平台降级发送，避免卡死）。
/// 2) NetGetData 钩子（最高优先级）：阻断 ContinueConnecting2 握手，阻止 TShock 原生
///    HandleConnecting（从而不再出现 /register /login 提示）。
/// 3) 回包由 CheckWhitelist 处理：Accept → HandleLogin 直接发 WorldInfo + 自动注册/登录
///    （与 CaiBotLite 一致：不经队列，避免连接在出队前失效导致 WorldInfo 永不发出、客户端卡「已找到会话」）。
/// </summary>
internal static class LoginHelper
{
    // 已发出白名单请求、等待回包的玩家 Index（fail-closed：超时未回包按拒绝进服处理）
    private static readonly ConcurrentDictionary<int, byte> PendingValidation = new();

    // 每槽位握手状态（与包到达顺序无关：平台包先到/后到都能取到真实平台）
    private const int MaxSlots = 256;
    private static readonly string?[] Uuids = new string?[MaxSlots];    // ClientUUID 包上报的设备 UUID

    /// <summary>每个槽位本次白名单请求的 request_id：回包必须带上同一个 id，防止伪造/重放的 Accept 生效</summary>
    internal static readonly string?[] PendingReqIds = new string?[MaxSlots];
    private static readonly bool[] PlatformSeen = new bool[MaxSlots];   // PlayerPlatformInfo 包是否已到
    private static readonly bool[] RequestSent = new bool[MaxSlots];    // 白名单请求是否已发出
    private static readonly long[] WaitDeadline = new long[MaxSlots];   // 等待平台包的兜底期限（TickCount64；0=无）
    private static readonly bool[] C2Seen = new bool[MaxSlots];         // ContinueConnecting2 是否已到（空 UUID 兜底发送的许可条件）
    private static readonly int[] RetryCount = new int[MaxSlots];       // 兜底发送重试计数（上限防死循环）

    private const int PlatformWaitTimeoutMs = 1500;  // 平台包等待上限（超时按当前平台降级发送；PE 平台包毫秒级到达，缩短等待加速降级）

    // 槽位复用前清空握手状态（ConnectRequest 时调用）
    private static void ResetSlot(int slot)
    {
        if (slot < 0 || slot >= MaxSlots)
        {
            return;
        }

        Uuids[slot] = null;
        PlatformSeen[slot] = false;
        RequestSent[slot] = false;
        WaitDeadline[slot] = 0;
        C2Seen[slot] = false;
        RetryCount[slot] = 0;
        PendingValidation.TryRemove(slot, out _);
    }

    // 兜底发送的主驱动源（与 CaiBotLite / 羽学白名单机器人一致，挂在 Netplay.UpdateInMainThread）：
    // 不论服务器是否有人在线都每帧触发。Terraria 1.4.5 起空服时主游戏循环停摆
    // （GamePostUpdate 不触发、握手中的玩家不算“有人”），此前兜底发送因此永不执行——
    // 无平台包客户端（实测 PC）的请求被守卫拦截后无人兜底，卡「发现服务器」。
    internal static void On_NetplayUpdateInMainThread(On.Terraria.Netplay.orig_UpdateInMainThread orig)
    {
        orig();
        try
        {
            TickRequestTimeout();
            // 换世界检测放这里：GamePostUpdate 在空服停摆，而 AutoResetPlus 可能趁没人时重置
            WorldResetGuard.Tick();
            // 定时备份也放这里：**空服正是最该备份的时候**，而 GamePostUpdate 空服停摆
            BackupScheduler.Tick();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError("[starZSEbot]兜底发送检查出错: " + ex);
        }
    }

    // 平台包等待超时的兜底发送（降级为当前平台）；由 UpdateInMainThread（每帧）与 GamePostUpdate 双驱动
    internal static void TickRequestTimeout()
    {
        if (!Config.Settings.WhiteList)
        {
            return;
        }

        var now = Environment.TickCount64;
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            var deadline = WaitDeadline[slot];
            if (deadline == 0 || now < deadline)
            {
                continue;
            }

            if (RequestSent[slot])
            {
                WaitDeadline[slot] = 0;
                continue;
            }

            // 兜底发送；被守卫拦截（名字/UUID 未齐）时重新武装期限稍后重试，避免单次失败后永久静默
            if (TrySendRequest(slot, true))
            {
                continue;
            }

            if (slot < TShock.Players.Length && TShock.Players[slot] is { ConnectionAlive: true }
                                               && RetryCount[slot] < 20)
            {
                RetryCount[slot]++;
                WaitDeadline[slot] = now + PlatformWaitTimeoutMs;
                TShock.Log.ConsoleInfo(
                    $"[starZSEbot][diag] slot={slot} platform wait timeout, force send blocked, retry={RetryCount[slot]}");
            }
            else
            {
                TShock.Log.ConsoleInfo($"[starZSEbot][diag] slot={slot} force send give up retry={RetryCount[slot]}");
                WaitDeadline[slot] = 0;
            }
        }
    }

    internal static void On_MessageBufferOnGetData(On.Terraria.MessageBuffer.orig_GetData orig, MessageBuffer self,
        int start, int length, out int messageType)
    {
        if (Config.Settings.WhiteList)
        {
            try
            {
                // 仅读取包头（不改包、不位移影响原始解析），原包必走 orig
                var reader = new BinaryReader(self.readerStream);
                reader.BaseStream.Position = start;
                var packetType = reader.ReadByte();
                if (packetType == (byte) PacketTypes.ConnectRequest)
                {
                    // 新连接：清空该槽位旧状态，平台先按 PC 记录（收到平台包再覆盖）
                    ResetSlot(self.whoAmI);
                    PlatformTracker.Set(self.whoAmI, PlatformTracker.PlatformType.PC);
                    TShock.Log.ConsoleInfo($"[starZSEbot][diag] slot={self.whoAmI} ConnectRequest recv");
                }
                else if (packetType == (byte) PacketTypes.PlayerPlatformInfo)
                {
                    // 读取偏移与 Platform 插件一致：包 ID 后跳过 1 字节，再读平台 ID
                    _ = reader.ReadByte();
                    PlatformTracker.Set(self.whoAmI, (PlatformTracker.PlatformType) reader.ReadByte());
                    if (self.whoAmI >= 0 && self.whoAmI < MaxSlots)
                    {
                        PlatformSeen[self.whoAmI] = true;
                    }

                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家槽位{self.whoAmI}上报平台: {PlatformTracker.Get(self.whoAmI)}");
                    // 平台包晚于 ClientUUID 时，在这里触发请求发送
                    TrySendRequest(self.whoAmI, false);
                }
                else if (packetType == (byte) PacketTypes.ContinueConnecting2)
                {
                    // CC2 = 握手关键节点（客户端身份材料应已发出）：标记后立即尝试一次发送；
                    // UUID 缺失时武装兜底期限（超时后按空 UUID 发送，兼容不上报 UUID 的客户端）
                    var s = self.whoAmI;
                    var hasU = s >= 0 && s < MaxSlots && !string.IsNullOrEmpty(Uuids[s]);
                    var seen = s >= 0 && s < MaxSlots && PlatformSeen[s];
                    TShock.Log.ConsoleInfo($"[starZSEbot][diag] slot={s} CC2 recv uuid={(hasU ? "yes" : "no")} platform={(seen ? "yes" : "no")}");
                    if (s >= 0 && s < MaxSlots)
                    {
                        C2Seen[s] = true;
                        if (!hasU && WaitDeadline[s] == 0)
                        {
                            WaitDeadline[s] = Environment.TickCount64 + PlatformWaitTimeoutMs;
                        }

                        TrySendRequest(s, false);
                    }
                }
                else if (packetType == (byte) PacketTypes.ClientUUID)
                {
                    var uuid = reader.ReadString();
                    if (self.whoAmI >= 0 && self.whoAmI < MaxSlots)
                    {
                        Uuids[self.whoAmI] = uuid;
                        if (WaitDeadline[self.whoAmI] == 0)
                        {
                            WaitDeadline[self.whoAmI] = Environment.TickCount64 + PlatformWaitTimeoutMs;
                        }
                    }

                    TShock.Log.ConsoleInfo($"[starZSEbot][diag] slot={self.whoAmI} ClientUUID recv uuid={(uuid.Length > 8 ? uuid[..8] : uuid)}");
                    // 平台包已到则立即发送；未到则等平台包或超时兜底
                    TrySendRequest(self.whoAmI, false);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[starZSEbot]读取登录报文时出错: " + ex);
            }
        }

        // 无论白名单开/关、无论读取成功与否，原始报文必须继续按原路径处理
        orig(self, start, length, out messageType);
    }

    // 发送白名单请求（主线程调用）；返回是否已发出（false 且连接存活时上层可稍后重试）。
    // force=true 表示等待超时兜底：CC2 已到但客户端未上报 UUID（部分 PC 端）也允许按空 UUID 发送
    private static bool TrySendRequest(int slot, bool force)
    {
        if (slot < 0 || slot >= MaxSlots || slot >= TShock.Players.Length)
        {
            return false;
        }

        if (RequestSent[slot])
        {
            return false;
        }

        var player = TShock.Players[slot];
        TShock.Log.ConsoleInfo($"[starZSEbot][diag] trySend slot={slot} force={force} sent={RequestSent[slot]} c2={C2Seen[slot]} alive={player is { ConnectionAlive: true }} nameEmpty={string.IsNullOrEmpty(player?.Name)} uuidEmpty={string.IsNullOrEmpty(Uuids[slot])} seen={PlatformSeen[slot]}");
        if (player is not { ConnectionAlive: true })
        {
            return false;
        }

        if (string.IsNullOrEmpty(Uuids[slot]) && !(force && C2Seen[slot]))
        {
            return false; // 等 ClientUUID 包；仅当 CC2 已到且超时兜底时，允许空 UUID 发送（兼容不上报 UUID 的客户端）
        }

        if (string.IsNullOrEmpty(player.Name))
        {
            return false; // 等玩家名到达（ContinueConnecting 阶段已有，此处仅防御）
        }

        if (!force && !PlatformSeen[slot])
        {
            return false; // 等 PlayerPlatformInfo 包（避免把 PE 端识别成 PC）
        }

        // fail-closed：白名单开启时，验证服务不可用 = 拒绝进服（不再静默放行）
        if (!WebsocketManager.IsWebsocketConnected)
        {
            player.SilentKickInProgress = true;
            player.Disconnect("[starZSEbot]服务器验证服务暂时不可用!\n" +
                              "请稍后再试");
            return false;
        }

        RequestSent[slot] = true;
        WaitDeadline[slot] = 0;
        PendingValidation[slot] = 0; // CheckWhitelist 收到回包后撤销；15 秒未回包则兜底踢出

        var body = (
            name: player.Name,
            ip: player.IP,
            uuid: Uuids[slot] ?? "",
            platform: PlatformTracker.Get(slot)
        );

        TShock.Log.ConsoleInfo($"[starZSEbot][diag] slot={slot} request SENT name={body.name} platform={body.platform} uuidEmpty={string.IsNullOrEmpty(body.uuid)}");

        // 绝不在游戏线程上触碰 WebSocket（并发 Send 会把主循环卡死），改由后台线程发送
        _ = Task.Run(() =>
        {
            try
            {
                var reqId = Guid.NewGuid().ToString("N");
                PendingReqIds[slot] = reqId;
                var writer = new PackageWriter(PackageType.Whitelist, true, reqId)
                    .Write("player_name", body.name)
                    .Write("player_ip", body.ip)
                    .Write("player_uuid", body.uuid)
                    .Write("player_platform", body.platform);
                writer.Critical = true; // 关键包：不能被世界/地图/存档这类大包等锁挤掉
                writer.Send();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[starZSEbot]后台发送白名单请求失败: " + ex);
            }
        });

        // 超时兜底：机器人长时间未回包 → 按拒绝进服处理（防止无限等待）
        var timeoutPlayer = player;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (!PendingValidation.TryRemove(timeoutPlayer.Index, out _))
            {
                return;
            }

            // 槽位可能已被新玩家复用：先确认该槽位仍是当时发起请求的同一玩家对象，避免误踢新连接
            var idx = timeoutPlayer.Index;
            if (idx < 0 || idx >= TShock.Players.Length
                        || !ReferenceEquals(TShock.Players[idx], timeoutPlayer))
            {
                return;
            }

            try
            {
                if (timeoutPlayer.ConnectionAlive)
                {
                    TShock.Log.ConsoleInfo(
                        $"[starZSEbot]玩家[{timeoutPlayer.Name}](IP: {timeoutPlayer.IP})白名单验证超时未回包...");
                    timeoutPlayer.SilentKickInProgress = true;
                    timeoutPlayer.Disconnect("[starZSEbot]白名单验证超时!\n" +
                                             "服务器暂无响应，请稍后再试");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[starZSEbot]白名单超时踢出失败: " + ex);
            }
        });

        return true;
    }

    /// <summary>
    /// NetGetData 钩子（最高优先级）：白名单开启时阻断握手期非法包，并拦截
    /// ContinueConnecting2 阻止 TShock 原生 HandleConnecting（不再弹 /register /login）。
    /// 放行清单在 CaiBotLite 基础上额外放行 ClientUUID / PlayerPlatformInfo
    /// （前者供 vanilla 设置 player.UUID，后者供平台识别）。
    /// </summary>
    internal static void OnGetData(GetDataEventArgs args)
    {
        if (!Config.Settings.WhiteList)
        {
            return;
        }

        try
        {
            // 越界槽位（异常/伪造包）直接跳过：原逻辑会在此抛 IndexOutOfRange 打断整个钩子链
            var idx = args.Msg.whoAmI;
            if (idx < 0 || idx >= TShock.Players.Length)
            {
                return;
            }

            var type = args.MsgID;
            var player = TShock.Players[idx];
            if (player is not { ConnectionAlive: true } ||
                (player.State < (int) ConnectionState.Complete
                 && type > PacketTypes.PlayerSpawn
                 && type != PacketTypes.PlayerMana
                 && type != PacketTypes.PlayerHp
                 && type != PacketTypes.PlayerBuff
                 && type != PacketTypes.ItemOwner
                 && type != PacketTypes.SyncLoadout
                 && type != PacketTypes.Placeholder
                 && type != PacketTypes.LoadNetModule
                 && type != PacketTypes.ClientUUID
                 && type != PacketTypes.PlayerPlatformInfo))
            {
                args.Handled = true;
                return;
            }

            if (player.State < (int) ConnectionState.Complete && type == PacketTypes.LoadNetModule)
            {
                // 短包时 ReadUInt16 可能抛异常：由外层 catch 兜底，不阻断原始报文路径
                var moduleId = (GetDataHandlers.NetModuleType) args.Msg.reader.ReadUInt16();
                // 客户端疑似异常发送 CreativePowers
                if (moduleId != GetDataHandlers.NetModuleType.CreativePowers)
                {
                    args.Handled = true;
                    return;
                }
            }

            if (type != PacketTypes.ContinueConnecting2)
            {
                return;
            }

            player.DataWhenJoined = new PlayerData(false);
            player.DataWhenJoined.CopyCharacter(player);
            player.PlayerData = new PlayerData(false);
            player.PlayerData.CopyCharacter(player);
            args.Handled = true; // 阻断原生 HandleConnecting，登录由白名单 Accept 后直接接管
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError("[starZSEbot]握手期报文处理出错: " + ex);
        }
    }

    // 白名单 Accept 后直接执行（与 CaiBotLite 一致，由 BOT 回包线程调用）：
    // 手动发 WorldInfo + 自动注册/登录（移植自 CaiBotLite / TShock 原生登录分支）
    internal static void HandleLogin(TSPlayer player)
    {
        if (player.Name == TSServerPlayer.AccountName)
        {
            player.Disconnect("[starZSEbot]此玩家名被禁止使用!");
            return;
        }

        if (player.State == (int) ConnectionState.AssigningPlayerSlot)
        {
            player.State = (int) ConnectionState.AwaitingPlayerInfo;
        }

        NetMessage.SendData((int) PacketTypes.WorldInfo, player.Index);
        Main.SyncAnInvasion(player.Index);

        var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
        if (account != null)
        {
            Login(player, account);
            return;
        }

        account = Register(player);
        Login(player, account);
    }

    // 移植 TShock 6.2.1 HandleConnecting 登录分支（不做 UUID 比对：白名单已校验设备）
    private static void Login(TSPlayer player, UserAccount account)
    {
        var group = TShock.Groups.GetGroupByName(account.Group);

        if (!TShock.Groups.AssertGroupValid(player, group, true))
        {
            return;
        }

        player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);
        if (Main.ServerSideCharacter && TShock.CharacterDB.IsSeededAppearanceMissing(player.PlayerData))
        {
            TShock.CharacterDB.SyncSeededAppearance(account, player);
            player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);
        }

        // 机器人已批准本设备（Accept 即授权凭据）→ 把 TShock 账号 UUID 同步为本次上报值，
        // 这样账号的设备绑定与服务端记录一致，后续（含原生 /login 路径）比对才有意义
        var reportedUuid = player.Index >= 0 && player.Index < MaxSlots ? Uuids[player.Index] : null;
        if (!string.IsNullOrEmpty(reportedUuid)
            && !string.Equals(account.UUID ?? "", reportedUuid, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                TShock.UserAccounts.SetUserAccountUUID(account, reportedUuid);
                account.UUID = reportedUuid;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[starZSEbot]同步账号 UUID 失败: " + ex);
            }
        }

        player.Group = group;
        player.tempGroup = null;
        player.Account = account;
        player.IsLoggedIn = true;
        player.IsDisabledForSSC = false;

        if (Main.ServerSideCharacter)
        {
            if (player.HasPermission(Permissions.bypassssc))
            {
                player.PlayerData.CopyCharacter(player);
                TShock.CharacterDB.InsertPlayerData(player);
            }

            // 与 CaiBotLite 一致：调用两次以确保 SSC 角色数据稳定还原
            player.PlayerData.RestoreCharacter(player);
            player.PlayerData.RestoreCharacter(player);
        }

        player.LoginFailsBySsi = false;

        if (player.HasPermission(Permissions.ignorestackhackdetection))
        {
            player.IsDisabledForStackDetection = false;
        }

        if (player.HasPermission(Permissions.usebanneditem))
        {
            player.IsDisabledForBannedWearable = false;
        }

        player.SendSuccessMessage($"[starZSEbot]已经验证{account.Name}登录完毕。");
        TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}]自动登录成功。");

        PlayerHooks.OnPlayerPostLogin(player);
    }

    private static UserAccount Register(TSPlayer player)
    {
        var uuid = player.Index >= 0 && player.Index < MaxSlots ? Uuids[player.Index] : null;
        var account = new UserAccount
        {
            Name = player.Name,
            Group = TShock.Config.Settings.DefaultRegistrationGroupName,
            UUID = string.IsNullOrEmpty(uuid) ? player.UUID : uuid
        };
        account.CreateBCryptHash(Guid.NewGuid().ToString());
        TShock.UserAccounts.AddUserAccount(account);
        player.SendSuccessMessage($"[starZSEbot]账户{account.Name}注册成功。");
        TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}]注册了新账户：{account.Name}");
        return account;
    }

    internal static bool CheckWhitelist(TSPlayer player, WhiteListResult result)
    {
        // 已收到机器人回包：撤销超时兜底（本次结果由下方 switch 处理，不再触发超时踢出）
        PendingValidation.TryRemove(player.Index, out _);

        var groupId = Config.Settings.GroupNumber.ToString();
        if (Config.Settings.GroupNumber == 0)
        {
            groupId = "";
        }

        if (string.IsNullOrEmpty(player.Name))
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})版本可能过低...");
            player.Disconnect("你的游戏版本可能过低,\n" +
                              "请使用Terraria1.4.4+游玩");
            return false;
        }

        try
        {
            switch (result)
            {
                case WhiteListResult.Accept:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})已通过白名单验证...");
                    break;
                }
                case WhiteListResult.NotInWhitelist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})没有添加白名单...");
                    player.SilentKickInProgress = true;
                    player.Disconnect($"[starZSEbot]没有添加白名单!\n" +
                                      $"请在群{groupId}内发送\"/添加白名单 角色名字'\"");
                    return false;
                }
                case WhiteListResult.Frozen:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})白名单已被冻结(已退群)...");
                    player.Disconnect($"[starZSEbot]你的白名单已被冻结!\n" +
                                      $"请重新加入群{groupId}即可自动解冻");
                    return false;
                }
                case WhiteListResult.InGroupBlacklist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})被屏蔽，处于群黑名单中...");
                    player.Disconnect("[starZSEbot]你已被服务器屏蔽\n" +
                                      "你处于本群黑名单中!");
                    return false;
                }
                case WhiteListResult.InBotBlacklist:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})被屏蔽，处于全局黑名单中...");
                    player.Disconnect("[starZSEbot]你已被Bot屏蔽\n" +
                                      "你处于全局黑名单中!");
                    return false;
                }
                case WhiteListResult.NeedLogin:
                {
                    TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})本次进服需要登录确认...");
                    player.Disconnect($"[starZSEbot]本次进服需要登录确认!\n" +
                                      $"请到QQ群{groupId}中 @机器人 发送\"登录\"\n" +
                                      $"批准后请重新进入服务器");

                    return false;
                }
                case WhiteListResult.Unknown:
                default:
                {
                    TShock.Log.ConsoleInfo(
                        $"[starZSEbot]玩家[{player.Name}](IP: {player.IP})无效登录结果[{result}], 可能是适配插件版本过低...");
                    player.Disconnect($"[starZSEbot]登录出错!" +
                                      $"无法处理登录结果: {result}");

                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleInfo($"[starZSEbot]玩家[{player.Name}](IP: {player.IP})验证白名单时出现错误...\n" +
                                   $"{ex}");
            player.SilentKickInProgress = true;
            player.Disconnect($"[starZSEbot]服务器发生错误无法处理该请求!\n" +
                              $"请尝试重新加入游戏或者联系服务器群{groupId}管理员");
            return false;
        }

        return true;
    }
}