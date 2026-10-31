using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using AIAgent.Core;

namespace AIAgent.Wiki;

/// <summary>SQLite 后端：FTS5 trigram 子串检索；FTS 不可用时退回 LIKE。</summary>
public sealed class SqliteWikiStore : IWikiSource, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly bool _fts;

    private SqliteWikiStore(SqliteConnection conn, int count, bool fts)
    {
        _conn = conn;
        Count = count;
        _fts = fts;
    }

    public int Count { get; }

    public string Engine => _fts ? "sqlite+fts5" : "sqlite";

    public static SqliteWikiStore Open(string path)
    {
        SqliteBoot.Ensure();
        var conn = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly");
        conn.Open();

        int count;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pages";
            count = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var fts = true;
        try
        {
            using var probe = conn.CreateCommand();
            probe.CommandText = "SELECT count(*) FROM pages_fts WHERE pages_fts MATCH '测试'";
            probe.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            fts = false;
            PluginLog.Warn("知识库 FTS5 不可用，退回 LIKE 检索: " + ex.Message);
        }

        PluginLog.Info($"知识库已加载: {count} 页 | {(fts ? "sqlite+fts5" : "sqlite+like")}");
        return new SqliteWikiStore(conn, count, fts);
    }

    public IReadOnlyList<WikiHit> Search(string query, int limit = 3, int excerptChars = 600)
    {
        if (Count == 0 || string.IsNullOrWhiteSpace(query)) return Array.Empty<WikiHit>();

        var q = query.Trim();
        if (q.Length < 2) return Array.Empty<WikiHit>();

        try
        {
            using var cmd = _conn.CreateCommand();
            if (_fts)
            {
                cmd.CommandText = @"
SELECT p.title, p.url, snippet(pages_fts, 1, '', '', '…', 400) AS snip, bm25(pages_fts) AS score
FROM pages_fts
JOIN pages p ON p.id = pages_fts.rowid
WHERE pages_fts MATCH @q
ORDER BY score
LIMIT @n";
                cmd.Parameters.AddWithValue("@q", "\"" + q.Replace("\"", "\"\"") + "\"");
            }
            else
            {
                cmd.CommandText = @"
SELECT title, url, substr(text, 1, 300) AS snip, 0 AS score
FROM pages
WHERE title LIKE @like OR text LIKE @like
LIMIT @n";
                cmd.Parameters.AddWithValue("@like", "%" + q + "%");
            }

            cmd.Parameters.AddWithValue("@n", Math.Max(1, limit));

            var hits = new List<WikiHit>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(new WikiHit(
                    reader.GetString(0),
                    reader.GetString(1),
                    Trim(reader.GetString(2), excerptChars),
                    reader.GetDouble(3)));
            }

            return hits;
        }
        catch (SqliteException ex)
        {
            PluginLog.Error("知识库检索失败", ex);
            return Array.Empty<WikiHit>();
        }
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }

    public void Dispose() => _conn.Dispose();
}
