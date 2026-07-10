using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using TShockAPI;
using Terraria.Map;
using Terraria;
using TerrariaApi.Server;

namespace PixelArtPlus;

[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public override string Name => "PixelArtPlus";
    public override string Author => "Codex";
    public override string Description => "像素画生成插件 - 自动点亮地图";
    public override Version Version => new(1, 0, 0, 5);

    private readonly ColorManager _colorManager;
    private readonly TileRenderer _tileRenderer;
    private readonly ImageProcessor _imageProcessor;
    private CancellationTokenSource _currentCts;

    private string ImageDir => Path.Combine(TShock.SavePath, "PixelArtImages");

    private readonly Dictionary<string, PendingPos> _pendingPos = new();
    private class PendingPos { public string ImagePath; public SpawnOpts Opts; }

    private struct SpawnOpts
    {
        public bool UseWalls, UsePaint, EnableDither, UseLight;
        public bool ClearArea, AutoFill, GhostMode, DualMode;
        public int BlendMode, TargetW, TargetH;
        public double Brightness, Saturation, Scale;
    }

    public Plugin(Main game) : base(game)
    {
        _colorManager = new ColorManager();
        _tileRenderer = new TileRenderer();
        _imageProcessor = new ImageProcessor(_colorManager);
    }

    public override void Initialize()
    {
        _colorManager.LoadPalettes();
        Directory.CreateDirectory(ImageDir);
        GetDataHandlers.ReadNetModule.Register(this.OnNetModule);
        Commands.ChatCommands.Add(new Command("pixelartplus.use", PixelArtCmd, "pa", "pixelartplus")
        { HelpText = "像素画: /pa list | /pa wall [索引] [选项]" });
        Commands.ChatCommands.Add(new Command("pixelartplus.admin", (a) => { }, "parefresh"));
        TShock.Log.ConsoleInfo("[PixelArtPlus] v1.0.0.5 已加载");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _currentCts?.Cancel(); GetDataHandlers.ReadNetModule.UnRegister(this.OnNetModule); }
        base.Dispose(disposing);
    }

    private void OnNetModule(object? sender, GetDataHandlers.ReadNetModuleEventArgs args)
    {
        if (args.ModuleType != GetDataHandlers.NetModuleType.Ping) return;
        var plr = args.Player;
        if (plr == null || !plr.Active) return;
        PendingPos pending;
        lock (_pendingPos)
        {
            if (!_pendingPos.TryGetValue(plr.Name, out pending)) return;
            _pendingPos.Remove(plr.Name);
        }
        using var reader = new BinaryReader(args.Data);
        var pos = reader.ReadVector2();
        int cx = (int)pos.X, cy = (int)pos.Y;
        if (cx < 0 || cx >= Main.maxTilesX || cy < 0 || cy >= Main.maxTilesY)
        { plr.SendErrorMessage("[PixelArtPlus] 点击超出世界边界"); return; }
        args.Handled = true;
        plr.SendSuccessMessage($"[PixelArtPlus] 已选位置: ({cx},{cy})");
        Task.Run(() => RunGeneration(plr, pending.ImagePath, cx, cy, pending.Opts));
    }

    private void PixelArtCmd(CommandArgs args)
    {
        if (args.Parameters.Count == 0) { ShowHelp(args.Player); return; }
        switch (args.Parameters[0].ToLower())
        {
            case "list": case "l": ShowList(args.Player); break;
            case "info": case "i": ShowInfo(args.Player); break;
            case "cancel": case "c": CancelOp(args.Player); break;
            case "cancelpos": case "cp": CancelPos(args.Player); break;
            case "pos": case "p": CmdPos(args, -1); break;
            case "poswall": case "pw": CmdPos(args, -1); break;
            case "posspawn": case "ps": CmdPos(args, 0); break;
            case "posblend": case "pb": CmdPos(args, 2); break;
            case "wall": case "w": CmdDirect(args, -1); break;
            case "spawn": case "s": CmdDirect(args, 0); break;
            case "blend": case "b": CmdDirect(args, 2); break;
            case "dual": case "du": CmdDual(args); break;
            default: args.Player.SendErrorMessage("[PixelArtPlus] 未知命令"); break;
        }
    }

    private void ShowHelp(TSPlayer p)
    {
        p.SendInfoMessage("===== PixelArtPlus v1.0.0.5 =====");
        p.SendInfoMessage("/pa list (l) - 列出图片");
        p.SendInfoMessage("/pa wall [索引] [选项] (w) - 墙体(推荐,自动点亮地图)");
        p.SendInfoMessage("/pa spawn [索引] [选项] (s) - 物块");
        p.SendInfoMessage("/pa pos [索引] [选项] (p) - 地图选点后生成");
        p.SendInfoMessage("/pa cancel (c) - 取消生成");
        p.SendInfoMessage("/pa cancelpos (cp) - 取消选点");
        p.SendInfoMessage("选项: dither(d) paint(p) noclear(nc) nolight(nl) autofill(af) ghost(g) bright sat scale");
    }

    private void ShowInfo(TSPlayer p)
    {
        p.SendInfoMessage($"[PixelArtPlus] 图片目录: {ImageDir}");
        p.SendInfoMessage($"[PixelArtPlus] 调色板: {_colorManager.FlatPalette.Count} 种颜色");
    }

    private void ShowList(TSPlayer p)
    {
        var files = GetImages();
        if (files.Count == 0) { p.SendErrorMessage($"[PixelArtPlus] 未找到图片: {ImageDir}"); return; }
        p.SendInfoMessage($"=== 可用图片 ({files.Count}张) ===");
        for (int i = 0; i < files.Count; i++) p.SendInfoMessage($"  [{i}] {Path.GetFileName(files[i])}");
    }

    private void CancelOp(TSPlayer p) { _currentCts?.Cancel(); _currentCts = null; p.SendSuccessMessage("[PixelArtPlus] 已取消"); }
    private void CancelPos(TSPlayer p) { lock (_pendingPos) _pendingPos.Remove(p.Name); p.SendSuccessMessage("[PixelArtPlus] 选点已取消"); }

    private List<string> GetImages()
    {
        var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        return Directory.GetFiles(ImageDir).Where(f => exts.Contains(Path.GetExtension(f).ToLower())).OrderBy(f => f).ToList();
    }

    private bool ParseOpts(CommandArgs args, int startIdx, out SpawnOpts opts, out int idx)
    {
        opts = default; opts.UseLight = true; opts.ClearArea = true; opts.Brightness = 1.0; opts.Saturation = 1.0;
        if (!int.TryParse(args.Parameters[startIdx], out idx)) { args.Player.SendErrorMessage("[PixelArtPlus] 索引必须为数字"); return false; }
        for (int i = startIdx + 1; i < args.Parameters.Count; i++)
        {
            var o = args.Parameters[i].ToLower();
            if (o == "nl" || o == "nolight") opts.UseLight = false;
            else if (o == "nc" || o == "noclear") opts.ClearArea = false;
            else if (o == "p" || o == "paint") opts.UsePaint = true;
            else if (o == "d" || o == "dither") opts.EnableDither = true;
            else if (o == "af" || o == "autofill") opts.AutoFill = true;
            else if (o == "g" || o == "ghost") opts.GhostMode = true;
            else if (o == "dual" || o == "dl") opts.DualMode = true;
            else if (o.StartsWith("scale:")) double.TryParse(o[6..], out opts.Scale);
            else if (o.StartsWith("bright:")) double.TryParse(o[7..], out opts.Brightness);
            else if (o.StartsWith("sat:")) double.TryParse(o[4..], out opts.Saturation);
            else if (int.TryParse(o, out int n)) { if (opts.TargetW == 0) opts.TargetW = n; else if (opts.TargetH == 0) opts.TargetH = n; }
        }
        return true;
    }

    private string? ResolveImage(TSPlayer p, int idx)
    {
        var files = GetImages();
        if (idx < 0 || idx >= files.Count) { p.SendErrorMessage($"[PixelArtPlus] 无效索引 {idx}"); return null; }
        return files[idx];
    }

    private void CmdPos(CommandArgs args, int blendMode)
    {
        if (args.Parameters.Count < 2) { args.Player.SendErrorMessage("[PixelArtPlus] 用法: /pa pos [索引]"); return; }
        if (!ParseOpts(args, 1, out var opts, out int idx)) return;
        var path = ResolveImage(args.Player, idx); if (path == null) return;
        opts.UseWalls = blendMode == -1; opts.BlendMode = blendMode;
        if (opts.GhostMode && opts.BlendMode == 0) opts.BlendMode = 1;
        lock (_pendingPos) { _pendingPos[args.Player.Name] = new PendingPos { ImagePath = path, Opts = opts }; }
        args.Player.SendSuccessMessage("[PixelArtPlus] 请按M打开地图，点击目标位置");
    }

    private void CmdDirect(CommandArgs args, int blendMode)
    {
        if (args.Parameters.Count < 2) { args.Player.SendErrorMessage("[PixelArtPlus] 用法: /pa wall [索引]"); return; }
        if (!ParseOpts(args, 1, out var opts, out int idx)) return;
        var path = ResolveImage(args.Player, idx); if (path == null) return;
        opts.UseWalls = blendMode == -1; opts.BlendMode = blendMode;
        if (opts.GhostMode && opts.BlendMode == 0) opts.BlendMode = 1;
        var pos = new Point((int)(args.Player.X / 16f), (int)(args.Player.Y / 16f));
        args.Player.SendInfoMessage("[PixelArtPlus] 后台生成中...");
        Task.Run(() => RunGeneration(args.Player, path, pos.X, pos.Y, opts));
    }

    private void RunGeneration(TSPlayer player, string imagePath, int centerX, int centerY, SpawnOpts opts)
    {
        try
        {
            player.SendInfoMessage("[PixelArtPlus] 处理图片...");
            var image = _imageProcessor.Process(imagePath, opts.TargetW, opts.TargetH,
                opts.Brightness, opts.Saturation, opts.Scale,
                opts.EnableDither, false, opts.UseWalls, opts.BlendMode);
            int w = image.Width, h = image.Height;
            int startX = centerX - w / 2, startY = centerY - h / 2;
            if (startX < 0 || startX + w >= Main.maxTilesX || startY < 0 || startY + h >= Main.maxTilesY)
            { player.SendErrorMessage("[PixelArtPlus] 图像超出世界边界"); return; }
            player.SendInfoMessage($"[PixelArtPlus] 尺寸: {w}x{h} 起点: ({startX},{startY})");

            if (opts.ClearArea)
            {
                player.SendInfoMessage("[PixelArtPlus] 清除区域...");
                _tileRenderer.ClearArea(startX, startY, w, h, opts.UseWalls || opts.BlendMode == 2);
            }

            player.SendInfoMessage("[PixelArtPlus] 放置图格中...");
            int placed = 0, total = w * h, lastPct = 0;
            _tileRenderer.GenerateSync(image, startX, startY, opts.UseWalls, opts.BlendMode,
                opts.UsePaint, opts.GhostMode, false,
                onPlaced: (count) => {
                    placed = count;
                    if (total > 0) { int pct = count * 100 / total; if (pct - lastPct >= 10 || count == total) { lastPct = pct; player.SendInfoMessage($"[PixelArtPlus] {pct}% ({count}/{total})"); } }
                });

            if (opts.UseLight)
            {
                player.SendInfoMessage("[PixelArtPlus] 照明墙...");
                _tileRenderer.PlaceLightWalls(startX, startY, w, h);
            }

            if (opts.DualMode && opts.UseWalls)
            {
                player.SendInfoMessage("[PixelArtPlus] 双图层(虚化物块)...");
                var tileImg = _imageProcessor.Process(imagePath, opts.TargetW, opts.TargetH,
                    opts.Brightness, opts.Saturation, opts.Scale,
                    opts.EnableDither, false, false, 0);
                _tileRenderer.GenerateSync(tileImg, startX, startY, false, 1,
                    opts.UsePaint, true, false);
            }

            player.SendInfoMessage("[PixelArtPlus] 同步...");
            _tileRenderer.FinalSync(startX, startY, w, h);

            // Map reveal: light up the generated area on the executing player's minimap
            player.SendInfoMessage("[PixelArtPlus] 地图同步...");
            RevealMap(player, startX, startY, w, h);

            var failed = _tileRenderer.CheckFailed(image, startX, startY, opts.UseWalls);
            foreach (var id in failed) _colorManager.AddToBlacklist(id, opts.UseWalls);

            if (opts.AutoFill && failed.Count > 0)
            {
                player.SendInfoMessage("[PixelArtPlus] 自动填充...");
                Thread.Sleep(1000);
                for (int a = 0; a < 5; a++)
                {
                    int f = _tileRenderer.AutoFillEmpty(image, startX, startY, opts.UseWalls, opts.BlendMode, opts.UsePaint, _colorManager);
                    if (f == 0) break; Thread.Sleep(300);
                }
            }
            player.SendSuccessMessage($"[PixelArtPlus] 完成! 放置 {placed} 个图格");
        }
        catch (Exception ex)
        {
            player.SendErrorMessage($"[PixelArtPlus] 失败: {ex.Message}");
            TShock.Log.ConsoleError($"[PixelArtPlus] {ex}");
        }
    }

    private void RevealMap(TSPlayer player, int startX, int startY, int w, int h)
    {
        if (startX < 0 || startY < 0 || startX + w >= Main.maxTilesX || startY + h >= Main.maxTilesY) return;
        try
        {
            for (int y = startY; y < startY + h; y++)
            {
                for (int x = startX; x < startX + w; x++)
                {
                    var tile = Main.tile[x, y];
                    Color mapColor = Color.White;
                    if (tile.wall > 0) mapColor = Color.Gray;
                    MapTile mt = Main.Map[x, y];
                    mt.Light = byte.MaxValue;
                    Main.Map.SetTile(x, y, ref mt);
                }
            }
            int secW = 200, secH = 150;
            int sx = startX / secW, ex = (startX + w) / secW;
            int sy = startY / secH, ey = (startY + h) / secH;
            
            for (int ys = sy; ys <= ey; ys++)
                for (int xs = sx; xs <= ex; xs++)
                    NetMessage.SendData(58, player.Index, -1, Terraria.Localization.NetworkText.Empty, xs, ys, 1f);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[PixelArtPlus] 地图同步失败: {ex.Message}");
        }
    }

    private void CmdDual(CommandArgs args)
    {
        if (args.Parameters.Count < 2) { args.Player.SendErrorMessage("[PixelArtPlus] 用法: /pa dual [索引]"); return; }
        if (!ParseOpts(args, 1, out var opts, out int idx)) return;
        var path = ResolveImage(args.Player, idx); if (path == null) return;
        var pos = new Point((int)(args.Player.X / 16f), (int)(args.Player.Y / 16f));
        args.Player.SendInfoMessage("[PixelArtPlus] 双图层生成中...");
        Task.Run(() =>
        {
            try
            {
                args.Player.SendInfoMessage("[PixelArtPlus] 处理图像...");
                var dual = _imageProcessor.ProcessDual(path, opts.TargetW, opts.TargetH, opts.Brightness, opts.Saturation, opts.Scale);
                int w = dual.Width, h = dual.Height;
                int startX = pos.X - w / 2, startY = pos.Y - h / 2;
                if (startX < 0 || startX + w >= Main.maxTilesX || startY < 0 || startY + h >= Main.maxTilesY)
                { args.Player.SendErrorMessage("[PixelArtPlus] 超出边界"); return; }
                args.Player.SendInfoMessage($"[PixelArtPlus] 尺寸: {w}x{h}");
                if (opts.ClearArea) _tileRenderer.ClearArea(startX, startY, w, h, true);
                args.Player.SendInfoMessage("[PixelArtPlus] 墙体+虚化物块...");
                int placed = _tileRenderer.GenerateDualSync(dual, startX, startY, opts.UsePaint);
                args.Player.SendInfoMessage("[PixelArtPlus] 照明墙...");
                _tileRenderer.PlaceLightWalls(startX, startY, w, h);
                _tileRenderer.FinalSync(startX, startY, w, h);
                args.Player.SendSuccessMessage($"[PixelArtPlus] 完成! {placed} 格");
            }
            catch (Exception ex) { args.Player.SendErrorMessage($"[PixelArtPlus] 失败: {ex.Message}"); }
        });
    }

}

