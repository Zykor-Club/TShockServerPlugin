using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace PixelArtPlus;

public struct DualEntry
{
    public PaletteEntry Wall;
    public PaletteEntry Tile;
}

public class ColorManager
{
    public static readonly Dictionary<int, (byte R, byte G, byte B)> PaintColors = new()
    {
        {0, (255,255,255)}, {1, (255,0,0)}, {2, (255,127,0)}, {3, (255,255,0)},
        {4, (127,255,0)}, {5, (0,255,0)}, {6, (0,255,127)}, {7, (0,255,255)},
        {8, (0,127,255)}, {9, (0,0,255)}, {10, (127,0,255)}, {11, (255,0,255)},
        {12, (255,0,127)}, {13, (200,0,0)}, {14, (200,100,0)}, {15, (200,200,0)},
        {16, (100,200,0)}, {17, (0,200,0)}, {18, (0,200,100)}, {19, (0,200,200)},
        {20, (0,100,200)}, {21, (0,0,200)}, {22, (100,0,200)}, {23, (200,0,200)},
        {24, (200,0,100)}, {25, (75,75,75)}, {26, (255,255,255)}, {27, (175,175,175)},
        {28, (255,178,125)}, {30, (0,0,0)}, {31, (255,255,255)},
    };

    private static readonly HashSet<int> GravityTiles = new() {
        TileID.Sand, TileID.Ebonsand, TileID.Crimsand, TileID.Pearlsand,
        TileID.Silt, TileID.Slush, TileID.DesertFossil,
    };
    private static readonly HashSet<int> TileBlacklist = new() { 224, 495, 234, 112, 123, 199, 181, 633, 23, 183, 662, 661, 424, 484 };
    private static readonly HashSet<int> WallBlacklist = new();
    private static readonly string BlacklistFilePath;
    private static readonly string PluginDataDir;

    // LUTs for single-layer matching
    private PaletteEntry?[,,] _tileLut = new PaletteEntry?[16, 16, 16];
    private PaletteEntry?[,,] _wallLut = new PaletteEntry?[16, 16, 16];
    // LUT for dual-layer matching (wall + ghost block)
    private DualEntry?[,,] _dualLut = new DualEntry?[16, 16, 16];

    public Dictionary<int, Dictionary<int, (byte R, byte G, byte B)>> TilePalette { get; } = new();
    public Dictionary<int, Dictionary<int, (byte R, byte G, byte B)>> WallPalette { get; } = new();
    public List<PaletteEntry> FlatPalette { get; } = new();
    public bool IsReady { get; private set; }

    static ColorManager()
    {
        PluginDataDir = Path.Combine(TShock.SavePath, "PixelArtPlus");
        BlacklistFilePath = Path.Combine(PluginDataDir, "blacklist.json");
        Directory.CreateDirectory(PluginDataDir);
    }
    public ColorManager() { LoadBlacklist(); }

    public void LoadPalettes()
    {
        try
        {
            LoadComprehensivePalette();
            LoadFallbackPalette("map_colors.json", false);
            LoadFallbackPalette("texture_colors.json", true);
            BuildFlatPalette();
            BuildAllLUTs();
            IsReady = true;
            TShock.Log.ConsoleInfo($"[PixelArtPlus] 调色板: {TilePalette.Count}物块, {WallPalette.Count}墙体, {FlatPalette.Count}组合");
        }
        catch (Exception ex) { TShock.Log.ConsoleError($"[PixelArtPlus] {ex.Message}"); }
    }

    public void BuildAllLUTs()
    {
        TShock.Log.ConsoleInfo("[PixelArtPlus] 构建LUT...");
        for (int ri = 0; ri < 16; ri++)
        {
            for (int gi = 0; gi < 16; gi++)
            {
                for (int bi = 0; bi < 16; bi++)
                {
                    byte r = (byte)(ri * 16 + 8), g = (byte)(gi * 16 + 8), b = (byte)(bi * 16 + 8);

                    // Single-layer LUTs
                    _tileLut[ri, gi, bi] = FindClosest(r, g, b, false);
                    _wallLut[ri, gi, bi] = FindClosest(r, g, b, true);

                    // Dual-layer: wall + ghost block combined
                    // combined = 0.6 * wallColor + 0.4 * tileColor
                    // So: tileColor = (target - 0.6 * wallColor) / 0.4
                    var wall = _wallLut[ri, gi, bi];
                    if (wall != null)
                    {
                        int needR = (int)((r - 0.6 * wall.Value.R) / 0.4);
                        int needG = (int)((g - 0.6 * wall.Value.G) / 0.4);
                        int needB = (int)((b - 0.6 * wall.Value.B) / 0.4);
                        var tile = FindClosest(
                            (byte)Math.Clamp(needR, 0, 255),
                            (byte)Math.Clamp(needG, 0, 255),
                            (byte)Math.Clamp(needB, 0, 255), false);
                        if (tile != null)
                            _dualLut[ri, gi, bi] = new DualEntry { Wall = wall.Value, Tile = tile.Value };
                    }
                }
            }
        }
        TShock.Log.ConsoleInfo("[PixelArtPlus] LUT完成(含双图层)");
    }

    /// <summary>Fast single-layer match with 27-cell refinement.</summary>
    public PaletteEntry? FastMatch(byte r, byte g, byte b, bool preferWall)
    {
        var lut = preferWall ? _wallLut : _tileLut;
        int ri = Math.Clamp(r / 16, 0, 15), gi = Math.Clamp(g / 16, 0, 15), bi = Math.Clamp(b / 16, 0, 15);
        PaletteEntry? best = null;
        double bestDist = double.MaxValue;
        int mdRi = Math.Max(0, ri - 1), mxRi = Math.Min(15, ri + 1);
        int mdGi = Math.Max(0, gi - 1), mxGi = Math.Min(15, gi + 1);
        int mdBi = Math.Max(0, bi - 1), mxBi = Math.Min(15, bi + 1);
        for (int ni = mdRi; ni <= mxRi; ni++)
            for (int nj = mdGi; nj <= mxGi; nj++)
                for (int nk = mdBi; nk <= mxBi; nk++)
                {
                    var e = lut[ni, nj, nk];
                    if (e == null) continue;
                    double d = ColorDistance(r, g, b, e.Value.R, e.Value.G, e.Value.B);
                    if (d < bestDist) { bestDist = d; best = e; }
                }
        return best;
    }

    /// <summary>Fast dual-layer match with 27-cell refinement. Returns (wall, tile) pair.</summary>
    public DualEntry? FastDualMatch(byte r, byte g, byte b)
    {
        int ri = Math.Clamp(r / 16, 0, 15), gi = Math.Clamp(g / 16, 0, 15), bi = Math.Clamp(b / 16, 0, 15);
        DualEntry? best = null;
        double bestDist = double.MaxValue;
        int mdRi = Math.Max(0, ri - 1), mxRi = Math.Min(15, ri + 1);
        int mdGi = Math.Max(0, gi - 1), mxGi = Math.Min(15, gi + 1);
        int mdBi = Math.Max(0, bi - 1), mxBi = Math.Min(15, bi + 1);
        for (int ni = mdRi; ni <= mxRi; ni++)
            for (int nj = mdGi; nj <= mxGi; nj++)
                for (int nk = mdBi; nk <= mxBi; nk++)
                {
                    var e = _dualLut[ni, nj, nk];
                    if (e == null) continue;
                    byte cr = (byte)(0.6 * e.Value.Wall.R + 0.4 * e.Value.Tile.R);
                    byte cg = (byte)(0.6 * e.Value.Wall.G + 0.4 * e.Value.Tile.G);
                    byte cb = (byte)(0.6 * e.Value.Wall.B + 0.4 * e.Value.Tile.B);
                    double d = ColorDistance(r, g, b, cr, cg, cb);
                    if (d < bestDist) { bestDist = d; best = e; }
                }
        return best;
    }

    /// <summary>Blend match: picks best of ghost tile vs wall.</summary>
    public PaletteEntry? FastBlendMatch(byte r, byte g, byte b)
    {
        var tile = FastMatch(r, g, b, false);
        var wall = FastMatch(r, g, b, true);
        if (tile == null) return wall;
        if (wall == null) return tile;
        byte tr = (byte)Math.Min(255, r * 2.5);
        byte tg = (byte)Math.Min(255, g * 2.5);
        byte tb = (byte)Math.Min(255, b * 2.5);
        double td = ColorDistance(tr, tg, tb, tile.Value.R, tile.Value.G, tile.Value.B) + 500;
        double wd = ColorDistance(r, g, b, wall.Value.R, wall.Value.G, wall.Value.B);
        return td < wd ? tile : wall;
    }

    private void LoadComprehensivePalette()
    {
        var asm = typeof(ColorManager).Assembly;
        using var s = asm.GetManifestResourceStream("palette_complete.json");
        if (s == null) return;
        using var r = new StreamReader(s);
        var doc = JsonDocument.Parse(r.ReadToEnd());
        var root = doc.RootElement;
        if (root.TryGetProperty("tiles", out var t))
            foreach (var tile in t.EnumerateObject())
            {
                int id = int.Parse(tile.Name);
                var paints = new Dictionary<int, (byte, byte, byte)>();
                foreach (var p in tile.Value.EnumerateObject())
                {
                    int pid = int.Parse(p.Name);
                    var arr = p.Value.EnumerateArray().Select(e => e.GetByte()).ToArray();
                    if (arr.Length >= 3) paints[pid] = (arr[0], arr[1], arr[2]);
                }
                TilePalette[id] = paints;
            }
        if (root.TryGetProperty("walls", out var w))
            foreach (var wall in w.EnumerateObject())
            {
                int id = int.Parse(wall.Name);
                var paints = new Dictionary<int, (byte, byte, byte)>();
                foreach (var p in wall.Value.EnumerateObject())
                {
                    int pid = int.Parse(p.Name);
                    var arr = p.Value.EnumerateArray().Select(e => e.GetByte()).ToArray();
                    if (arr.Length >= 3) paints[pid] = (arr[0], arr[1], arr[2]);
                }
                WallPalette[id] = paints;
            }
    }

    private void LoadFallbackPalette(string name, bool tex)
    {
        var asm = typeof(ColorManager).Assembly;
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return;
        using var r = new StreamReader(s);
        var json = r.ReadToEnd();
        if (!tex)
        {
            var data = JsonSerializer.Deserialize<ColorData>(json);
            if (data == null) return;
            foreach (var kv in data.Tiles) if (!TilePalette.ContainsKey(kv.Value.TileId)) TilePalette[kv.Value.TileId] = new() { {0, ((byte)kv.Value.R, (byte)kv.Value.G, (byte)kv.Value.B)} };
            foreach (var kv in data.Walls) if (!WallPalette.ContainsKey(kv.Value.WallId)) WallPalette[kv.Value.WallId] = new() { {0, ((byte)kv.Value.R, (byte)kv.Value.G, (byte)kv.Value.B)} };
        }
        else
        {
            var data = JsonSerializer.Deserialize<TextureColorData>(json);
            if (data == null) return;
            foreach (var kv in data.Tiles) { if (kv.Value.CanPlace && !TilePalette.ContainsKey(kv.Key)) TilePalette[kv.Key] = new() { {0, (kv.Value.R, kv.Value.G, kv.Value.B)} }; }
            foreach (var kv in data.Walls) { if (!WallPalette.ContainsKey(kv.Key)) WallPalette[kv.Key] = new() { {0, (kv.Value.R, kv.Value.G, kv.Value.B)} }; }
        }
    }

    private void BuildFlatPalette()
    {
        FlatPalette.Clear();
        foreach (var (id, ps) in TilePalette) foreach (var (pid, rgb) in ps) FlatPalette.Add(new PaletteEntry(id, pid, rgb.Item1, rgb.Item2, rgb.Item3, false));
        foreach (var (id, ps) in WallPalette) foreach (var (pid, rgb) in ps) FlatPalette.Add(new PaletteEntry(id, pid, rgb.Item1, rgb.Item2, rgb.Item3, true));
    }

    public static double ColorDistance(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        double avgR = (r1 + r2) / 2.0;
        double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return (2.0 + avgR / 256.0) * dr * dr + 4.0 * dg * dg + (2.0 + (255.0 - avgR) / 256.0) * db * db;
    }

    public PaletteEntry? FindClosest(byte r, byte g, byte b, bool preferWalls = false)
    {
        PaletteEntry? best = null;
        double bestDist = double.MaxValue;
        foreach (var e in FlatPalette)
        {
            if (e.IsWall && !preferWalls) continue;
            if (!e.IsWall && preferWalls) continue;
            if (e.IsWall && WallBlacklist.Contains(e.TypeId)) continue;
            if (!e.IsWall && (TileBlacklist.Contains(e.TypeId) || !CanPlaceTile(e.TypeId))) continue;
            double d = ColorDistance(r, g, b, e.R, e.G, e.B);
            if (d < bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    public static bool CanPlaceTile(int id)
    {
        if (id < 0 || id >= TileID.Count) return false;
        if (TileBlacklist.Contains(id)) return false;
        if (!Main.tileSolid[id] && !TileID.Sets.Platforms[id]) return false;
        if (TileID.Sets.IsVine[id] || TileID.Sets.IsATreeTrunk[id] || TileID.Sets.CommonSapling[id]) return false;
        if (Main.tileFrameImportant[id]) return false;
        if (TileID.Sets.IsLivingFire[id]) return false;
        if (GravityTiles.Contains(id)) return false;
        return true;
    }

    public void AddToBlacklist(int id, bool isWall) { if (isWall) WallBlacklist.Add(id); else TileBlacklist.Add(id); SaveBlacklist(); }

    private void LoadBlacklist()
    {
        try
        {
            var f = BlacklistFilePath;
            if (File.Exists(f))
            {
                var d = JsonSerializer.Deserialize<BlData>(File.ReadAllText(f));
                if (d == null) return;
                foreach (var t in d.Tiles) TileBlacklist.Add(t);
                foreach (var w in d.Walls) WallBlacklist.Add(w);
            }
        }
        catch { }
    }
    private void SaveBlacklist()
    {
        try
        {
            var d = new BlData { Tiles = TileBlacklist.ToList(), Walls = WallBlacklist.ToList() };
            File.WriteAllText(BlacklistFilePath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
    private class BlData { public List<int> Tiles { get; set; } = new(); public List<int> Walls { get; set; } = new(); }
}
