using System.Diagnostics;
using Microsoft.Xna.Framework;
using OTAPI;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace MidiPlayer.Core;

/// <summary>
/// 八音盒绑定功能：
/// - 绑定：管理员 /midi bind 后右键八音盒，将 2×2 左上角坐标 + WorldId 写入数据库。
/// - 联动：全服模式下，绑定盒打开 → 全服恢复播放；关闭 → 全服暂停。
/// - 防摧毁：绑定盒不可被挖掘/替换/爆炸破坏。
/// 切换世界后按 WorldId 自动恢复对应世界的绑定。
/// </summary>
public class MusicBoxBinding
{
    public static readonly MusicBoxBinding Instance = new();

    private const byte TileChangePacketId = 20;
    private const long BindTimeoutMs = 30_000; // 绑定模式最长等待时间，超时自动退出

    /// <summary>当前世界绑定的八音盒左上角坐标（null = 未绑定）。</summary>
    public PointBinding? Bound { get; private set; }

    /// <summary>当前是否处于绑定模式（等待管理员右键八音盒）。</summary>
    public bool IsInBindMode => _pendingBindPlayer >= 0;

    private volatile int _pendingBindPlayer = -1;
    private long _pendingBindStartTick;
    private int _currentWorldId;

    private MusicBoxBinding() { }

    // ==================== 生命周期 ====================

    public void Initialize()
    {
        MusicBoxDatabase.Initialize();
        ReloadBindingForCurrentWorld();

        Hooks.MessageBuffer.GetData += OnGetData;
        HookEvents.Terraria.WorldGen.SwitchMB += OnSwitchMB;
        HookEvents.Terraria.WorldGen.KillTile += OnKillTile;
    }

    public void Dispose()
    {
        Hooks.MessageBuffer.GetData -= OnGetData;
        HookEvents.Terraria.WorldGen.SwitchMB -= OnSwitchMB;
        HookEvents.Terraria.WorldGen.KillTile -= OnKillTile;
    }

    /// <summary>世界加载/切换时调用：按当前世界恢复绑定并应用开关状态。</summary>
    public void OnWorldLoad()
    {
        ReloadBindingForCurrentWorld();
    }

    private void ReloadBindingForCurrentWorld()
    {
        _currentWorldId = Main.ActiveWorldFileData?.WorldId ?? 0;
        _pendingBindPlayer = -1;
        Bound = MusicBoxDatabase.GetBinding(_currentWorldId);
        ApplyCurrentState();
    }

    /// <summary>
    /// 生成/切回全服模式后，按绑定盒当前开关状态同步全服强制暂停/恢复。
    /// </summary>
    public void ApplyCurrentState()
    {
        if (Bound == null)
            return;

        if (Main.tile == null)
            return;

        var tile = Main.tile[Bound.X, Bound.Y];
        if (tile == null)
            return;

        SetForcePaused(tile.frameX < 36);
    }

    /// <summary>绑定模式超时自动退出（由 GameUpdate 每帧调用）。</summary>
    public void CheckBindTimeout()
    {
        if (_pendingBindPlayer < 0)
            return;

        long elapsed = (long)((Stopwatch.GetTimestamp() - _pendingBindStartTick) * TickToMs);
        if (elapsed < BindTimeoutMs)
            return;

        _pendingBindPlayer = -1;
    }

    private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

    // ==================== 绑定命令接口 ====================

    /// <summary>进入绑定模式。返回是否成功。</summary>
    public bool EnterBindMode(TSPlayer player)
    {
        if (!player.HasPermission("midiplayer.admin"))
            return false;

        _pendingBindPlayer = player.Index;
        _pendingBindStartTick = Stopwatch.GetTimestamp();
        return true;
    }

    /// <summary>取消当前玩家的绑定模式。</summary>
    public void CancelBindMode(TSPlayer player)
    {
        if (_pendingBindPlayer == player.Index)
            _pendingBindPlayer = -1;
    }

    /// <summary>解除当前世界绑定。返回是否成功。</summary>
    public bool UnbindCurrentWorld(TSPlayer player)
    {
        if (!player.HasPermission("midiplayer.admin"))
            return false;
        if (Bound == null)
            return false;

        _pendingBindPlayer = -1;
        Bound = null;
        MusicBoxDatabase.DeleteBinding(_currentWorldId);

        // 解除绑定后，八音盒不再联动，清除可能残留的强制暂停状态
        PlayScheduler.Instance.ForceResumeGlobal();
        return true;
    }

    // ==================== 状态联动 ====================

    private static void SetForcePaused(bool paused)
    {
        if (paused)
            PlayScheduler.Instance.ForcePauseGlobal();
        else
            PlayScheduler.Instance.ForceResumeGlobal();
    }

    // ==================== 数据包：八音盒开关（远程玩家）/ 绑定 / 防挖掘 ====================
    // 远程玩家右键八音盒时，客户端发送 TileChange(20) 数据包到服务器，
    // 服务器「原样应用」frameX，不会调用 SwitchMB。因此需在此解析该数据包。
    private void OnGetData(object? sender, Hooks.MessageBuffer.GetDataEventArgs e)
    {
        if (e.PacketId != TileChangePacketId)
            return;

        // 短路：既无绑定模式、也无绑定盒时，本处理器必然空转，无需解析任何 TileChange(20) 包
        if (_pendingBindPlayer < 0 && Bound == null)
            return;

        var tiles = ParseTileChange(e.Instance.readBuffer, e.Start);
        if (tiles == null || tiles.Count == 0)
            return;

        int whoAmI = e.Instance.whoAmI;
        bool cancelForDestroy = false;
        (PointBinding Box, short FrameX)? matched = null;

        foreach (var tile in tiles)
        {
            if (tile.Type != TileID.MusicBoxes)
                continue;

            int topLeftX = tile.X - ((tile.FrameX / 18) % 2);
            int topLeftY = tile.Y - ((tile.FrameY / 18) % 2);
            var topLeft = new PointBinding(topLeftX, topLeftY);

            // 防挖掘：绑定盒被移除（active=false）→ 拒绝该数据包并回传图格
            if (!tile.Active && Bound != null && Bound == topLeft)
            {
                cancelForDestroy = true;
                continue;
            }

            // 绑定模式：命中管理员右键 → 记录绑定
            if (_pendingBindPlayer == whoAmI)
            {
                _pendingBindPlayer = -1;
                Bound = new PointBinding(topLeftX, topLeftY);
                MusicBoxDatabase.SaveBinding(_currentWorldId, topLeftX, topLeftY);

                TShock.Players[whoAmI]?.SendMessage(
                    $"{UiHelper.Prefix()} [c/90EE90:八音盒已绑定] [c/FFFFAA:坐标] [c/FFD700:({topLeftX}, {topLeftY})] [c/AAAAAA:全服播放将随此盒开关联动]",
                    Color.White);
                return;
            }

            // 联动：仅全服模式且命中绑定盒
            if (PlayScheduler.Instance.IsGlobalMode && Bound != null && Bound == topLeft)
                matched = (topLeft, tile.FrameX);
        }

        if (matched.HasValue)
            SetForcePaused(matched.Value.FrameX < 36);

        if (cancelForDestroy)
        {
            e.Result = HookResult.Cancel;
            if (Bound != null)
                NetMessage.SendTileSquare(whoAmI, Bound.X, Bound.Y, 2, 2);
        }
    }

    // ==================== 电路触发：绑定盒被电线开关 ====================
    // SwitchMB Hook 在切换【前】触发，读旧态判方向。
    private void OnSwitchMB(object? sender, HookEvents.Terraria.WorldGen.SwitchMBEventArgs e)
    {
        if (Bound == null || !PlayScheduler.Instance.IsGlobalMode)
            return;

        var tile = Main.tile[e.i, e.j];
        if (tile == null || tile.type != TileID.MusicBoxes)
            return;

        int topLeftX = e.i - ((tile.frameX / 18) % 2);
        int topLeftY = e.j - ((tile.frameY / 18) % 2);
        if (Bound.X != topLeftX || Bound.Y != topLeftY)
            return;

        // 旧态：开→即将关→暂停；关→即将开→恢复
        SetForcePaused(tile.frameX >= 36);
    }

    // ==================== 防摧毁：阻挡 KillTile（挖掘 + 爆炸等一切破坏） ====================
    private void OnKillTile(object? sender, HookEvents.Terraria.WorldGen.KillTileEventArgs e)
    {
        if (Bound == null)
            return;

        // 命中绑定盒 2×2 区域内任意一格 → 禁止该格被破坏
        if (e.i >= Bound.X && e.i <= Bound.X + 1
            && e.j >= Bound.Y && e.j <= Bound.Y + 1)
        {
            e.ContinueExecution = false;
        }
    }

    // ==================== TileChange(20) 解析 ====================

    private static List<TileChangeTile>? ParseTileChange(byte[] buffer, int start)
    {
        if (buffer.Length - start < 8)
            return null;
        if (buffer[start] != TileChangePacketId)
            return null;

        int originX = BitConverter.ToInt16(buffer, start + 1);
        int originY = BitConverter.ToInt16(buffer, start + 3);
        int width = buffer[start + 5];
        int height = buffer[start + 6];

        if (width <= 0 || height <= 0 || width > 50 || height > 50)
            return null;

        int pos = start + 8;
        var tiles = new List<TileChangeTile>(width * height);

        for (int ty = 0; ty < height; ty++)
        {
            for (int tx = 0; tx < width; tx++)
            {
                if (pos + 3 > buffer.Length)
                    break;

                byte flags1 = buffer[pos++];
                byte flags2 = buffer[pos++];
                byte flags3 = buffer[pos++];

                if ((flags2 & 0x04) != 0) { if (pos >= buffer.Length) break; pos++; } // tile.color
                if ((flags2 & 0x08) != 0) { if (pos >= buffer.Length) break; pos++; } // tile.wallColor

                bool active = (flags1 & 0x01) != 0;
                ushort type = 0;
                short frameX = 0;
                short frameY = 0;

                if (active)
                {
                    if (pos + 2 > buffer.Length)
                        break;
                    type = BitConverter.ToUInt16(buffer, pos);
                    pos += 2;

                    if (Main.tileFrameImportant[type])
                    {
                        if (pos + 4 > buffer.Length)
                            break;
                        frameX = BitConverter.ToInt16(buffer, pos);
                        pos += 2;
                        frameY = BitConverter.ToInt16(buffer, pos);
                        pos += 2;
                    }
                }

                tiles.Add(new TileChangeTile(originX + tx, originY + ty, type, frameX, frameY, active));
            }
        }

        return tiles.Count > 0 ? tiles : null;
    }

    private readonly record struct TileChangeTile(int X, int Y, ushort Type, short FrameX, short FrameY, bool Active);
}