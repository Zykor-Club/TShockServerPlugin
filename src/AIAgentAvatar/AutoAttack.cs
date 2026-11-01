extern alias TrAlias;
using System.Text;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using static FakePlayer.Plugin;

namespace FakePlayer;

#region 弹幕生成配置类
/// <summary>弹幕生成配置（JSON映射）</summary>
public class ProjSpawnCfg
{
    [JsonProperty("弹幕名", Order = 1)] public string Name { get; set; } = "";     // 弹幕显示名（自动填写）
    [JsonProperty("弹幕ID", Order = 2)] public int Type = -1;                      // -1 继承武器弹幕类型ID
    [JsonProperty("以NPC为中心", Order = 3)] public bool NpcCenter = false;
    [JsonProperty("伤害", Order = 3)] public int Dmg = -1;                         // -1 继承武器伤害
    [JsonProperty("击退", Order = 4)] public float Kb = -1f;                       // -1 继承武器击退
    [JsonProperty("持续时间帧", Order = 5)] public int Life = 180;                 // 存活帧数
    [JsonProperty("发射速度", Order = 6)] public float Spd = 0f;                  // 发射速度（格/帧）
    [JsonProperty("速度向量XY/格", Order = 7)] public string VelXY = "0,0";        // 速度向量（格）
    [JsonProperty("发射偏移XY/格", Order = 8)] public string OffXY = "0,0";        // 发射位置偏移（格）
    [JsonProperty("发射角度", Order = 9)] public string Ang = "0";                 // 发射角度（度）
    [JsonProperty("模式", Order = 10)] public string Mode = "单发";                // 发射模式：单发/平行/散射/圆形/下落
    [JsonProperty("模式参数", Order = 11)] public string ModeArgs = "";            // 模式参数：数量[,参数1[,参数2]]
    [JsonProperty("更新列表", Order = 12)] public List<string> UpList = new();     // 更新配置文件名列表
    [JsonProperty("说明", Order = 13)] public string Desc { get; set; } = "";      // 自动生成的说明
}
#endregion

#region 弹幕更新配置类
/// <summary>弹幕更新阶段配置（JSON映射）</summary>
public class ProjUpdCfg
{
    [JsonProperty("弹幕名", Order = 1)] public string Name { get; set; } = "";
    [JsonProperty("新弹幕ID", Order = 2)] public int NewId = 0;                    // 变为新的弹幕ID（0不变）
    [JsonProperty("伤害", Order = 3)] public int Dmg = 40;                         // 新伤害
    [JsonProperty("击退", Order = 4)] public float Kb = 5f;                        // 新击退
    [JsonProperty("更新间隔毫秒", Order = 5)] public double Intvl = 500;           // 多久执行一次更新（毫秒）
    [JsonProperty("加时帧", Order = 6)] public int PlusLife = 0;                   // 额外增加持续时间（帧）
    [JsonProperty("新速度", Order = 7)] public float Spd = 0f;                     // 新发射速度
    [JsonProperty("新速度向量", Order = 8)] public string VelXY = "0,0";           // 新速度向量（格）
    [JsonProperty("位置偏移", Order = 9)] public string OffXY = "0,0";             // 位置偏移（格）
    [JsonProperty("旋转角度", Order = 10)] public float Rot = 0f;                  // 旋转角度（度）
    [JsonProperty("半径偏移", Order = 11)] public float Rad = 0f;                  // 半径偏移（格）
    [JsonProperty("旧追踪强度", Order = 12)] public float Homing = 0f;             // 兼容旧版追踪强度（已过时）
    [JsonProperty("AI赋值", Order = 13)] public Dictionary<int, float> AI = new(); // 修改弹幕的AI数组
    [JsonProperty("启用追踪", Order = 14)] public bool TrkEn = false;              // 是否启用追踪
    [JsonProperty("追踪强度", Order = 15)] public float TrkStr = 0.05f;            // 每帧转向比例
    [JsonProperty("追踪范围格", Order = 16)] public float TrkRng = 80f;            // 追踪范围（格）
    [JsonProperty("最大转向度", Order = 17)] public float MaxAng = 15f;            // 每帧最大转向角度（度）
    [JsonProperty("说明", Order = 18)] public string Desc { get; set; } = "";
}
#endregion

#region 武器绑定配置
/// <summary>武器与弹幕的绑定关系（JSON映射）</summary>
public class ProjWeaponBind
{
    [JsonProperty("物品名", Order = 1)] public string Name { get; set; } = "";      // 武器显示名（自动补全）
    [JsonProperty("物品ID", Order = 2)] public string Ids { get; set; } = "";       // 物品ID（支持逗号分隔多个）
    [JsonProperty("弹幕列表", Order = 3)] public List<string> Names { get; set; } = new(); // 弹幕配置文件名（循环发射）
}
#endregion

#region 弹幕状态记录
/// <summary>记录假人发射的弹幕状态，用于更新和碰撞</summary>
internal class ProjUpSt
{
    public DummyPlayer? fake;      // 所属假人
    public int Owner;              // 假人槽位
    public int Idx;                // 弹幕索引
    public int CurUp;              // 当前更新阶段索引
    public int Dmg;                // 存储的伤害值（碰撞用）
    public float Kb;               // 存储的击退值
    public List<ProjUpdCfg>? UpList; // 更新配置列表
    public DateTime NextUp;        // 下次更新时间
}
#endregion

#region 自动攻击核心类
public static class AutoAttack
{
    #region 静态数据
    public static readonly string CfgDir = Path.Combine(TShock.SavePath, PluginName, "弹幕库"); // 配置根目录
    private static string HelpFile => Path.Combine(AutoAttack.CfgDir, "使用说明.txt");
    private static string BindFile => Path.Combine(TShock.SavePath, PluginName, "武器绑定.json"); // 主配置同级目录
    private static string UpdDir => Path.Combine(CfgDir, "更新弹幕");                           // 更新配置子目录
    internal static List<ProjUpSt> MyProj = new();                                              // 所有活跃的弹幕记录
    public static Dictionary<int, List<string>> BindMap = new();                                // 武器ID -> 弹幕配置名列表
    public static Dictionary<string, ProjSpawnCfg> SpawnCache = new();                          // 生成配置缓存
    public static Dictionary<string, List<ProjUpdCfg>> UpdCache = new();                        // 更新配置缓存
    #endregion

    #region TryAtk - 假人攻击NPC
    /// <summary>
    /// 假人攻击指定的NPC（使用主手武器，支持远程、近战、魔法等）。
    /// 攻击成功后会自动清空工作状态。
    /// </summary>
    /// <param name="dp">假人实例</param>
    /// <param name="npc">攻击目标</param>
    /// <returns>是否成功发动攻击</returns>
    internal static bool TryAtk(DummyPlayer dp, NPC npc)
    {
        var plr = dp.TSPlayer.TPlayer;
        if (plr == null) return false;

        // 武器冷却检查：如果冷却未结束，不能攻击
        if (dp.UseTime > 0) return false;

        // 视线检测（CanHit）
        if (!Collision.CanHit(plr.position, plr.width, plr.height, npc.position, npc.width, npc.height))
            return false;

        var wpn = plr.inventory[0];
        if (wpn == null || wpn.IsAir || wpn.damage <= 0) return false;
        if (npc.Distance(plr.Center) >= GetAtkRange(wpn, npc)) return false;

        int type = wpn.shoot;
        float speed = wpn.shootSpeed;
        bool canShoot = false;
        int da = wpn.damage;
        float kb = wpn.knockBack;
        int ammoID = 0;
        plr.PickAmmo(wpn, ref type, ref speed, ref canShoot, ref da, ref kb, out ammoID);

        // 获取武器暴击(排除鞭子)
        bool isCrit = false;
        if (!ItemID.Sets.SummonerWeaponThatScalesWithAttackSpeed[wpn.type])
        {
            int critChance = plr.GetWeaponCrit(wpn);
            isCrit = Main.rand.Next(100) < critChance;
            if (isCrit) da = (int)(da * 1.5);
        }

        // 获取武器击退
        kb = plr.GetWeaponKnockback(wpn, kb);

        // 计算弹幕方向（从玩家指向 NPC 中心）
        Vector2 direction = npc.Center - plr.Center;
        if (direction != Vector2.Zero) direction.Normalize();
        else direction = new Vector2(plr.direction, 0f);
        Vector2 vel = direction * speed;

        // 添加微小散射
        float spread = wpn.useAmmo == AmmoID.Arrow ? 0.05f : 0.02f;
        vel = vel.RotatedByRandom(spread);

        // 弹幕武器的伤害到弹幕AI事件处理
        Strike(dp, npc, plr, wpn, type, da, kb, isCrit, vel);

        //  近战和召唤鞭用 useAnimation 远程用 useTime
        int delay = ItemID.Sets.SummonerWeaponThatScalesWithAttackSpeed[wpn.type] ||
                    wpn.melee ? wpn.useAnimation : wpn.useTime;

        dp.UseTime = delay;  // 更新攻击冷却

        return true;
    }
    #endregion

    #region Strike - 生成伤害方法
    private static void Strike(DummyPlayer dp, NPC npc, Player plr, Item wpn, int type, int dmg, float kb, bool crit, Vector2 vel)
    {
        // 已发射自定义弹幕，不再执行原版攻击逻辑
        if (TryShoot(dp, npc, plr, wpn, vel, dmg, kb)) return;

        var slot = Projectile.NewProjectile(plr.GetProjectileSource_Item(wpn),
                     plr.Center, vel, type, dmg, kb, Main.myPlayer);

        if (slot is not < 0 and not >= Main.maxProjectiles)
        {
            dp.SyncProj((short)slot, plr.position, vel, type, dmg, kb);

            if (wpn.magic || wpn.ranged)
            {
                // 无更新配置，仅记录碰撞用
                MyProj.Add(new ProjUpSt
                {
                    fake = dp,
                    Owner = dp.PlayerSlot,
                    Idx = slot,
                    Dmg = dmg,
                    Kb = kb,
                    UpList = null,
                    CurUp = 0,
                    NextUp = DateTime.MaxValue
                });
            }
            else
                // 近战武器直接造成伤害
                dp.StrikeNPC(npc, dmg, kb, crit);
        }
    }
    #endregion

    #region GetAtkRange - 武器攻击范围（像素）
    /// <summary>
    /// 计算当前武器对特定目标的推荐攻击距离（像素）
    /// </summary>
    public static float GetAtkRange(Item wpn, NPC target)
    {
        // 如果武器绑定了自定义弹幕，则视为远程攻击，使用远程范围（让假人在远处即可攻击）
        if (AutoAttack.BindMap.ContainsKey(wpn.type))
            return Config.RangeRng * 16 * Config.AtkDistMul;

        // 近战武器：模拟原版碰撞箱最大范围
        if (wpn.melee || (wpn.summon && !wpn.noMelee))
        {
            float scale = 1.0f;                     // 假人无工具速度加成
            int baseW = (int)(32 * scale);
            int baseH = (int)(32 * scale);
            // 默认朝向右侧（实际攻击时会根据方向调整，这里只取长度）
            int dir = 1;
            Rectangle rect = new Rectangle(0, 0, baseW, baseH);
            if (dir == -1)
                rect.X -= rect.Width;

            // 根据 useStyle 模拟攻击中后期扩展（最大范围）
            if (wpn.useStyle == 1)      // 挥砍
            {
                if (dir == -1)
                    rect.X -= (int)(rect.Width * 0.2);
                rect.Width = (int)(rect.Width * 2);
                rect.Height = (int)(rect.Height * 1.4);
            }
            else if (wpn.useStyle == 3) // 长矛突刺
            {
                if (dir == -1)
                    rect.X -= (int)(rect.Width * 0.4);
                rect.Width = (int)(rect.Width * 1.4);
                rect.Height = (int)(rect.Height * 0.6);
            }
            // 其他 useStyle 保持基础范围
            float baseRange = (dir == 1) ? rect.Right : (rect.Left + rect.Width);
            float npcHalf = target.width / 2f;
            float range = baseRange + npcHalf;

            // 上限 20 格（320像素）
            return Math.Min(range, 20 * 16);
        }

        // 默认使用远程配置兜底
        return Config.RangeRng * 16 * Config.AtkDistMul;
    }
    #endregion

    #region FindTar - 查找最近的敌对NPC
    /// <summary>
    /// 在玩家周围指定范围内寻找最近的、活跃的敌对NPC。
    /// </summary>
    /// <param name="plr">玩家对象</param>
    /// <param name="range">攻击范围（像素）</param>
    /// <returns>最近的NPC，如果没有则返回 null</returns>
    internal static NPC? FindTar(Player plr, float range)
    {
        NPC? best = null;
        float bestDist = range;
        var npcs = ActiveNPCs;
        for (int i = npcs.Count - 1; i >= 0; i--)
        {
            NPC? n = npcs[i];
            if (n == null || !n.active) { npcs.RemoveAt(i); continue; }

            if (!PxUtil.InWorldBounds(n.Center, 200f)) continue;

            float d = n.Distance(plr.Center);
            // 当配置文件要求视线时，才检查碰撞；否则直接算可见
            bool canSee = !Config.CanSeeNpc || Collision.CanHitLine(plr.Center, 1, 1, n.Center, 1, 1);
            if (d < bestDist && canSee)
            {
                bestDist = d;
                best = n;
            }
        }
        return best;
    }
    #endregion

    #region Fire - 发射自定义弹幕
    /// <summary>实际发射弹幕的核心逻辑（伤害强制设为0）</summary>
    private static void Fire(DummyPlayer dp, Player plr, ProjSpawnCfg s, Vector2 baseVel, int shoot, int wpnDmg, float wpnKb, NPC? npc = null)
    {
        // 决定最终弹幕类型、伤害和击退：配置值 >=0 使用配置，否则继承武器
        int type = s.Type > 0 ? s.Type : shoot;
        int dmg = s.Dmg >= 0 ? s.Dmg : wpnDmg;
        float kb = s.Kb >= 0 ? s.Kb : wpnKb;

        // 发射偏移（随机范围）
        Vector2 off = PxUtil.GetRngVec(s.OffXY, Main.rand);
        Vector2 cen;
        if (s.NpcCenter && npc != null && npc.active)
            cen = npc.Center + PxUtil.ToPx(off);
        else
            cen = plr.Center + PxUtil.ToPx(off);

        // 发射角度（随机范围）
        float angDeg = PxUtil.GetRngFloat(s.Ang, Main.rand);
        Vector2 dir = baseVel.SafeNormalize(Vector2.Zero);
        if (dir == Vector2.Zero) dir = Vector2.UnitX;
        dir = dir.RotatedBy(angDeg * Math.PI / 180);

        // 速度计算
        Vector2 vel = PxUtil.GetRngVec(s.VelXY, Main.rand);
        float spd = s.Spd > 0 ? s.Spd : baseVel.Length();
        if (vel != Vector2.Zero)
            vel = PxUtil.ToPx(vel);
        else
            vel = dir * spd;

        // 解析模式参数（数量,参数1,参数2）
        var (stack, p1, p2) = PxUtil.GetMode(s.ModeArgs);
        List<Vector2> vels = new();    // 存储每个弹幕的速度
        List<Vector2> poses = new();   // 存储每个弹幕的生成位置（用于下落模式）

        // 根据发射模式生成速度列表和位置列表
        switch (s.Mode)
        {
            case "平行": // 平行排列，间距 step 弧度
                float step = p1;
                int start = -(stack - 1) / 2;
                for (int i = 0; i < stack; i++)
                    vels.Add(dir.RotatedBy((start + i) * step) * spd);
                break;
            case "散射": // 扇形散射，总角度 (stack-1)*p1 弧度
                float total = p1 * (stack - 1);
                float angStart = -total / 2;
                for (int i = 0; i < stack; i++)
                    vels.Add(dir.RotatedBy(angStart + i * p1) * spd);
                break;
            case "圆形": // 均匀分布在圆周上
                for (int i = 0; i < stack; i++)
                {
                    float ang = MathHelper.TwoPi / stack * i;
                    vels.Add(new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * spd);
                }
                break;
            case "下落":
                float h = p1;
                for (int i = 0; i < stack; i++)
                {
                    float offX = Main.rand.Next(-(int)h, (int)h + 1);
                    Vector2 pos = new(cen.X + offX, cen.Y - h);
                    Vector2 tar;
                    if (npc != null && npc.active)
                    {
                        // 指向目标 NPC
                        tar = npc.Center + new Vector2(offX, 0) - pos;
                        if (tar != Vector2.Zero) tar.Normalize();
                    }
                    else
                    {
                        // 指向玩家中心水平偏移处
                        tar = plr.Center + new Vector2(offX, 0) - pos;
                        if (tar != Vector2.Zero) tar.Normalize();
                    }
                    vels.Add(tar * spd);
                    poses.Add(pos);
                }
                break;
            case "线性":
                float stepX = p1, stepY = p2; // 偏移增量（格）
                for (int i = 0; i < stack; i++)
                {
                    Vector2 pos = cen + new Vector2(stepX * i, stepY * i) * 16f;
                    poses.Add(pos);
                    vels.Add(dir * spd);
                }
                break;
            default: // 单发
                vels.Add(dir * spd);
                break;
        }

        // 如果未指定生成位置（非下落模式），则所有弹幕都从中心点发射
        if (poses.Count == 0)
            for (int i = 0; i < vels.Count; i++) poses.Add(cen);

        // 逐个生成弹幕
        for (int i = 0; i < vels.Count; i++)
        {
            // 生成弹幕时伤害强制为0，避免原版造成伤害
            var slot = Projectile.NewProjectile(plr.GetProjectileSource_Item(plr.inventory[0]),
                       poses[i], vels[i], type, 0, kb, dp.PlayerSlot);
            if (slot < 0 || slot >= Main.maxProjectiles) continue;

            var proj = Main.projectile[slot];
            proj.timeLeft = s.Life;     // 设置持续时间
            proj.friendly = true;       // 设为友方，但伤害为0故实际无害

            // 发送弹幕同步包给所有客户端（保证伤害同步为0）
            dp.SyncProj((short)slot, proj.position, proj.velocity, type, 0, kb);

            // 加载更新配置（如果配置了 UpList）
            List<ProjUpdCfg>? upList = null;
            if (s.UpList != null)
            {
                foreach (var upName in s.UpList)
                {
                    var list = LoadUpd(upName);
                    if (list != null)
                    {
                        if (upList == null) upList = new();
                        upList.AddRange(list);
                    }
                }
            }

            // 记录弹幕状态，供后续碰撞和更新使用
            MyProj.Add(new ProjUpSt
            {
                fake = dp,
                Owner = dp.PlayerSlot,
                Idx = slot,
                Dmg = dmg,
                Kb = kb,
                UpList = upList,
                CurUp = 0,
                NextUp = DateTime.UtcNow
            });
        }
    }

    /// <summary>假人攻击时调用，尝试发射自定义弹幕</summary>
    internal static bool TryShoot(DummyPlayer dp, NPC npc, Player plr, Item wpn, Vector2 aim, int wpnDmg, float wpnKb)
    {
        // 初始化武器循环索引字典（若为null）
        if (dp.ProjCycleIdx == null) dp.ProjCycleIdx = new Dictionary<int, int>();
        // 查找武器绑定的弹幕名列表
        if (!BindMap.TryGetValue(wpn.type, out var names) || names.Count == 0) return false;

        // 获取当前循环索引，取模获得本次使用的弹幕名，然后索引+1
        int idx = dp.ProjCycleIdx.GetValueOrDefault(wpn.type);
        string projName = names[idx % names.Count];
        dp.ProjCycleIdx[wpn.type] = idx + 1;

        // 加载弹幕生成配置
        var spawn = LoadSpawn(projName);
        if (spawn == null) return false;

        // 发射自定义弹幕
        Fire(dp, plr, spawn, aim, wpn.shoot, wpnDmg, wpnKb, npc);
        return true;
    }
    #endregion

    #region UpdateProj - 弹幕更新事件（碰撞+阶段更新）
    /// <summary>每帧由服务器钩子调用，处理自定义弹幕的碰撞和阶段更新</summary>
    public static void UpdateProj(ProjectileAiUpdateEventArgs args)
    {
        var proj = args.Projectile;
        if (proj == null || !proj.active) return;

        // 先移除所有已经失效的记录（轻量级，每帧只检查当前弹幕）
        MyProj.RemoveAll(rec => rec.Idx == proj.whoAmI && (rec.fake == null || !rec.fake.Active));

        // 查找对应的弹幕状态记录
        var rec = MyProj.FirstOrDefault(p => p != null && p.Idx == proj.whoAmI && p.Owner == proj.owner);
        if (rec == null) return;

        // 弹幕已失效，直接清理记录
        if (!proj.active)
        {
            MyProj.Remove(rec);
            return;
        }

        var dp = rec.fake;
        if (dp == null || !dp.Active)
        {
            MyProj.Remove(rec);   // 假人已断开，清理记录
            return;
        }

        // ========== 碰撞伤害检测 ==========
        var box = proj.Hitbox;
        box.Inflate(32, 32);       // 扩大碰撞箱，提高命中判定舒适度
        for (int i = 0; i < ActiveNPCs.Count; i++)
        {
            var n = ActiveNPCs[i];
            if (n == null || !n.active || n.life <= 0)
            {
                ActiveNPCs.RemoveAt(i);      // 即时清理
                continue;
            }

            if (!n.Hitbox.Intersects(box)) continue;
            int dmg = rec.Dmg > 0 ? rec.Dmg : 30;   // 默认伤害30
            dp.StrikeNPC(n, dmg, rec.Kb);           // 假人造成伤害
            dp.KillProj((short)proj.whoAmI);        // 移除弹幕
            MyProj.Remove(rec);                     // 清除记录
            return;
        }

        // ========== 阶段更新（如果有下一个更新阶段且时间到） ==========
        if (rec.UpList != null && rec.CurUp < rec.UpList.Count && DateTime.UtcNow >= rec.NextUp)
        {
            var up = rec.UpList[rec.CurUp];
            bool changed = false;     // 标记弹幕是否有变化，以决定是否发送同步包

            // 更换弹幕类型
            if (up.NewId != 0 && up.NewId != proj.type)
            {
                proj.type = up.NewId;
                changed = true;
            }
            // 额外增加持续时间
            if (up.PlusLife != 0)
            {
                proj.timeLeft += up.PlusLife;
                changed = true;
            }
            // 更改速度（标量）
            if (up.Spd > 0f && proj.velocity != Vector2.Zero)
            {
                proj.velocity = proj.velocity.SafeNormalize(Vector2.Zero) * up.Spd;
                changed = true;
            }
            // 更改速度（向量）
            if (!string.IsNullOrEmpty(up.VelXY) && up.VelXY != "0,0")
            {
                var v = PxUtil.ToVec(up.VelXY);
                if (v != Vector2.Zero) { proj.velocity = v; changed = true; }
            }

            // 追踪模式：寻找最近的敌对NPC并转向
            if (up.TrkEn)
            {
                NPC? target = null;
                float bestSq = up.TrkRng * up.TrkRng * 256f; // 范围转像素平方
                for (int i = 0; i < ActiveNPCs.Count; i++)
                {
                    var n = ActiveNPCs[i];
                    if (!n.active || n.life <= 0) continue;
                    float dSq = Vector2.DistanceSquared(proj.Center, n.Center);
                    if (dSq < bestSq)
                    {
                        bestSq = dSq;
                        target = n;
                    }
                }
                if (target != null)
                {
                    Vector2 toTar = target.Center - proj.Center;
                    if (toTar != Vector2.Zero) toTar.Normalize();
                    float curAng = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                    float tarAng = (float)Math.Atan2(toTar.Y, toTar.X);
                    float delta = PxUtil.NormAng(tarAng - curAng);        // 角度差归一化
                    float maxDelta = MathHelper.ToRadians(up.MaxAng);     // 最大转向角（弧度）
                    delta = MathHelper.Clamp(delta, -maxDelta, maxDelta);
                    float newAng = curAng + delta * up.TrkStr;            // 应用追踪强度
                    Vector2 newVel = new Vector2((float)Math.Cos(newAng), (float)Math.Sin(newAng)) * proj.velocity.Length();
                    if (newVel != proj.velocity)
                    {
                        proj.velocity = newVel;
                        changed = true;
                    }
                }
            }

            // 旋转速度向量
            if (up.Rot != 0f)
            {
                proj.velocity = proj.velocity.RotatedBy(up.Rot * Math.PI / 180);
                changed = true;
            }
            // 位置偏移
            if (!string.IsNullOrEmpty(up.OffXY) && up.OffXY != "0,0")
            {
                proj.position += PxUtil.ToVec(up.OffXY);
                changed = true;
            }
            // 半径环绕（绕圆周运动）
            if (up.Rad > 0f)
            {
                float ang = (float)(DateTime.UtcNow.Ticks % 360) * MathHelper.Pi / 180;
                proj.position += new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * up.Rad * 16f;
                changed = true;
            }
            // AI数组赋值
            foreach (var kv in up.AI)
                if (kv.Key < proj.ai.Length) { proj.ai[kv.Key] = kv.Value; changed = true; }

            // 更新存储的伤害和击退（用于后续碰撞）
            if (up.Dmg != 0) { rec.Dmg = up.Dmg; changed = true; }
            if (up.Kb != 0f) { rec.Kb = up.Kb; changed = true; }

            // 若弹幕有任何变化，则向客户端发送同步包
            if (changed)
                dp.SyncProj((short)rec.Idx, proj.position, proj.velocity, proj.type, 0, rec.Kb);

            // 移动到下一个更新阶段，并设定下次更新时间
            rec.CurUp++;
            rec.NextUp = DateTime.UtcNow.AddMilliseconds(up.Intvl);
        }
    }
    #endregion

    #region 武器绑定管理
    /// <summary>从文件读取武器绑定列表</summary>
    private static List<ProjWeaponBind> LoadBinds()
    {
        if (!File.Exists(BindFile)) return new();                     // 文件不存在则返回空列表
        var json = File.ReadAllText(BindFile);                        // 读取文件内容
        return JsonConvert.DeserializeObject<List<ProjWeaponBind>>(json) ?? new(); // 反序列化，失败则空列表
    }

    /// <summary>保存武器绑定列表到文件</summary>
    private static void SaveBinds(List<ProjWeaponBind> binds)
    {
        var json = JsonConvert.SerializeObject(binds, Formatting.Indented); // 序列化为带缩进的JSON
        File.WriteAllText(BindFile, json);                                 // 写入文件
    }

    /// <summary>刷新内存中的武器绑定映射（BindMap）</summary>
    public static void RefreshMap()
    {
        BindMap.Clear();                                          // 清空原有映射
        var binds = LoadBinds();                                  // 重新加载文件中的绑定列表
        foreach (var b in binds)
        {
            if (string.IsNullOrWhiteSpace(b.Ids)) continue;       // 没有物品ID则跳过
            var ids = b.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries); // 分割多个ID
            foreach (var idStr in ids)
            {
                if (!int.TryParse(idStr.Trim(), out int id)) continue; // 解析失败则跳过
                if (!BindMap.ContainsKey(id)) BindMap[id] = new List<string>(); // 若该ID尚无条目则创建

                // 将弹幕名添加到映射中（去重）
                foreach (var name in b.Names)
                    if (!BindMap[id].Contains(name))
                        BindMap[id].Add(name);
            }
        }
    }

    /// <summary>创建默认武器绑定文件（若不存在）</summary>
    public static void SetDefBind()
    {
        if (File.Exists(BindFile)) return;                        // 文件已存在则跳过
        var def = new List<ProjWeaponBind>                        // 默认绑定列表
        {
            new() { Ids = $"{ItemID.Tsunami}", Names = new() { "灵液箭" } },
            new() { Ids = $"{ItemID.DaedalusStormbow}", Names = new() { "圣箭", "落星" } },
            new() { Ids = $"{ItemID.StarWrath}", Names = new() { "狂星之怒" } },
            new() { Ids = $"{ItemID.BubbleGun}", Names = new() { "泡泡枪" } }
        };
        SaveBinds(def);                                            // 保存到文件
    }
    #endregion

    #region 弹幕生成配置管理
    /// <summary>获取生成配置文件的完整路径</summary>
    private static string SpawnPath(string name) => Path.Combine(CfgDir, name + ".json");

    /// <summary>保存单个生成配置到文件并更新缓存</summary>
    public static void SaveSpawn(string name, ProjSpawnCfg cfg)
    {
        var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
        File.WriteAllText(SpawnPath(name), json);
        SpawnCache[name] = cfg;  // 同步更新缓存
    }

    /// <summary>加载生成配置（优先从缓存读取）</summary>
    public static ProjSpawnCfg? LoadSpawn(string name)
    {
        if (SpawnCache.TryGetValue(name, out var cfg)) return cfg;   // 缓存命中直接返回
        var path = SpawnPath(name);
        if (!File.Exists(path)) return null;                         // 文件不存在
        cfg = JsonConvert.DeserializeObject<ProjSpawnCfg>(File.ReadAllText(path));
        if (cfg != null) SpawnCache[name] = cfg;                     // 加入缓存
        return cfg;
    }

    /// <summary>创建默认的弹幕生成配置（若不存在）</summary>
    public static void SetDefault()
    {
        var defaults = new Dictionary<string, ProjSpawnCfg>          // 预设的几个弹幕
        {
            ["灵液箭"] = new() { Type = ProjectileID.IchorArrow, Life = 180, Mode = "散射", ModeArgs = "5,0.1" },
            ["圣箭"] = new() { Type = ProjectileID.HolyArrow, Life = 240, Mode = "下落", ModeArgs = "6,400" },
            ["落星"] = new() { Type = ProjectileID.HallowStar, Life = 240, Mode = "圆形", ModeArgs = "6,400" },
            ["狂星之怒"] = new() { Type = ProjectileID.StarWrath, Life = 180, Mode = "下落", ModeArgs = "5,400,600" },
            ["泡泡枪"] = new() { Type = ProjectileID.Bubble, Life = 120, Mode = "散射", ModeArgs = "3,0.1" }
        };

        foreach (var kv in defaults)
            if (!File.Exists(SpawnPath(kv.Key)))                     // 文件不存在才创建
                SaveSpawn(kv.Key, kv.Value);
    }
    #endregion

    #region 弹幕更新配置管理
    /// <summary>获取更新配置文件的完整路径</summary>
    private static string UpdPath(string name) => Path.Combine(UpdDir, name + ".json");

    /// <summary>保存更新配置列表到文件并更新缓存</summary>
    public static void SaveUpd(string name, List<ProjUpdCfg> list)
    {
        var json = JsonConvert.SerializeObject(list, Formatting.Indented);
        File.WriteAllText(UpdPath(name), json);
        UpdCache[name] = list;                                      // 更新缓存
    }

    /// <summary>加载更新配置列表（优先从缓存读取）</summary>
    public static List<ProjUpdCfg>? LoadUpd(string name)
    {
        if (UpdCache.TryGetValue(name, out var list)) return list;
        var path = UpdPath(name);
        if (!File.Exists(path)) return null;
        list = JsonConvert.DeserializeObject<List<ProjUpdCfg>>(File.ReadAllText(path));
        if (list != null) UpdCache[name] = list;
        return list;
    }

    /// <summary>创建默认的弹幕更新配置（若不存在）</summary>
    public static void SetDefUpd()
    {
        var defs = new Dictionary<string, List<ProjUpdCfg>>
        {
            ["追踪"] = new() { new() { Intvl = 300, Homing = 0.08f, Spd = 12 }, new() { Intvl = 400, Homing = 0.12f, Spd = 16 } },
            ["加速"] = new() { new() { Intvl = 200, Spd = 18 }, new() { Intvl = 200, Spd = 24 } },
            ["分裂"] = new() { new() { Intvl = 100, NewId = ProjectileID.Bullet, Spd = 10 } },
            ["旋转"] = new() { new() { Intvl = 100, Rot = 15f, Rad = 1f } },
            ["强追踪"] = new() { new() { Intvl = 150, TrkEn = true, TrkStr = 0.15f, TrkRng = 200, MaxAng = 30, Spd = 20 } },
            ["变伤害"] = new() {
            new() { Intvl = 300, Dmg = 50, Kb = 8f },
            new() { Intvl = 300, Dmg = 80, Kb = 12f },
            new() { Intvl = 300, Dmg = 120, Kb = 15f }},
            ["变AI"] = new() {
            new() { Intvl = 200, AI = new Dictionary<int, float> { { 0, 100f } } },
            new() { Intvl = 200, AI = new Dictionary<int, float> { { 0, 200f }, { 1, 50f } } }},
            ["变向量"] = new() {
            new() { Intvl = 300, VelXY = "5,0", Spd = 0 },
            new() { Intvl = 300, VelXY = "-5,0" },
            new() { Intvl = 300, VelXY = "0,8" }},
            ["延长"] = new() {
            new() { Intvl = 500, PlusLife = 60 },
            new() { Intvl = 500, PlusLife = 60 }},
            ["震荡"] = new() {
            new() { Intvl = 200, VelXY = "-8,0" },
            new() { Intvl = 200, VelXY = "8,0" },
            new() { Intvl = 200, VelXY = "-8,0" }},
            ["变速"] = new() {
            new() { Intvl = 300, Spd = 20 },
            new() { Intvl = 300, Spd = 8 },
            new() { Intvl = 300, Spd = 20 }},
            ["追踪分裂"] = new() {
            new() { Intvl = 600, TrkEn = true, TrkStr = 0.05f, Spd = 8 },
            new() { NewId = ProjectileID.Bullet, Spd = 12, Dmg = 30 }},
        };
        foreach (var kv in defs)
            if (!File.Exists(UpdPath(kv.Key)))
                SaveUpd(kv.Key, kv.Value);
    }
    #endregion

    #region 初始化与重载
    /// <summary>初始化系统：创建目录和默认配置文件（不加载到内存）</summary>
    public static void Init()
    {
        if (!Directory.Exists(CfgDir)) Directory.CreateDirectory(CfgDir); // 创建主目录
        if (!Directory.Exists(UpdDir)) Directory.CreateDirectory(UpdDir); // 创建更新目录
        SetDefBind();       // 确保武器绑定文件存在
        SetDefault();      // 确保生成配置存在
        SetDefUpd();        // 确保更新配置存在
        // 确保使用说明文件存在（不覆盖已有）
        if (!File.Exists(HelpFile)) CreateHelp();
    }

    /// <summary>热重载：补全名称/说明，刷新内存映射，清空配置缓存</summary>
    public static void Reload()
    {
        SpawnCache.Clear(); // 清空生成弹幕配置缓存，下次使用时重新从文件读取
        UpdCache.Clear();   // 清空更新弹幕配置缓存
        BindMap.Clear();    // 清空武器绑定缓存

        WeaponName();       // 补全武器绑定中缺失的物品名（可能写入文件）
        ProjName();         // 补全弹幕生成/更新配置中缺失的名称和说明（可能写入文件）
        RefreshMap();       // 重新加载绑定到 BindMap（读取已补全的文件）
    }
    #endregion

    #region 自动补全名称和说明
    /// <summary>补全武器绑定中缺失的物品名（从Lang获取）</summary>
    public static void WeaponName()
    {
        if (!File.Exists(BindFile)) return;
        var binds = LoadBinds();
        bool changed = false;
        foreach (var b in binds)
        {
            b.Name = string.Empty;
            if (string.IsNullOrEmpty(b.Name))
            {
                // 取第一个ID作为代表（支持多ID，只取第一个）
                var first = b.Ids.Split(',')[0].Trim();
                if (int.TryParse(first, out int id))
                    b.Name = Lang.GetItemNameValue(id);    // 获取物品显示名
                else
                    b.Name = "未知";
                changed = true;
            }
        }
        if (changed) SaveBinds(binds);                    // 有修改则保存到文件
    }

    /// <summary>重新生成所有弹幕配置的名称和说明（强制覆盖，不保留旧值）</summary>
    public static void ProjName()
    {
        // ========== 1. 生成配置 ==========
        var files = Directory.GetFiles(CfgDir, "*.json");
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var cfg = JsonConvert.DeserializeObject<ProjSpawnCfg>(json);
            if (cfg == null) continue;

            string name = Path.GetFileNameWithoutExtension(file);
            SpawnCache[name] = cfg;

            // 强制清空，然后重新生成
            cfg.Name = string.Empty;
            cfg.Desc = string.Empty;
            bool needSave = false;

            if (string.IsNullOrEmpty(cfg.Name))
            {
                cfg.Name = cfg.Type > 0 ? Lang.GetProjectileName(cfg.Type).Value : "继承武器";
                needSave = true;
            }
            if (string.IsNullOrEmpty(cfg.Desc))
            {
                cfg.Desc = SpawnDesc(cfg);
                needSave = true;
            }

            if (needSave)
                File.WriteAllText(file, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }

        // ========== 2. 更新配置 ==========
        if (!Directory.Exists(UpdDir)) return;
        var updFiles = Directory.GetFiles(UpdDir, "*.json");
        foreach (var file in updFiles)
        {
            var json = File.ReadAllText(file);
            var list = JsonConvert.DeserializeObject<List<ProjUpdCfg>>(json);
            if (list == null) continue;

            string upName = Path.GetFileNameWithoutExtension(file);
            UpdCache[upName] = list;

            bool needSave = false;
            foreach (var up in list)
            {
                // 强制清空名称和说明，然后重新生成
                up.Name = string.Empty;
                up.Desc = string.Empty;

                if (string.IsNullOrEmpty(up.Name))
                {
                    up.Name = up.NewId > 0 ? Lang.GetProjectileName(up.NewId).Value : "继承武器";
                    needSave = true;
                }

                if (string.IsNullOrEmpty(up.Desc))
                {
                    up.Desc = UpdDesc(up);
                    needSave = true;
                }
            }
            if (needSave)
                File.WriteAllText(file, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
    }
    /// <summary>根据弹幕生成配置自动生成说明文本</summary>
    private static string SpawnDesc(ProjSpawnCfg s)
    {
        var (cnt, p1, p2) = PxUtil.GetMode(s.ModeArgs);
        string baseName = !string.IsNullOrEmpty(s.Name) ? s.Name : (s.Type > 0 ? $"ID{s.Type}" : "继承武器");
        // 模式描述
        string mode = s.Mode switch
        {
            "单发" => $"单发 {baseName}",
            "平行" => cnt == 1 ? $"单发 {baseName}" : $"平行 {cnt}枚{baseName}，间隔{p1 * 57.3:F1}°",
            "散射" => cnt == 1 ? $"单发 {baseName}" : $"扇形 {cnt}枚{baseName}，总{(cnt - 1) * p1 * 57.3:F1}°",
            "圆形" => $"圆形 {cnt}枚{baseName}",
            "下落" => $"下落 {cnt}枚{baseName}，高度{p1:F0}",
            "线性" => cnt == 1 ? $"单发 {baseName}" : $"线性 {cnt}枚{baseName}，偏移({p1:F1},{p2:F1})格/枚",
            _ => $"发射 {baseName}"
        };
        // 数值描述
        string dmg = s.Dmg >= 0 ? $"伤害{s.Dmg}" : "伤害继承武器";
        string kb = s.Kb >= 0 ? $"击退{s.Kb:F1}" : "击退继承武器";
        string spd = s.Spd > 0 ? $"速度{s.Spd}像素/帧" : "速度继承武器";
        string life = $"持续{s.Life}帧";
        string upd = (s.UpList != null && s.UpList.Count > 0) ? $"更新:{string.Join(",", s.UpList)}" : "";
        return $"{mode}；{dmg}；{kb}；{spd}；{life}。{upd}".TrimEnd('。');
    }

    /// <summary>根据弹幕更新配置自动生成说明文本</summary>
    private static string UpdDesc(ProjUpdCfg u)
    {
        var parts = new List<string>();
        string name = !string.IsNullOrEmpty(u.Name) ? u.Name : $"ID{u.NewId}";
        if (u.NewId != 0) parts.Add($"变{name}");
        if (u.Dmg != 0) parts.Add($"伤害{u.Dmg}");
        if (u.Kb != 0) parts.Add($"击退{u.Kb:F1}");
        if (u.Spd != 0) parts.Add($"速度{u.Spd:F1}");
        if (!string.IsNullOrEmpty(u.VelXY) && u.VelXY != "0,0") parts.Add($"方向{u.VelXY}");
        if (u.PlusLife != 0) parts.Add($"加时{u.PlusLife}");
        if (u.Rot != 0) parts.Add($"旋{u.Rot * 60 / 360:F1}圈/秒");
        if (u.Rad != 0) parts.Add($"半径{u.Rad}格");
        if (u.Homing > 0) parts.Add($"旧追踪{u.Homing * 100:F0}%");
        if (u.AI.Count > 0) parts.Add($"AI:{string.Join(",", u.AI.Select(kv => $"ai[{kv.Key}]={kv.Value}"))}");
        if (u.TrkEn) parts.Add($"追踪({u.TrkRng}格,{u.TrkStr * 100:F0}%,{u.MaxAng}°/帧)");
        if (parts.Count == 0) return "无变化";
        return $"每{u.Intvl}毫秒，" + string.Join("，", parts);
    }
    #endregion

    #region 生成使用说明文件
    /// <summary>生成使用说明文件（若不存在）</summary>
    public static void CreateHelp()
    {
        if (File.Exists(HelpFile)) return; // 已存在，不覆盖（用户可自行修改）

        var sb = new StringBuilder();
        sb.AppendLine("═════════════════════════════════");
        sb.AppendLine("                    假人插件 - 弹幕系统使用说明");
        sb.AppendLine("═════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("【一、目录结构】");
        sb.AppendLine($"  弹幕库根目录：{AutoAttack.CfgDir}");
        sb.AppendLine("  ├─ 武器绑定.json          ← 武器ID → 弹幕配置名映射");
        sb.AppendLine("  ├─ 弹幕配置名.json        ← 弹幕生成配置（多个）");
        sb.AppendLine("  └─ 更新弹幕/                  ← 弹幕更新阶段配置（子目录）");
        sb.AppendLine();
        sb.AppendLine("【二、武器绑定配置（武器绑定.json）】");
        sb.AppendLine("  格式示例：");
        sb.AppendLine("  [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"物品名\": \"海啸\",         // 可选，自动补全");
        sb.AppendLine("      \"物品ID\": \"2623\",        // 支持多个ID，用逗号分隔，如 \"2623,2624\"");
        sb.AppendLine("      \"弹幕列表\": [\"灵液箭\"]    // 弹幕配置文件名（不含.json），可多个，依次循环");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine();
        sb.AppendLine("【三、弹幕生成配置（*.json）】");
        sb.AppendLine("  字段说明：");
        sb.AppendLine("  - 弹幕名 ---- 显示名称，自动补全");
        sb.AppendLine("  - 弹幕ID ---- 弹幕类型，填 0 或负数则继承武器原有弹幕");
        sb.AppendLine("  - 伤害 / 击退 ---- -1 表示继承武器属性，>=0 强制使用配置值");
        sb.AppendLine("  - 持续时间帧 ---- 弹幕存在时间（1秒=60帧）");
        sb.AppendLine("  - 发射速度 ---- 0则继承武器速度（像素/帧）");
        sb.AppendLine("  - 速度向量XY/格 ---- 固定速度向量，如 \"10,0\"；或随机范围 \"xMin,xMax,yMin,yMax\"，如 \"5,10,0,5");
        sb.AppendLine("  - 发射偏移XY/格 ---- 固定偏移，如 \"2,0\"；或随机范围 \"xMin,xMax,yMin,yMax\"，如 \"-5,5,0,0");
        sb.AppendLine("  - 发射角度 ---- 固定角度（度），如 \"30\"；或随机范围 \"min,max\"，如 \"-15,15");
        sb.AppendLine("  - 模式 ---- 单发 / 平行 / 散射 / 圆形 / 下落 / 线性");
        sb.AppendLine("  - 模式参数 ---- 格式 \"数量,参数1,参数2\"");
        sb.AppendLine("      ・ 平行：参数1 = 相邻弹幕角度增量（弧度），如 \"5,0.1\" 表示5支，间隔5.73°");
        sb.AppendLine("      ・ 散射：参数1 = 相邻弹幕角度增量（弧度），如 \"5,0.2\" 表示5支，总角度约45.8°");
        sb.AppendLine("      ・ 圆形：只需数量，如 \"8\" 表示8支均匀一圈");
        sb.AppendLine("      ・ 下落：参数1 = 下落高度（像素），如 \"6,400\" 表示6支箭，从400像素高随机落下");
        sb.AppendLine("      ・ 线性：参数1 = X方向偏移（格），参数2 = Y方向偏移（格），如 \"5,2,1\" 表示5枚弹幕，每枚向右偏移2格、向下偏移1格");
        sb.AppendLine("      ・ 单发：模式参数无效，只发射1枚");
        sb.AppendLine("  - 更新列表 ---- 更新配置文件名数组（不含.json），按顺序执行");
        sb.AppendLine();
        sb.AppendLine("【四、弹幕更新配置（更新弹幕/*.json）】");
        sb.AppendLine("  是一个JSON数组，每个元素代表一个更新阶段：");
        sb.AppendLine("  [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"弹幕名\": \"追踪弹幕\",");
        sb.AppendLine("      \"新弹幕ID\": 0,              // 0不修改，>0则更换（请勿使用负数）");
        sb.AppendLine("      \"伤害\": 40,                 // 0不修改");
        sb.AppendLine("      \"击退\": 5.0,");
        sb.AppendLine("      \"更新间隔毫秒\": 500,        // 阶段持续时间");
        sb.AppendLine("      \"加时帧\": 0,                // 弹幕总存在时间增加");
        sb.AppendLine("      \"新速度\": 12.0,             // 发射速度");
        sb.AppendLine("      \"新速度向量\": \"0,0\",        // 速度向量（优先级高于标量）");
        sb.AppendLine("      \"位置偏移\": \"0,0\",          // 每次更新时位置偏移（格）");
        sb.AppendLine("      \"旋转角度\": 15.0,           // 每阶段旋转角度（度）");
        sb.AppendLine("      \"半径偏移\": 1.0,            // 圆周运动半径（格）");
        sb.AppendLine("      \"启用追踪\": true,           // 是否追踪最近敌对NPC");
        sb.AppendLine("      \"追踪强度\": 0.08,           // 每帧转向强度（0~1）");
        sb.AppendLine("      \"追踪范围格\": 120,          // 追踪有效范围");
        sb.AppendLine("      \"最大转向度\": 15,           // 每帧最大转向角度（度）");
        sb.AppendLine("      \"AI赋值\": {}               // 修改弹幕的ai[0~3]数组");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine();
        sb.AppendLine("【五、注意事项】");
        sb.AppendLine("  1. 所有文件编码均为 UTF-8，支持中文。");
        sb.AppendLine("  2. 弹幕生成时伤害固定为0，实际伤害由“伤害”字段决定（碰撞时由假人造成）。");
        sb.AppendLine("  3. 更新阶段按顺序执行，完成后进入下一阶段，全部完成后不再更新。");
        sb.AppendLine("  4. 追踪功能每帧转向，强度过高会导致弹幕抖动，建议0.05~0.15。");
        sb.AppendLine("  5. 重载命令：/reload");
        sb.AppendLine("═════════════════════════════════");

        File.WriteAllText(HelpFile, sb.ToString(), Encoding.UTF8);
        TShock.Log.ConsoleInfo($"[{PluginName}] 已创建弹幕系统使用说明：{HelpFile}");
    }
    #endregion
}
#endregion