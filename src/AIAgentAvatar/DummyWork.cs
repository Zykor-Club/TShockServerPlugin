extern alias TrAlias;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using TrAlias.TrProtocol.NetPackets;
using static FakePlayer.Plugin;
using static FakePlayer.PxUtil;
using Color = Microsoft.Xna.Framework.Color;
using PlayerControlData = TrAlias.TrProtocol.Models.PlayerControlData;
using PlayerControls = TrAlias.TrProtocol.NetPackets.PlayerControls;
using PlayerMiscData1 = TrAlias.TrProtocol.Models.PlayerMiscData1;
using PlayerMiscData2 = TrAlias.TrProtocol.Models.PlayerMiscData2;
using PlayerMiscData3 = TrAlias.TrProtocol.Models.PlayerMiscData3;
using DoorAction = TrAlias.TrProtocol.Models.DoorAction;
using Point = Microsoft.Xna.Framework.Point;
using SyncEquipment = TrAlias.TrProtocol.NetPackets.SyncEquipment;
using TrColor = TrAlias.Microsoft.Xna.Framework.Color;
using TrVector2 = TrAlias.Microsoft.Xna.Framework.Vector2;
using Point16 = TrAlias.Terraria.DataStructures.Point16;
using Vector2 = Microsoft.Xna.Framework.Vector2;

namespace FakePlayer;

/// <summary>
/// 假人行动逻辑（移动、攻击、图格交互、排斥等）
/// </summary>
internal static class DummyWork
{
    #region DoWork - 主行为入口
    /// <summary>
    /// 假人每帧的主行为入口（移动、攻击、工作、传送）
    /// </summary>
    /// <param name="dp">要控制的假人玩家实例</param>
    public static void DoWork(DummyPlayer dp)
    {
        // 获取假人对应的 Terraria 玩家对象
        var plr = dp.TSPlayer.TPlayer;
        if (plr == null) return;

        // 获取假人跟随的目标玩家（可能为 null）
        var plr2 = dp.Follow;

        // 1. 确定移动目标点和攻击目标 NPC
        Vector2 tarPos; NPC? npc;
        GetTaget(dp, plr, plr2, out tarPos, out npc);

        // 超距离传送（如果传送，则跳过本帧的移动和攻击包）
        if (TpMax(dp, plr, plr2, npc, plr.position)) return;

        // 2. 移动卡死检测（如果卡住并传送成功，则跳过本次移动）
        if (!Stuck(dp, plr, plr2, npc, ref tarPos)) return;

        // 3. 构建玩家控制数据包所需的结构
        var ctrl = new PlayerControlData();   // 基础控制（左右、跳跃、使用物品等）
        var misc1 = new PlayerMiscData1();    // 杂项数据1（如是否有速度）
        var misc2 = new PlayerMiscData2();    // 杂项数据2（悬停、跳跃相关）
        var misc3 = new PlayerMiscData3();    // 杂项数据3（自动连发、上次使用物品成功等）

        // 弹幕躲避（影响移动目标点和控制标志）
        Dodge(dp, plr, npc, ref tarPos, ref ctrl, ref misc2);

        // 继承跟随目标的速度（让假人的移动更平滑）
        var vel = plr2?.TPlayer?.velocity ?? plr.velocity;
        if (vel.LengthSquared() > 0.01f) misc1.HasVelocity = true;

        // 移动决策（水平移动、跳跃、飞行）
        ApplyMove(dp, plr, tarPos, ref vel, ref ctrl, ref misc1, ref misc2);


        // 攻击分支（存在敌对 NPC 且可以攻击）
        byte SelSlot = (byte)plr.selectedItem;
        if (npc != null && AutoAttack.TryAtk(dp, npc))
        {
            // 设置使用物品标志
            ctrl.IsUsingItem = true;
            misc3.AutoReuseAllWeapons = true;
            misc3.LastItemUseAttemptSuccess = true;

            // 设置面对方向
            bool faceRight = npc.Center.X > plr.Center.X;
            ctrl.FaceDirection = faceRight;
            plr.ChangeDir(faceRight ? 1 : -1);

            // 计算武器旋转角（使用原版计算方式，乘以方向）
            Vector2 aimDir = npc.Center - plr.Center;
            plr.itemRotation = (float)Math.Atan2(aimDir.Y * plr.direction, aimDir.X * plr.direction);

            // 设置物品动画时长
            var wpn = plr.inventory[plr.selectedItem];
            if (wpn != null && !wpn.IsAir)
                plr.itemAnimation = wpn.useAnimation;

            // 发送动画包和音效包（建议在 PlayerControls 之后发送，但顺序不影响方向）
            dp.SendPacket(new ItemAnimation
            {
                PlayerSlot = dp.PlayerSlot,
                Animation = (short)plr.itemAnimation,
                Rotation = plr.itemRotation
            });
            dp.SendPacket(new ItemUseSound { PlayerSlot = dp.PlayerSlot });
        }

        // 统一发送玩家控制包（包含物品使用标志）
        dp.SendPacket(new PlayerControls
        {
            PlayerSlot = dp.PlayerSlot,
            Position = new TrVector2(plr.position.X, plr.position.Y),
            Velocity = new TrVector2(vel.X,vel.Y),
            SelectedItem = SelSlot,
            PlayerControlData = ctrl,
            PlayerMiscData1 = misc1,
            PlayerMiscData2 = misc2,
            PlayerMiscData3 = misc3,
        });
    }
    #endregion

    #region GetTaget - 获取行动目标
    private static void GetTaget(DummyPlayer dp, Player p, TSPlayer? p2, out Vector2 tarPos, out NPC? npc)
    {
        // ----- 移动目标 -----
        npc = p2 != null ? AutoAttack.FindTar(p2.TPlayer, Config.NpcRange * 16f) :
               (Config.RoamAttack ? AutoAttack.FindTar(p, Config.NpcRange * 16f) : null);

        if (npc != null && npc.active)
        {
            // 先检查 NPC 中心是否在世界内（留 100 格缓冲）
            int tileX = (int)(npc.Center.X / 16);
            int tileY = (int)(npc.Center.Y / 16);
            if (!WorldGen.InWorld(tileX, tileY, 100) || IsSky(npc) || IsBanNpc(npc))
            {
                // 如果 NPC 超出世界，不可作为目标
                tarPos = p.Center;
                npc = null;
            }
            else if (npc.boss)
            {
                // 是boss 保持攻击距离
                tarPos = BoxEdge(npc, p.Center, Config.StopDist);
            }
            else
            {
                // 不是boss 直接贴脸干
                tarPos = npc.Center;
            }
        }
        else if (p2 != null)
        {
            // 跟随玩家：保持 2 格距离（32 像素），避免贴脸
            tarPos = BoxEdge(p2.TPlayer, p.Center, Config.StopDist);
        }
        else
        {
            var now = DateTime.UtcNow;
            int moveDir = (dp.TSPlayer.TPlayer.velocity.X > 0) ? 1 : (dp.TSPlayer.TPlayer.velocity.X < 0 ? -1 : 0);
            // 漫游模式
            if (GenRoam(p, moveDir, out Vector2 newPos))
            {
                dp.RoamPos = newPos;
                dp.NextRoamTime = now + TimeSpan.FromSeconds(Main.rand.Next(5, 10));
            }
            else
            {
                dp.RoamPos = p.Center;
                dp.NextRoamTime = now + TimeSpan.FromSeconds(1);
            }
            tarPos = dp.RoamPos;
        }
    }
    #endregion

    #region Stuck - 处理移动时被卡住位置方法（重构）
    private static bool Stuck(DummyPlayer dp, Player plr, TSPlayer? plr2, NPC? npc, ref Vector2 tarPos)
    {
        // 受阻超过 3 帧且处于漫游模式（无跟随玩家、无攻击目标）时，重新生成漫游点
        if (dp.BlockedTimer > 3 && dp.Follow == null && npc == null)
        {
            ResetRoam(dp, plr);
            dp.BlockedTimer = 0;
            tarPos = dp.RoamPos;
        }

        float diffX = Math.Abs(tarPos.X - plr.position.X);
        float diffY = Math.Abs(tarPos.Y - plr.position.Y);
        if (diffX <= Config.StopDist * 16 && diffY <= Config.StopDist * 16)
        {
            return true; // 已到达目标，无需处理卡住
        }

        // 获取当前玩家所在的 tile 坐标
        Point curr = new Point((int)(plr.Center.X / 16), (int)(plr.Center.Y / 16));

        // 检查当前 tile 是否已被标记为坏点
        if (BadSpots.Contains(curr) && Tick - dp.LastTP > Config.TpCD * 60)
        {
            // 传送脱困
            int w = dp.TSPlayer.TPlayer.width;
            int h = dp.TSPlayer.TPlayer.height;

            // 获取传送目标点
            Vector2 tp = (plr2?.TPlayer != null) ?
                          BoxEdge(plr2.TPlayer, plr.position, Config.StopDist) :
                        ((npc != null && npc.active && !IsSky(npc) && !IsBanNpc(npc)) ?
                          BoxEdge(npc, plr.position, Config.StopDist) :
                          new Vector2(Main.spawnTileX * 16, Main.spawnTileY * 16));

            bool TryTp(Vector2 center)
            {
                Vector2 pos = center;
                pos.Y -= 48f;
                Vector2 topLeft = pos - new Vector2(w / 2, h / 2);
                return !Collision.SolidCollision(topLeft, w, h) && dp.TSPlayer.Teleport(pos.X, pos.Y);
            }

            if (TryTp(tp))
            {
                // 传送成功后从坏点列表中移除该点
                BadSpots.Remove(curr);
                dp.RoamPos = Vector2.Zero;
                dp.NextRoamTime = DateTime.UtcNow;
                dp.DodgeCD = 0;
                dp.LastTP = Tick;
                return false; // 跳过本次移动
            }
        }

        return true;
    }
    #endregion

    #region ApplyMove - 移动决策（水平移动、跳跃、飞行）
    private static readonly float speed = Config.MoveSpeed * 16f / 60f;
    private static void ApplyMove(DummyPlayer dp, Player plr, Vector2 tarPos,
        ref Vector2 vel, ref PlayerControlData ctrl, ref PlayerMiscData1 misc1, ref PlayerMiscData2 misc2)
    {
        // 每帧更新 doorHelper（自动处理开门和关门）
        plr.doorHelper.Update(plr);
        plr.doorHelper.AllowOpeningDoorsByVelocityAloneForATime(20); // 允许用速度开门的时间

        Vector2 myPos = plr.position;
        ctrl.FaceDirection = tarPos.X > myPos.X;
        float dif = tarPos.Y - myPos.Y;
        float speed = Config.MoveSpeed * 16f / 60f;
        float hopeX = (ctrl.FaceDirection ? 1f : -1f) * speed;

        // 水平移动
        if (Math.Abs(tarPos.X - myPos.X) > Config.StopDist * 16)
        {
            ctrl.ControlRight = hopeX > 0;
            ctrl.ControlLeft = hopeX < 0;
        }

        // 检测是否受阻（使用自定义忽略半砖/斜坡的碰撞）
        float realMoveX = MoveIgnore(myPos, hopeX, plr.width, plr.height);
        bool blocked = Math.Abs(realMoveX) < Math.Abs(hopeX) * 0.2f;
        dp.BlockedTimer = blocked ? dp.BlockedTimer + 1 : 0;

        if (blocked)
        {
            Point badTile = new Point((int)(plr.Center.X / 16), (int)(plr.Center.Y / 16));
            AddBadSpot(badTile);
        }

        // ---- 垂直移动（仅在未受阻时处理跳跃/下落） ----
        if (blocked || (dif < -32 && !plr.controlJump && CanJump(dp)))
        {
            Jump(plr, ref ctrl, ref misc2);
        }
        else if (dif > 32 || plr.ZoneSkyHeight)
        {
            // 下落
            ctrl.ControlUp = ctrl.ControlJump = misc2.TryHoveringUp = false;
            plr.releaseJump = ctrl.ControlDown = misc2.TryHoveringDown = true;
        }
    }
    #endregion

    #region TpMax - 超距离传送（选最远目标）
    private static bool TpMax(DummyPlayer dp, Player plr, TSPlayer? tarPlr, NPC? npc, Vector2 myPos)
    {
        Entity? tar = null;
        float maxD = -1f;

        // 检查玩家
        if (tarPlr?.TPlayer != null)
        {
            float d = myPos.Distance(tarPlr.TPlayer.Center);
            if (d > Config.TpPlayer * 16 || (plr.AnyWet && !tarPlr.TPlayer.AnyWet))
            {
                tar = tarPlr.TPlayer;
                maxD = d;
            }
        }

        // 检查 NPC（非boss、非天空、非黑名单）
        if (npc != null && npc.active && !npc.boss && !IsSky(npc) && !IsBanNpc(npc))
        {
            float d = myPos.Distance(npc.Center);
            if (d > Config.NpcRange * 16 || (plr.AnyWet && !npc.AnyWet))
            {
                if (d > maxD) // 选更远的
                {
                    tar = npc;
                    maxD = d;
                }
            }
        }

        // 传送执行
        if (tar != null && Tick - dp.LastTP > Config.TpCD * 60)
        {
            Vector2 tppos = BoxEdge(tar, myPos, Config.StopDist);
            tppos = ClampToWorld(tppos, 64f);
            tppos.Y -= 32f;
            int w = dp.TSPlayer.TPlayer.width;
            int h = dp.TSPlayer.TPlayer.height;
            Vector2 topLeft = tppos - new Vector2(w / 2, h / 2);
            if (!Collision.SolidCollision(topLeft, w, h))
            {
                dp.TSPlayer.Teleport(tppos.X, tppos.Y);
                dp.LastTP = Tick;
                return true;
            }
        }
        return false;
    }
    #endregion

    #region Jump - 跳跃
    private static void Jump(Player plr,
    ref PlayerControlData ctrl,
    ref PlayerMiscData2 misc2, bool skip = false)
    {
        if (skip) plr.jump += 1;
        ctrl.ControlUp = ctrl.ControlJump = misc2.TryHoveringUp = true;
    }
    private static bool CanJump(DummyPlayer dp)
    {
        if (Tick - dp.LastJump < 10 ||
            dp.TSPlayer.TPlayer.ZoneSkyHeight) return false;
        dp.LastJump = Tick; return true;
    }
    #endregion

    #region IsSky + IsBanNpc 禁止靠近的NPC
    private static bool IsSky(NPC npc) =>
    npc.active && (npc.Center.Y / 16f) <= Main.worldSurface * 0.35f;
    private static bool IsBanNpc(NPC npc) =>
    npc?.active == true && Config.BlockNpc.Contains(npc.type);
    #endregion

    #region RepelFake - 假人之间的排斥（防重叠）
    public static void RepelFake(DummyPlayer dp)
    {
        if (!dp.Active || !dp.IsPlaying || Fakes is null || !Fakes.Any()) return;

        var p1 = dp.TSPlayer.TPlayer;
        // 扩大假人碰撞箱 (例如扩大 8 像素, 约 0.5 格)
        int expand = 8;
        Rectangle rect1 = p1.Hitbox;
        rect1.Inflate(expand, expand);
        for (int i = 0; i < Fakes.Length; i++)
        {
            var f = Fakes[i];
            if (f == null || !f.Active || !f.IsPlaying || f == dp) continue;
            var p2 = f.TSPlayer.TPlayer;
            if (p2 == null) continue;

            Rectangle rect2 = p2.Hitbox;
            rect2.Inflate(expand, expand);
            if (!rect1.Intersects(rect2)) continue;

            // 计算反弹方向
            Vector2 dir = p1.Center - p2.Center;
            if (dir != Vector2.Zero) dir.Normalize();
            p1.velocity += dir * 2f;
            p2.velocity -= dir * 2f;
            SendRepel(dp, true);
        }
    }
    #endregion

    #region RepelNpc - 假人与怪物之间的排斥
    /// <summary>
    /// 当假人与怪物碰撞箱重叠时，快速弹开
    /// </summary>
    public static void RepelNpc(DummyPlayer dp)
    {
        if (!dp.Active || !dp.IsPlaying || !Config.RepelNpc) return;

        var plr = dp.TSPlayer.TPlayer;

        // 扩大假人的碰撞箱
        Rectangle rect = plr.Hitbox;
        rect.Inflate(16, 16);
        for (int i = 0; i < ActiveNPCs.Count; i++)
        {
            NPC npc = ActiveNPCs[i];
            if (npc == null || !npc.active) continue;
            if (npc.friendly || npc.townNPC) continue;
            if (!rect.Intersects(npc.Hitbox)) continue;

            Vector2 dir = plr.Center - npc.Center;
            if (dir != Vector2.Zero) dir.Normalize();
            float force = Config.RepelNpcForce;
            plr.velocity = dir * force;

            SendRepel(dp);
            dp.SendPacket(new Dodge { PlayerSlot = dp.PlayerSlot, DodgeType = 2 });
            break;
        }
    }
    #endregion

    #region SendRepel - 发送排斥移动包
    /// <summary>
    /// 向服务器发送假人的移动数据，用于实现排斥时的跳跃/移动。
    /// </summary>
    /// <param name="dp">假人实例</param>
    /// <param name="jump">是否同时触发跳跃</param>
    public static void SendRepel(DummyPlayer dp, bool jump = false)
    {
        var c = new PlayerControlData();
        var m = new PlayerMiscData2();
        if (jump) Jump(dp.TSPlayer.TPlayer, ref c, ref m);
        SendRepel(dp, c, m);  // 调用下面的重载
    }

    // 重载：传入控制数据
    public static void SendRepel(DummyPlayer dp, PlayerControlData c, PlayerMiscData2 m)
    {
        var plr = dp.TSPlayer.TPlayer;
        dp.SendPacket(new PlayerControls
        {
            PlayerSlot = dp.PlayerSlot,
            Position = new TrVector2(plr.position.X, plr.position.Y),
            Velocity = new TrVector2(plr.velocity.X, plr.velocity.Y),
            PlayerControlData = c,
            PlayerMiscData2 = m
        });
    }
    #endregion

    #region Dodge - 弹幕躲避
    private static void Dodge(DummyPlayer dp, Player plr, NPC? npc,
        ref Vector2 tarPos, ref PlayerControlData ctrl, ref PlayerMiscData2 misc2)
    {
        // 冷却中 与配置开启 不处理 
        if (Tick - dp.DodgeCD > Config.DodgeCool || !Config.DodgeProj) return;

        // 检测是否有危险的弹幕
        bool danger = NeedDg(plr, out Vector2 dd);
        if (!danger) return;

        // 2. 构造理想点（玩家中心 + 方向 * 80像素）
        Vector2 ideal = plr.Center + dd * 80f;

        // 3. 寻找最近的安全点
        var spot = SafeSpot(plr, ideal);
        if (spot.HasValue)
        {
            tarPos = spot.Value;
            Jump(plr, ref ctrl, ref misc2);
        }
        else if (!plr.controlJump && CanJump(dp))
        {
            // 没有安全点就跳
            Jump(plr, ref ctrl, ref misc2, true);
            // 发送闪避包
            dp.SendPacket(new Dodge { PlayerSlot = dp.PlayerSlot, DodgeType = 2 });
        }
        else
        {
            // 跳完就下降
            ctrl.ControlUp = ctrl.ControlJump = misc2.TryHoveringUp = false;
            plr.releaseJump = ctrl.ControlDown = misc2.TryHoveringDown = true;
            // 发送闪避包
            dp.SendPacket(new Dodge { PlayerSlot = dp.PlayerSlot, DodgeType = 2 });
        }

        dp.DodgeCD = Tick;
    }

    private static bool NeedDg(Player plr, out Vector2 dangerDir)
    {
        dangerDir = Vector2.Zero;
        float closest = float.MaxValue;
        bool found = false;

        // 扩大后的玩家预警碰撞箱
        int expand = (int)((Config.DodgeRange + Config.DgExpand) * 16f);
        Rectangle plrBox = plr.Hitbox;
        plrBox.Inflate(expand, expand);

        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile p = Main.projectile[i];
            if (!p.active || !p.hostile || p.damage <= 0)
                continue;

            // 预测未来碰撞箱
            Vector2 fut = PredictPos(p.Center, p.velocity, Config.DodgeLook);
            Rectangle futBox = new Rectangle(
                (int)(fut.X - p.width / 2f),
                (int)(fut.Y - p.height / 2f),
                p.width,
                p.height
            );

            if (!plrBox.Intersects(futBox))
                continue;

            // 获取方向：从弹幕未来中心指向玩家中心
            Vector2 dir = plr.Center - fut;
            float dist = dir.LengthSquared();
            if (dist < closest)
            {
                closest = dist;
                if (dir != Vector2.Zero) dir.Normalize();
                dangerDir = dir;
                found = true;
            }
        }

        return found;
    }
    #endregion

    #region EmptyDire - 获取空方向（用于躲避弹幕）
    private static Vector2? SafeSpot(Player plr, Vector2 prefer)
    {
        float rad = Config.DodgeCheckRadius;
        Vector2? best = null;
        float bestDist = float.MaxValue;
        int w = plr.width;
        int h = plr.height;
        for (int dx = -(int)rad; dx <= (int)rad; dx++)
        {
            for (int dy = -(int)rad; dy <= (int)rad; dy++)
            {
                int tx = (int)(plr.Center.X / 16) + dx;
                int ty = (int)(plr.Center.Y / 16) + dy;
                if (!WorldGen.InWorld(tx, ty, 2)) continue;

                // 玩家左上角
                Vector2 pos = new Vector2(tx * 16 + 8 - w / 2, ty * 16 + 8 - h / 2);
                if (Collision.SolidCollision(pos, w, h)) continue;

                Vector2 spot = new Vector2(tx * 16 + 8, ty * 16 + 8);
                float dSq = Vector2.DistanceSquared(spot, prefer);
                if (dSq < bestDist)
                {
                    bestDist = dSq;
                    best = spot;
                }
            }
        }
        return best;
    }
    #endregion

    #region BoxEdge - 判断碰撞箱边缘
    private static Vector2 BoxEdge(Entity e, Vector2 from, float range)
    {
        Rectangle box = e.Hitbox;
        float cx = Math.Clamp(from.X, box.Left, box.Right);
        float cy = Math.Clamp(from.Y, box.Top, box.Bottom);
        Vector2 close = new Vector2(cx, cy);
        Vector2 dir = from - close;
        if (dir == Vector2.Zero)
            dir = new Vector2(e.direction, 0);
        else
            dir.Normalize();
        return close + dir * (range * 16f);
    }
    #endregion

    #region MoveIgnore - 水平移动碰撞检测
    /// <summary>
    /// /// 水平移动碰撞检测，忽略以下地形：
    /// - 半砖 (halfBrick)
    /// - 斜坡 (slope != 0)
    /// - 被致动的砖块 (inActive)
    /// - 关闭的门 (ClosedDoors)
    /// - 平台 (Main.tileSolidTop)
    /// 
    /// 同时保持与原 TileCollision 相同的逐像素步进方式，
    /// 且完全不影响垂直方向（Y 轴不动，仅调整 X）
    /// </summary>
    /// <param name="pos">实体当前位置（左上角）</param>
    /// <param name="velX">期望的水平移动速度（正值向右，负值向左）</param>
    /// <param name="w">实体宽度</param>
    /// <param name="h">实体高度</param>
    /// <returns>实际可行的水平位移（带符号）</returns>
    public static float MoveIgnore(Vector2 pos, float velX, int w, int h)
    {
        if (velX == 0f) return 0f;

        int dir = velX > 0 ? 1 : -1;
        float remain = Math.Abs(velX);
        float moved = 0f;

        // 确定碰撞检测范围（稍微扩大）
        int startX = (int)(pos.X / 16f) - 1;
        int endX = (int)((pos.X + w) / 16f) + 2;
        int startY = (int)(pos.Y / 16f) - 1;
        int endY = (int)((pos.Y + h) / 16f) + 2;

        startX = Math.Max(0, startX);
        endX = Math.Min(Main.maxTilesX - 1, endX);
        startY = Math.Max(0, startY);
        endY = Math.Min(Main.maxTilesY - 40, endY);

        // 逐像素移动，每次最多 1 像素
        while (remain > 0f)
        {
            float step = Math.Min(1f, remain);
            float newX = pos.X + (dir * step);
            Rectangle rect = new Rectangle((int)newX, (int)pos.Y, w, h);
            bool blocked = false;

            for (int i = startX; i <= endX && !blocked; i++)
            {
                for (int j = startY; j <= endY && !blocked; j++)
                {
                    ITile tile = Main.tile[i, j];
                    if (tile == null) continue;
                    if (!tile.active() || tile.inActive()) continue;     // 忽略被致动的砖块
                    if (tile.halfBrick()) continue;                     // 忽略半砖
                    if (tile.slope() != 0) continue;                    // 忽略斜坡
                    if (TileID.Sets.ForAdvancedCollision.ClosedDoors[tile.type]) continue; // 忽略关闭的门
                    if (!Main.tileSolid[tile.type] || Main.tileSolidTop[tile.type]) continue; // 忽略平台及非实心块

                    // 孤立单格检测（使用 WorldGen.SolidTile）
                    bool leftSolid = i - 1 >= 0 && WorldGen.SolidTile(i - 1, j);
                    bool rightSolid = i + 1 < Main.maxTilesX && WorldGen.SolidTile(i + 1, j);
                    if (!leftSolid && !rightSolid) continue; // 孤立单格，忽略阻挡

                    Rectangle tileRect = new Rectangle(i * 16, j * 16, 16, 16);
                    if (rect.Intersects(tileRect))
                    {
                        blocked = true;
                        break;
                    }
                }
            }

            if (blocked) break;

            remain -= step;
            moved += step;
            pos.X = newX;
        }

        return velX > 0 ? moved : -moved;
    }
    #endregion

    #region GenRoam - 生成随机漫游点
    /// <summary>生成随机漫游点,优先选择与当前移动方向一致的点，成功返回 true</summary>
    private static bool GenRoam(Player plr, int dir, out Vector2 pos)
    {
        int rad = Config.RoamDist;
        int cx = (int)(plr.Center.X / 16);
        int cy = (int)(plr.Center.Y / 16);
        int minX = Math.Max(100, cx - rad);
        int maxX = Math.Min(Main.maxTilesX - 100, cx + rad);
        int minY = Math.Max(100, cy - (int)(rad * Config.RoamVert));
        int maxY = Math.Min(Main.maxTilesY - 100, cy + (int)(rad * Config.RoamVert));

        // 尝试5次，优先尝试与 dir 同侧的点
        for (int i = 0; i < 5; i++)
        {
            int offX = Main.rand.Next(-rad, rad + 1);

            // 尝试前2次当前移动方向一致的方向点
            if (dir != 0 && i < 2)
                offX = Math.Abs(offX) * dir;

            int offY = (int)Main.rand.Next(-(int)(rad * Config.RoamVert), (int)(rad * Config.RoamVert) + 1);
            int nx = Math.Clamp(cx + offX, minX, maxX);
            int ny = Math.Clamp(cy + offY, minY, maxY);
            Point p = new Point(nx, ny);

            if (!BadSpots.Contains(p) && WorldGen.InWorld(nx, ny, 100))
            {
                if (Math.Abs(nx - cx) < 3 && Math.Abs(ny - cy) < 3) continue;
                pos = new Vector2(nx * 16, ny * 16);
                return true;
            }
        }
        pos = plr.Center;
        return false;
    }
    #endregion

    #region ResetRoamPoint - 重置漫游点方法
    private static void ResetRoam(DummyPlayer dp, Player plr)
    {
        int moveDir = (plr.velocity.X > 0) ? 1 : (plr.velocity.X < 0 ? -1 : 0);
        if (GenRoam(plr, moveDir, out Vector2 newPos))
        {
            dp.RoamPos = newPos;
            dp.NextRoamTime = DateTime.UtcNow + TimeSpan.FromSeconds(Main.rand.Next(5, 15));
        }
        else
        {
            dp.RoamPos = plr.Center;
            dp.NextRoamTime = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        }
    }
    #endregion

    #region SetNoPick - 给假人添加负重石防止拾取
    /// <summary>
    /// 检查假人背包中是否有负重石，如果没有则在背包最后一个槽位添加一个。
    /// 负重石可以防止假人自动拾取物品。
    /// </summary>
    /// <param name="dp">假人实例</param>
    public static void SetNoPick(DummyPlayer dp)
    {
        var plr = dp.TSPlayer.TPlayer;
        if (plr.HasItem(ItemID.EncumberingStone)) return;
        int slot = NetItem.InventoryIndex.Item1 + 49;
        dp.SendPacket(new SyncEquipment
        {
            PlayerSlot = dp.PlayerSlot,
            ItemSlot = (byte)slot,
            Stack = 1,
            Prefix = 0,
            ItemType = ItemID.EncumberingStone
        });
    }
    #endregion

    #region GetV2 - 向量转换辅助方法
    /// <summary>
    /// 将 TrProtocol 使用的 Color 转换为 XNA Color。
    /// </summary>
    public static Color GetColor(TrColor c) => new Color(r: c.R, g: c.G, b: c.B, a: c.A);
    #endregion
}