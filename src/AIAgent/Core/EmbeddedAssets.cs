using System.IO;

namespace AIAgent.Core;

/// <summary>
/// 首次加载时把内嵌的人设和 Wiki 释放到数据目录，之后用户可以直接编辑这些文件。
/// 已存在时不覆盖，避免升级冲掉服主的修改。
/// </summary>
public static class EmbeddedAssets
{
    private const string PersonaResource = "AIAgent.persona.md";
    private const string WikiResource = "AIAgent.wiki.json";

    public static void Ensure(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        Extract(PersonaResource, Path.Combine(dataDirectory, "persona.md"));
        Extract(WikiResource, Path.Combine(dataDirectory, "wiki.json"));
    }

    private static void Extract(string resourceName, string destination)
    {
        if (File.Exists(destination)) return;

        using var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return;

        using var file = File.Create(destination);
        stream.CopyTo(file);
    }
}
