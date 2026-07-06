using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Terraria;
using TShockAPI;

namespace PixelArtPlus;

public class TileRenderer
{
    private const int DiamondGemsparkWall = 155;
    private const int SyncInterval = 99999;

    public TileRenderer() { }

    public int GenerateSync(ProcessedImage image, int startX, int startY,
        bool useWalls, int blendMode, bool usePaint, bool ghostMode, bool dualMode,
        Action<int> onPlaced = null)
    {
        int placed = 0, w = image.Width, h = image.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var entry = image.Pixels[y * w + x];
                if (entry == null) continue;
                int wx = startX + x, wy = startY + y;
                if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;

                if (entry.Value.IsWall)
                    PlaceWallFast(wx, wy, entry.Value.TypeId, entry.Value.PaintId, usePaint);
                else
                    PlaceTileFast(wx, wy, entry.Value.TypeId, entry.Value.PaintId, usePaint, ghostMode || blendMode == 1 || blendMode == 2);
                placed++;
                if (placed % SyncInterval == 0) NetMessage.SendTileSquare(-1, wx, wy, 1);
            }
            onPlaced?.Invoke(placed);
        }
        return placed;
    }

    /// <summary>Dual-layer generation: place wall + ghost block at each position for richer colors.</summary>
    public int GenerateDualSync(ProcessedDualImage image, int startX, int startY,
        bool usePaint, Action<int> onPlaced = null)
    {
        int placed = 0, w = image.Width, h = image.Height;
        int progress = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var entry = image.Pixels[y * w + x];
                if (entry == null) continue;
                int wx = startX + x, wy = startY + y;
                if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;

                // Place wall (background layer)
                PlaceWallFast(wx, wy, entry.Value.Wall.TypeId, entry.Value.Wall.PaintId, usePaint);
                // Place ghost block (foreground at 40% opacity, shows wall behind)
                PlaceTileFast(wx, wy, entry.Value.Tile.TypeId, entry.Value.Tile.PaintId, usePaint, ghostMode: true);
                placed++;
                if (placed % SyncInterval == 0) NetMessage.SendTileSquare(-1, wx, wy, 1);
            }
            progress += w;
            onPlaced?.Invoke(placed);
        }
        return placed;
    }

    public void PlaceTileFast(int x, int y, int tileId, int paintId, bool usePaint, bool ghostMode)
    {
        var tile = Main.tile[x, y];
        tile.active(true);
        tile.type = (ushort)tileId;
        tile.frameX = -1; tile.frameY = -1;
        if (ghostMode) { tile.actuator(true); tile.inActive(true); }
        if (usePaint && paintId != 0) WorldGen.paintTile(x, y, (byte)paintId);
    }

    public void PlaceWallFast(int x, int y, int wallId, int paintId, bool usePaint)
    {
        Main.tile[x, y].wall = (ushort)wallId;
        if (usePaint && paintId != 0) WorldGen.paintWall(x, y, (byte)paintId);
    }

    public void ClearArea(int startX, int startY, int width, int height, bool clearWalls = false)
    {
        for (int i = 0; i < height; i++)
            for (int j = 0; j < width; j++)
            {
                int wx = startX + j, wy = startY + i;
                if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;
                var tile = Main.tile[wx, wy];
                tile.active(false); tile.type = 0; tile.frameX = 0; tile.frameY = 0;
                tile.color(0); tile.actuator(false); tile.inActive(false);
                if (clearWalls) tile.wall = 0;
                tile.wallColor(0); tile.liquid = 0; tile.liquidType(0);
            }
    }

    public void PlaceLightWalls(int startX, int startY, int width, int height)
    {
        for (int i = 0; i < height; i++)
            for (int j = 0; j < width; j++)
            {
                int wx = startX + j, wy = startY + i;
                if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;
                var t = Main.tile[wx, wy];
                if (t.active() && t.wall == 0) t.wall = DiamondGemsparkWall;
            }
    }

    public void FinalSync(int startX, int startY, int width, int height)
    {
        int c = 50;
        for (int y = 0; y < height; y += c)
            for (int x = 0; x < width; x += c)
                NetMessage.SendTileSquare(-1, startX + x, startY + y, Math.Min(c, width - x), Math.Min(c, height - y));
        for (int i = 0; i < TShock.Players.Length; i++)
            if (TShock.Players[i]?.Active == true)
                for (int j = 0; j < Main.maxSectionsX; j++)
                    for (int k = 0; k < Main.maxSectionsY; k++)
                        Netplay.Clients[i].TileSections[j, k] = false;
    }

    public HashSet<int> CheckFailed(ProcessedImage image, int startX, int startY, bool useWalls)
    {
        var f = new HashSet<int>(); int w = image.Width, h = image.Height;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int wx = startX + x, wy = startY + y; if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;
            var tile = Main.tile[wx, wy]; var entry = image.Pixels[y * w + x]; if (entry == null) continue;
            if (useWalls || entry.Value.IsWall) { if (tile.wall == 0) f.Add(entry.Value.TypeId); }
            else { if (!tile.active()) f.Add(entry.Value.TypeId); }
        }
        return f;
    }

    public int AutoFillEmpty(ProcessedImage image, int startX, int startY, bool useWalls, int blendMode, bool usePaint, ColorManager cm)
    {
        int filled = 0, w = image.Width, h = image.Height;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int wx = startX + x, wy = startY + y; if (wx < 0 || wx >= Main.maxTilesX || wy < 0 || wy >= Main.maxTilesY) continue;
            var tile = Main.tile[wx, wy]; if (tile.active() || tile.wall != 0) continue;
            var dom = GetDominant(image, x, y); if (dom == null) continue;
            var match = (useWalls || blendMode == 2) ? cm.FindClosest(dom.Value.Item1, dom.Value.Item2, dom.Value.Item3, true) : cm.FindClosest(dom.Value.Item1, dom.Value.Item2, dom.Value.Item3, false);
            if (match == null) continue;
            if (match.Value.IsWall) PlaceWallFast(wx, wy, match.Value.TypeId, match.Value.PaintId, usePaint);
            else PlaceTileFast(wx, wy, match.Value.TypeId, match.Value.PaintId, usePaint, blendMode == 1);
            NetMessage.SendTileSquare(-1, wx, wy, 1); filled++;
        }
        return filled;
    }

    private (byte, byte, byte)? GetDominant(ProcessedImage image, int lx, int ly)
    {
        int w = image.Width, h = image.Height;
        var b = new Dictionary<int, List<(byte, byte, byte)>>();
        foreach (int r in new[] { 1, 2, 3, 5, 8 })
        {
            for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = lx + dx, ny = ly + dy; if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                var e = image.Pixels[ny * w + nx]; if (e == null) continue;
                int k = (e.Value.R / 16) << 8 | (e.Value.G / 16) << 4 | (e.Value.B / 16);
                if (!b.ContainsKey(k)) b[k] = new(); b[k].Add((e.Value.R, e.Value.G, e.Value.B));
            }
            if (b.Count > 0) break;
        }
        if (b.Count == 0) return null;
        var d = b.OrderByDescending(kv => kv.Value.Count).First().Value;
        return ((byte)(d.Sum(c => (int)c.Item1) / d.Count), (byte)(d.Sum(c => (int)c.Item2) / d.Count), (byte)(d.Sum(c => (int)c.Item3) / d.Count));
    }
}
