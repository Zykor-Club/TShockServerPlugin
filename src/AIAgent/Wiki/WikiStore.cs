using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIAgent.Core;

namespace AIAgent.Wiki;

public sealed class WikiPage
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed class WikiFile
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public List<WikiPage> Pages { get; set; } = new();
}

public sealed record WikiHit(string Title, string Url, string Excerpt, double Score);

/// <summary>
/// 离线 Wiki 知识库：启动时把 wiki.json 读进内存，用中文二元组匹配做检索。
/// 数据是"资料"，注入时必须声明不是指令。
/// </summary>
public sealed class WikiStore : IWikiSource
{
    private readonly List<WikiPage> _pages;
    private readonly Dictionary<WikiPage, HashSet<string>> _titleBigrams = new();

    private WikiStore(List<WikiPage> pages)
    {
        _pages = pages;
        foreach (var page in pages)
        {
            _titleBigrams[page] = Bigrams(page.Title);
        }
    }

    public int Count => _pages.Count;

    public string Engine => "json";

    public static WikiStore Load(string path)
    {
        if (!File.Exists(path))
        {
            PluginLog.Warn($"Wiki 知识库不存在: {path}（AI 会用自身知识回答）");
            return new WikiStore(new List<WikiPage>());
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var file = JsonSerializer.Deserialize<WikiFile>(json) ?? new WikiFile();
            PluginLog.Info($"Wiki 知识库已加载: {file.Pages.Count} 页 | {file.GeneratedAt}");
            return new WikiStore(file.Pages);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Wiki 知识库加载失败: {path}", ex);
            return new WikiStore(new List<WikiPage>());
        }
    }

    public IReadOnlyList<WikiHit> Search(string query, int limit = 3, int excerptChars = 600)
    {
        if (_pages.Count == 0 || string.IsNullOrWhiteSpace(query)) return Array.Empty<WikiHit>();

        var q = Normalize(query);
        if (q.Length < 2) return Array.Empty<WikiHit>();
        var queryBigrams = Bigrams(q);

        var scored = new List<(WikiPage Page, double Score)>();
        foreach (var page in _pages)
        {
            var score = Score(page, q, queryBigrams);
            if (score > 0) scored.Add((page, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, limit))
            .Select(x => new WikiHit(x.Page.Title, x.Page.Url, Excerpt(x.Page.Text, q, excerptChars), x.Score))
            .ToList();
    }

    private double Score(WikiPage page, string query, HashSet<string> queryBigrams)
    {
        var score = 0.0;

        var titleBigrams = _titleBigrams[page];
        foreach (var bigram in queryBigrams)
        {
            if (titleBigrams.Contains(bigram)) score += 8;
        }

        if (page.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 40;
        else if (query.Contains(page.Title, StringComparison.OrdinalIgnoreCase) && page.Title.Length >= 2) score += 24;

        foreach (var bigram in queryBigrams)
        {
            if (page.Text.Contains(bigram, StringComparison.Ordinal)) score += 2;
        }

        return score;
    }

    private static string Excerpt(string text, string query, int maxChars)
    {
        if (text.Length <= maxChars) return text.Trim();

        var anchor = -1;
        foreach (var bigram in Bigrams(query))
        {
            var index = text.IndexOf(bigram, StringComparison.Ordinal);
            if (index >= 0 && (anchor < 0 || index < anchor)) anchor = index;
        }

        var start = anchor < 0 ? 0 : Math.Max(0, anchor - 80);
        var length = Math.Min(maxChars, text.Length - start);
        var piece = text.Substring(start, length).Trim();

        if (start > 0) piece = "…" + piece;
        if (start + length < text.Length) piece += "…";
        return piece;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsControl(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static HashSet<string> Bigrams(string text)
    {
        var clean = Normalize(text);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < clean.Length; i++)
        {
            set.Add(clean.Substring(i, 2));
        }
        return set;
    }
}
