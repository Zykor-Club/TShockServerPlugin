using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PixelArtPlus;

public class ImageProcessor
{
    private readonly ColorManager _colorManager;

    public ImageProcessor(ColorManager colorManager) { _colorManager = colorManager; }

    public ProcessedImage Process(string imagePath, int targetWidth, int targetHeight,
        double brightness = 1.0, double saturation = 1.0, double scale = 0.0,
        bool enableDither = false, bool useTextureMode = false, bool useWalls = false,
        int blendMode = 0)
    {
        using var image = Image.Load<Rgba32>(imagePath);
        if (scale > 0.0) { targetWidth = (int)(image.Width * scale); targetHeight = (int)(image.Height * scale); }
        else if (targetWidth <= 0 && targetHeight <= 0) { targetWidth = image.Width; targetHeight = image.Height; }
        else if (targetWidth <= 0) targetWidth = (int)((double)image.Width / image.Height * targetHeight);
        else if (targetHeight <= 0) targetHeight = (int)((double)image.Height / image.Width * targetWidth);
        bool oversized = targetWidth > 1000 || targetHeight > 1000;
        int origW = targetWidth, origH = targetHeight;
        image.Mutate(ctx =>
        {
            ctx.Resize(targetWidth, targetHeight);
            if (oversized) { targetWidth = Math.Min(targetWidth, 1000); targetHeight = Math.Min(targetHeight, 1000); ctx.Crop(new Rectangle((origW-targetWidth)/2, (origH-targetHeight)/2, targetWidth, targetHeight)); }
            if (Math.Abs(brightness - 1.0) > 0.001) ctx.Brightness((float)brightness);
            else if (useTextureMode && !useWalls && blendMode != 2) ctx.Brightness(1.3f);
            if (Math.Abs(saturation - 1.0) > 0.001) ctx.Saturate((float)saturation);
        });
        var pixels = new Rgba32[targetWidth * targetHeight];
        image.CopyPixelDataTo(pixels);
        if (enableDither) return DitherAndMatch(pixels, targetWidth, targetHeight, useWalls, blendMode);
        else return DirectMatch(pixels, targetWidth, targetHeight, useWalls, blendMode);
    }

    public ProcessedDualImage ProcessDual(string imagePath, int targetWidth, int targetHeight,
        double brightness = 1.0, double saturation = 1.0, double scale = 0.0)
    {
        using var image = Image.Load<Rgba32>(imagePath);
        if (scale > 0.0) { targetWidth = (int)(image.Width * scale); targetHeight = (int)(image.Height * scale); }
        else if (targetWidth <= 0 && targetHeight <= 0) { targetWidth = image.Width; targetHeight = image.Height; }
        else if (targetWidth <= 0) targetWidth = (int)((double)image.Width / image.Height * targetHeight);
        else if (targetHeight <= 0) targetHeight = (int)((double)image.Height / image.Width * targetWidth);
        bool oversized = targetWidth > 1000 || targetHeight > 1000;
        int origW = targetWidth, origH = targetHeight;
        image.Mutate(ctx => { ctx.Resize(targetWidth, targetHeight);
            if (oversized) { targetWidth = Math.Min(targetWidth, 1000); targetHeight = Math.Min(targetHeight, 1000); ctx.Crop(new Rectangle((origW-targetWidth)/2, (origH-targetHeight)/2, targetWidth, targetHeight)); }
            if (Math.Abs(brightness - 1.0) > 0.001) ctx.Brightness((float)brightness);
            if (Math.Abs(saturation - 1.0) > 0.001) ctx.Saturate((float)saturation);
        });
        var pixels = new Rgba32[targetWidth * targetHeight];
        image.CopyPixelDataTo(pixels);
        var result = new DualEntry?[targetWidth * targetHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p.A < 128) continue;
            result[i] = _colorManager.FastDualMatch(p.R, p.G, p.B);
        }
        return new ProcessedDualImage { Width = targetWidth, Height = targetHeight, Pixels = result };
    }

    private ProcessedImage DirectMatch(Rgba32[] pixels, int w, int h, bool useWalls, int blendMode)
    {
        var result = new PaletteEntry?[w * h];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p.A < 128) continue;
            result[i] = blendMode == 2 ? _colorManager.FastBlendMatch(p.R, p.G, p.B) : _colorManager.FastMatch(p.R, p.G, p.B, useWalls);
        }
        return new ProcessedImage { Width = w, Height = h, Pixels = result };
    }

    private ProcessedImage DitherAndMatch(Rgba32[] pixels, int w, int h, bool useWalls, int blendMode)
    {
        var result = new PaletteEntry?[w * h];
        var rBuf = new float[w * h]; var gBuf = new float[w * h]; var bBuf = new float[w * h];
        for (int i = 0; i < pixels.Length; i++) { rBuf[i] = pixels[i].R; gBuf[i] = pixels[i].G; bBuf[i] = pixels[i].B; }
        for (int y = 0; y < h; y++)
        {
            int startX = (y % 2 == 0) ? 0 : w - 1, endX = (y % 2 == 0) ? w : -1, stepX = (y % 2 == 0) ? 1 : -1;
            for (int x = startX; x != endX; x += stepX)
            {
                int idx = y * w + x;
                float rVal = Math.Clamp(rBuf[idx], 0, 255), gVal = Math.Clamp(gBuf[idx], 0, 255), bVal = Math.Clamp(bBuf[idx], 0, 255);
                var match = blendMode == 2 ? _colorManager.FastBlendMatch((byte)rVal, (byte)gVal, (byte)bVal) : _colorManager.FastMatch((byte)rVal, (byte)gVal, (byte)bVal, useWalls);
                result[idx] = match;
                if (match.HasValue)
                {
                    float er = rVal - match.Value.R, eg = gVal - match.Value.G, eb = bVal - match.Value.B;
                    void AddError(int dx, int dy, float wr, float wg, float wb) { int nx = x + dx, ny = y + dy; if (nx >= 0 && nx < w && ny >= 0 && ny < h) { int nidx = ny * w + nx; rBuf[nidx] += er * wr; gBuf[nidx] += eg * wg; bBuf[nidx] += eb * wb; } }
                    if (y % 2 == 0) { AddError(1, 0, 7f/16, 7f/16, 7f/16); AddError(-1, 1, 3f/16, 3f/16, 3f/16); AddError(0, 1, 5f/16, 5f/16, 5f/16); AddError(1, 1, 1f/16, 1f/16, 1f/16); }
                    else { AddError(-1, 0, 7f/16, 7f/16, 7f/16); AddError(1, 1, 3f/16, 3f/16, 3f/16); AddError(0, 1, 5f/16, 5f/16, 5f/16); AddError(-1, 1, 1f/16, 1f/16, 1f/16); }
                }
            }
        }
        return new ProcessedImage { Width = w, Height = h, Pixels = result };
    }
}

public class ProcessedImage
{
    public int Width { get; set; }
    public int Height { get; set; }
    public PaletteEntry?[] Pixels { get; set; } = Array.Empty<PaletteEntry?>();
}

public class ProcessedDualImage
{
    public int Width { get; set; }
    public int Height { get; set; }
    public DualEntry?[] Pixels { get; set; } = Array.Empty<DualEntry?>();
}
