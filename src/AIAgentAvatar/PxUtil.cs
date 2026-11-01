using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using TShockAPI;

namespace FakePlayer;

/// <summary>
/// 像素转换和游戏实体工具类
/// 提供像素与格数的转换、实体验证、距离计算等常用功能
/// </summary>
public static class PxUtil
{
    #region 基础转换方法
    // 格数转像素
    public static float ToPx(float tiles) => tiles * 16f;
    // 格数转像素
    public static Vector2 ToPx(Vector2 tiles) => tiles * 16f;
    // 格数转像素
    public static int ToPx(int tiles) => tiles * 16;
    // 像素转格数
    public static float ToTiles(float pixels) => pixels / 16f;
    // 像素转格数
    public static Vector2 ToTiles(Vector2 pixels) => pixels / 16f;
    // 像素转格数
    public static int ToTiles(int pixels) => pixels / 16;
    #endregion

    #region 扩展方法
    // 扩展方法：格数转像素
    public static Vector2 ToPx2(this Vector2 tiles) => PxUtil.ToPx(tiles);
    // 扩展方法：格数转像素
    public static float ToPx2(this float tiles) => PxUtil.ToPx(tiles);
    // 扩展方法：格数转像素
    public static int ToPx2(this int tiles) => PxUtil.ToPx(tiles);
    // 扩展方法：像素转格数
    public static Vector2 ToTiles2(this Vector2 pixels) => PxUtil.ToTiles(pixels);
    // 扩展方法：像素转格数
    public static float ToTiles2(this float pixels) => PxUtil.ToTiles(pixels);
    // 扩展方法：像素转格数
    public static int ToTiles2(this int pixels) => PxUtil.ToTiles(pixels);
    // 扩展方法：检查是否在像素范围内
    public static bool IsInRangeOf(this Vector2 from, Vector2 to, float rangePx) => 
        InRange(from, to, rangePx);
    // 扩展方法：检查是否在格数范围内
    public static bool IsInRangeOfTiles(this Vector2 from, Vector2 to, float rangeTiles) => 
        InRangeTiles(from, to, rangeTiles);
    #endregion

    #region 数学工具方法
    // 线性插值
    public static float Lerp(float from, float to, float amount) => MathHelper.Lerp(from, to, amount);
    // 限制浮点数范围
    public static float Clamp(float value, float min, float max) => MathHelper.Clamp(value, min, max);
    // 限制整数范围
    public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
    // 平滑插值
    public static float SmoothStep(float min, float max, float x) => Terraria.Utils.SmoothStep(min, max, x);
    // 获取插值比例
    public static float GetLerpValue(float from, float to, float t, bool clamped = false) => 
        Terraria.Utils.GetLerpValue(from, to, t, clamped);
    #endregion

    #region 距离计算
    // 计算两点间距离平方（性能更好）
    public static float DistanceSquared(Vector2 from, Vector2 to) => Vector2.DistanceSquared(from, to);
    // 计算两点间实际距离
    public static float Distance(Vector2 from, Vector2 to) => Vector2.Distance(from, to);
    // 计算从起点到目标点的角度
    public static float AngleTo(Vector2 from, Vector2 to) => (float)Math.Atan2(to.Y - from.Y, to.X - from.X);
    #endregion

    #region 范围检查
    // 检查两点是否在指定像素范围内
    public static bool InRange(Vector2 from, Vector2 to, float rangePx) => 
        Vector2.DistanceSquared(from, to) <= rangePx * rangePx;
    
    // 检查两点是否在指定格数范围内
    public static bool InRangeTiles(Vector2 from, Vector2 to, float rangeTiles) => 
        InRange(from, to, ToPx(rangeTiles));
    
    // 使用平方距离检查范围（避免开方运算）
    public static bool InRangeSquared(Vector2 from, Vector2 to, float rangePx) => 
        Vector2.DistanceSquared(from, to) <= rangePx * rangePx;
    #endregion

    #region 方向计算
    
    // 获取从起点指向目标点的方向向量
    public static Vector2 DirectionTo(Vector2 from, Vector2 to)
    {
        if (from == to) return Vector2.UnitX;
        return Vector2.Normalize(to - from);
    }
    
    // 安全获取方向向量（避免除零错误）
    public static Vector2 SafeDirectionTo(Vector2 from, Vector2 to, Vector2 defaultValue = default) => 
        (to - from).SafeNormalize(defaultValue);
    
    #endregion

    #region 实体验证
    // 验证NPC是否有效
    public static bool IsValidMst(NPC mst, NPC excludeNpc = null!) => 
        mst?.active == true && (excludeNpc == null || mst.whoAmI != excludeNpc.whoAmI);
    
    // 验证玩家是否有效（Player对象）
    public static bool IsValidPlr(Player plr) => 
        plr?.active == true && !plr.dead && plr.statLife > 0;
    
    // 验证玩家是否有效（TSPlayer对象）
    public static bool IsValidPlr(TSPlayer plr) => 
        plr?.Active == true && !plr.Dead && plr.TPlayer != null && plr.TPlayer.statLife > 0;
    
    // 验证抛射物是否有效
    public static bool IsValidProj(Projectile proj) => 
        proj?.active == true && proj.owner == Main.myPlayer;
    #endregion

    #region 生命值计算
    // 获取NPC生命值百分比
    public static float GetLifeRatio(NPC npc)
    {
        if (npc?.active != true || npc.lifeMax <= 0) return 0f;
        return MathHelper.Clamp(npc.life * 100f / npc.lifeMax, 0f, 100f);
    }
    
    // 获取玩家生命值百分比
    public static float GetLifeRatio(Player plr)
    {
        if (plr?.active != true || plr.statLifeMax2 <= 0) return 0f;
        return MathHelper.Clamp(plr.statLife * 100f / plr.statLifeMax2, 0f, 100f);
    }
    #endregion

    #region 向量和位置工具
    // 生成随机偏移向量（对称范围）
    public static Vector2 RandomOffset(float maxOffset)
    {
        if (maxOffset <= 0) return Vector2.Zero;
        return new Vector2(
            Main.rand.Next((int)-maxOffset, (int)maxOffset),
            Main.rand.Next((int)-maxOffset, (int)maxOffset)
        );
    }
    
    // 生成随机偏移向量（指定范围）
    public static Vector2 RandomOffset(float minOffset, float maxOffset)
    {
        if (minOffset > maxOffset) 
        {
            float temp = minOffset;
            minOffset = maxOffset;
            maxOffset = temp;
        }
        return new Vector2(
            Main.rand.Next((int)minOffset, (int)maxOffset),
            Main.rand.Next((int)minOffset, (int)maxOffset)
        );
    }
    
    // 预测实体未来位置
    public static Vector2 PredictPos(Vector2 currentPos, Vector2 velocity, float time) => 
        currentPos + velocity * Math.Max(0, time);
    
    // 旋转向量
    public static Vector2 RotateVector(Vector2 vector, float radians) => vector.RotatedBy(radians);
    
    // 获取实体中心位置
    public static Vector2 GetEntityCenter(Entity entity) => entity?.Center ?? Vector2.Zero;
    
    // 获取实体碰撞箱
    public static Rectangle GetEntityHitbox(Entity entity) => entity?.Hitbox ?? new Rectangle(0, 0, 0, 0);
    
    #endregion

    #region 世界边界检查
    
    // 检查位置是否在世界边界内
    public static bool InWorldBounds(Vector2 position, float margin = 0f) => 
        position.X >= margin && position.X <= Main.maxTilesX * 16f - margin &&
        position.Y >= margin && position.Y <= Main.maxTilesY * 16f - margin;
    
    // 检查格数位置是否在世界边界内
    public static bool InWorldBoundsTiles(Vector2 tilePosition, float margin = 0f) => 
        tilePosition.X >= margin && tilePosition.X <= Main.maxTilesX - margin &&
        tilePosition.Y >= margin && tilePosition.Y <= Main.maxTilesY - margin;
    
    // 将位置限制在世界边界内
    public static Vector2 ClampToWorld(Vector2 position, float margin = 16f)
    {
        float maxX = Main.maxTilesX * 16f;
        float maxY = Main.maxTilesY * 16f;
        return new Vector2(
            MathHelper.Clamp(position.X, margin, maxX - margin),
            MathHelper.Clamp(position.Y, margin, maxY - margin)
        );
    }
    
    #endregion

    #region 游戏逻辑工具
    // 获取NPC的有效目标玩家
    public static Player? GetValidTarget(NPC npc)
    {
        if (npc?.active != true || npc.target < 0 || npc.target >= Main.maxPlayers) return null;
        var plr = Main.player[npc.target];
        return IsValidPlr(plr) ? plr : null;
    }
    
    // 检查玩家生命值条件
    public static bool CheckHPCondition(Player plr, int hpValue, int hpRatio) => 
        plr != null && CheckHPCondition(hpValue, hpRatio, plr.statLife, plr.statLifeMax2);
    
    // 检查TSPlayer生命值条件
    public static bool CheckHPCondition(TSPlayer plr, int hpValue, int hpRatio) => 
        plr?.TPlayer != null && CheckHPCondition(hpValue, hpRatio, plr.TPlayer.statLife, plr.TPlayer.statLifeMax2);
    
    // 检查数量条件并记录消息
    public static bool CheckCountCondition(int required, int actual, string typeName, StringBuilder message = null)
    {
        if (required == 0) return true;
        
        if (required > 0 && actual < required)
        {
            message?.AppendLine($" {typeName}数量不足: 需要{required}个, 当前{actual}个");
            return false;
        }
        
        if (required < 0 && actual >= Math.Abs(required))
        {
            message?.AppendLine($" {typeName}数量过多: 需要少于{Math.Abs(required)}个, 当前{actual}个");
            return false;
        }
        
        return true;
    }
    
    #endregion

    #region 复杂工具方法
    // 解析范围字符串（如 "1,10"）
    public static (bool success, T min, T max) ParseRange<T>(string rangeStr, Func<string, T> parser) where T : IComparable<T>
    {
        if (string.IsNullOrWhiteSpace(rangeStr)) return (false, default, default)!;
            
        var parts = rangeStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (false, default, default)!;
        
        try
        {
            T val1 = parser(parts[0].Trim());
            T val2 = parser(parts[1].Trim());
            return (true, 
                val1.CompareTo(val2) <= 0 ? val1 : val2, 
                val1.CompareTo(val2) >= 0 ? val1 : val2);
        }
        catch
        {
            return (false, default, default)!;
        }
    }
    
    // 解析浮点数范围
    public static (bool success, float min, float max) ParseFloatRange(string rangeStr) => 
        ParseRange(rangeStr, float.Parse);
    
    // 解析整数范围
    public static (bool success, int min, int max) ParseIntRange(string rangeStr) => 
        ParseRange(rangeStr, int.Parse);
    
    // 内部方法：检查生命值条件
    private static bool CheckHPCondition(int hpValue, int hpRatio, int currentHP, int maxHP)
    {
        if (maxHP <= 0) return false;

        bool hpOk = hpValue == -1 || 
                   (hpValue > 0 ? currentHP >= hpValue : currentHP < Math.Abs(hpValue));

        bool ratioOk = true;
        if (hpRatio != -1)
        {
            float currentRatio = currentHP * 100f / maxHP;
            ratioOk = hpRatio > 0 ? currentRatio >= hpRatio : currentRatio < Math.Abs(hpRatio);
        }

        return hpOk && ratioOk;
    }
    
    #endregion
    
    #region 角度计算（为TarLock补充）
    // 计算从原点出发的两个向量之间的角度差
    public static float AngleTo(Vector2 origin, Vector2 v1, Vector2 v2)
    {
        // 归一化向量
        v1 = Vector2.Normalize(v1 - origin);
        v2 = Vector2.Normalize(v2 - origin);
        
        var dot = Vector2.Dot(v1, v2);
        var det = v1.X * v2.Y - v1.Y * v2.X;
        return (float)(Math.Atan2(det, dot) * (180.0 / Math.PI));
    }
    #endregion

    #region 将字符串解析为Vector2，并且将解析到的两个浮点数作为格数
    public static Vector2 ToVec(this string vecStr, Vector2 val = default)
    {
        if (string.IsNullOrWhiteSpace(vecStr) || vecStr == "0,0")
            return val;

        try
        {
            // 按逗号分割字符串
            var parts = vecStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return val;

            // 解析为浮点数，保持原始顺序
            float x = float.Parse(parts[0].Trim());
            float y = float.Parse(parts[1].Trim());

            // 返回像素坐标（如果输入是格数）
            return ToPx(new Vector2(x, y));
        }
        catch (Exception)
        {
            return val;
        }
    }
    #endregion

    #region 将角度差规范到 [-PI, PI]
    public static float NormAng(float rad)
    {
        while (rad > Math.PI) rad -= (float)Math.PI * 2;
        while (rad < -Math.PI) rad += (float)Math.PI * 2;
        return rad;
    }
    #endregion

    #region 工具函数：解析偏移/向量（格数转像素）
    // 解析模式参数：数量, 参数1, 参数2
    public static (int cnt, float p1, float p2) GetMode(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return (1, 0f, 0f);
        var p = args.Split(',');
        int cnt = 1;
        float p1 = 0f, p2 = 0f;
        if (p.Length >= 1) int.TryParse(p[0], out cnt);
        if (p.Length >= 2) float.TryParse(p[1], out p1);
        if (p.Length >= 3) float.TryParse(p[2], out p2);
        return (cnt, p1, p2);
    }
    #endregion

    #region 解析范围字符串，返回随机值（如果只给一个数则返回固定值）
    // 解析范围字符串，返回随机值（如果只给一个数则返回固定值）
    public static float GetRngFloat(string s, UnifiedRandom rand)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var p = s.Split(',');
        if (p.Length == 1 && float.TryParse(p[0], out float v)) return v;
        if (p.Length == 2 && float.TryParse(p[0], out float min) && float.TryParse(p[1], out float max))
            return min + (float)rand.NextDouble() * (max - min);
        return 0;
    }
    #endregion

    // 解析向量范围，返回随机 Vector2（格数，需转像素）
    #region 解析向量范围，返回随机 Vector2（格数，需转像素）
    public static Vector2 GetRngVec(string s, UnifiedRandom rand)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "0,0") return Vector2.Zero;
        var p = s.Split(',');
        if (p.Length == 2)
        {
            // "x,y" 固定值
            if (float.TryParse(p[0], out float x) && float.TryParse(p[1], out float y))
                return new Vector2(x, y);
        }
        else if (p.Length == 4)
        {
            // "xMin,xMax,yMin,yMax" 随机范围
            if (float.TryParse(p[0], out float xMin) && float.TryParse(p[1], out float xMax) &&
                float.TryParse(p[2], out float yMin) && float.TryParse(p[3], out float yMax))
            {
                float x = xMin + (float)rand.NextDouble() * (xMax - xMin);
                float y = yMin + (float)rand.NextDouble() * (yMax - yMin);
                return new Vector2(x, y);
            }
        }
        return Vector2.Zero;
    } 
    #endregion

    #region 查找最近的目标距离
    internal static float? FindTarget(Entity Entity, Vector2 Center, int Range)
    {
        if (Range <= 0) return null;

        float min = Range * 16f;
        float max = min * min;

        // 根据Entity类型决定查找的目标类型
        switch (Entity)
        {
            case NPC npcEt:
                // 查找NPC
                var npc = Main.npc
                    .Where(n => n.active && !n.friendly && n.life > 0)
                    .Where(n => n.DistanceSQ(Center) <= max)
                    .OrderBy(n => n.DistanceSQ(Center))
                    .FirstOrDefault();
                return npc?.Distance(Center);

            case Projectile projEt:
                // 查找弹幕
                var projectile = Main.projectile
                    .Where(p => p.active && !p.friendly)
                    .Where(p => p.DistanceSQ(Center) <= max)
                    .OrderBy(p => p.DistanceSQ(Center))
                    .FirstOrDefault();
                return projectile?.Distance(Center);

            case Player plrEt:
                // 查找玩家（排除自己）
                var plr = TShock.Players
                    .Where(p => p != null && p.Active && p.TPlayer.whoAmI != plrEt.whoAmI)
                    .Where(p => p.TPlayer.DistanceSQ(Center) <= max)
                    .OrderBy(p => p.TPlayer.DistanceSQ(Center))
                    .FirstOrDefault();
                return plr?.TPlayer.Distance(Center);

            case WorldItem itemEt:
                // 查找物品
                var item = Main.item
                    .Where(i => i.active)
                    .Where(i => i.DistanceSQ(Center) <= max)
                    .OrderBy(i => i.DistanceSQ(Center))
                    .FirstOrDefault();
                return item?.Distance(Center);

            // 查找物块
            case ITile tileEt:
                return FindTile(Center, Range);
            default:
                return null;
        }
    }

    private static float? FindTile(Vector2 Center, int Range)
    {
        if (Range <= 0) return null;

        int startX = (int)(Center.X / 16) - Range;
        int endX = (int)(Center.X / 16) + Range;
        int startY = (int)(Center.Y / 16) - Range;
        int endY = (int)(Center.Y / 16) + Range;

        // 确保在边界内
        startX = Math.Max(0, startX);
        endX = Math.Min(Main.maxTilesX - 1, endX);
        startY = Math.Max(0, startY);
        endY = Math.Min(Main.maxTilesY - 1, endY);

        float? Distance = null;
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                ITile tile = Main.tile[x, y];
                if (tile != null && tile.active())
                {
                    Vector2 tileCenter = new Vector2(x * 16 + 8, y * 16 + 8); // 物块中心
                    float distance = Vector2.Distance(Center, tileCenter);

                    if (!Distance.HasValue || distance < Distance.Value)
                    {
                        Distance = distance;
                    }
                }
            }
        }

        return Distance;
    }
    #endregion

    #region 查找最近的实体
    internal static Entity? FindEntity(Entity Entity, Vector2 Center, int Range)
    {
        if (Range <= 0) return null;

        float min = Range * 16f;
        float max = min * min;

        // 根据Entity类型决定查找的目标类型
        switch (Entity)
        {
            case NPC npcEt:
                // 查找NPC
                var npc = Main.npc
                    .Where(n => n.active && !n.friendly && n.life > 0)
                    .Where(n => n.DistanceSQ(Center) <= max)
                    .OrderBy(n => n.DistanceSQ(Center))
                    .FirstOrDefault();
                return npc;

            case Projectile projEt:
                // 查找弹幕
                var projectile = Main.projectile
                    .Where(p => p.active && !p.friendly)
                    .Where(p => p.DistanceSQ(Center) <= max)
                    .OrderBy(p => p.DistanceSQ(Center))
                    .FirstOrDefault();
                return projectile;

            case Player plrEt:
                // 查找玩家（排除自己）
                var plr = Main.player
                    .Where(p => p != null && p.active && p.whoAmI != plrEt.whoAmI)
                    .Where(p => p.DistanceSQ(Center) <= max)
                    .OrderBy(p => p.DistanceSQ(Center))
                    .FirstOrDefault();
                return plr;

            case WorldItem itemEt:
                // 查找物品
                var item = Main.item
                    .Where(i => i.active)
                    .Where(i => i.DistanceSQ(Center) <= max)
                    .OrderBy(i => i.DistanceSQ(Center))
                    .FirstOrDefault();
                return item;

            default:
                return null;
        }
    }
    #endregion
}