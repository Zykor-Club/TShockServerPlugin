using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PixelArtPlus;

#pragma warning disable CS8618

public class ColorData
{
    [JsonPropertyName("tiles")]
    public Dictionary<string, TileColorInfo> Tiles { get; set; } = new();
    [JsonPropertyName("walls")]
    public Dictionary<string, WallColorInfo> Walls { get; set; } = new();
}

public class TileColorInfo
{
    [JsonPropertyName("tileId")] public int TileId { get; set; }
    [JsonPropertyName("variation")] public int Variation { get; set; }
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
    [JsonPropertyName("hex")] public string Hex { get; set; } = "";
}

public class WallColorInfo
{
    [JsonPropertyName("wallId")] public int WallId { get; set; }
    [JsonPropertyName("variation")] public int Variation { get; set; }
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
    [JsonPropertyName("hex")] public string Hex { get; set; } = "";
}

public class TextureColorData
{
    [JsonPropertyName("Tiles")]
    public Dictionary<int, TextureTileInfo> Tiles { get; set; } = new();
    [JsonPropertyName("Walls")]
    public Dictionary<int, TextureWallInfo> Walls { get; set; } = new();
}

public class TextureTileInfo
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("R")] public byte R { get; set; }
    [JsonPropertyName("G")] public byte G { get; set; }
    [JsonPropertyName("B")] public byte B { get; set; }
    [JsonPropertyName("CanPlace")] public bool CanPlace { get; set; } = true;
}

public class TextureWallInfo
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("R")] public byte R { get; set; }
    [JsonPropertyName("G")] public byte G { get; set; }
    [JsonPropertyName("B")] public byte B { get; set; }
    [JsonPropertyName("CanPlace")] public bool CanPlace { get; set; } = true;
}

/// <summary>Palette entry with pre-computed CIELAB values for DeltaE color matching.</summary>
public struct PaletteEntry
{
    public int TypeId;
    public int PaintId;
    public byte R, G, B;
    public bool IsWall;
    public double LabL, LabA, LabB;

    public PaletteEntry(int typeId, int paintId, byte r, byte g, byte b, bool isWall)
    {
        TypeId = typeId; PaintId = paintId; R = r; G = g; B = b; IsWall = isWall;
        LabL = 0; LabA = 0; LabB = 0;
    }
}
