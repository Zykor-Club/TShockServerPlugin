using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using AIAgent.Core;

namespace AIAgent.Wiki;

/// <summary>把 wiki.json 导入 SQLite（FTS5 + trigram），供运行期检索。</summary>
public static class WikiDatabase
{
    public const string SchemaVersion = "1";

    public static int Build(string jsonPath, string dbPath)
    {
        SqliteBoot.Ensure();
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("找不到 wiki.json", jsonPath);
        }

        var file = JsonSerializer.Deserialize<WikiFile>(File.ReadAllText(jsonPath, Encoding.UTF8))
                   ?? throw new InvalidOperationException("wiki.json 内容为空");

        var temp = dbPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        using (var conn = new SqliteConnection("Data Source=" + temp + ";Pooling=False"))
        {
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE pages(
    id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    text TEXT NOT NULL
);
CREATE VIRTUAL TABLE pages_fts USING fts5(
    title, text,
    content='pages', content_rowid='id',
    tokenize='trigram'
);
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);";
                cmd.ExecuteNonQuery();
            }

            using (var tx = conn.BeginTransaction())
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO pages(title, url, text) VALUES (@t, @u, @x)";
                var pt = insert.CreateParameter(); pt.ParameterName = "@t"; insert.Parameters.Add(pt);
                var pu = insert.CreateParameter(); pu.ParameterName = "@u"; insert.Parameters.Add(pu);
                var px = insert.CreateParameter(); px.ParameterName = "@x"; insert.Parameters.Add(px);

                foreach (var page in file.Pages)
                {
                    pt.Value = page.Title;
                    pu.Value = page.Url;
                    px.Value = page.Text;
                    insert.ExecuteNonQuery();
                }

                using var meta = conn.CreateCommand();
                meta.CommandText = "INSERT INTO meta(key, value) VALUES ('source', @s), ('generatedAt', @g), ('schema', @v)";
                meta.Parameters.AddWithValue("@s", file.Source ?? string.Empty);
                meta.Parameters.AddWithValue("@g", file.GeneratedAt ?? string.Empty);
                meta.Parameters.AddWithValue("@v", SchemaVersion);
                meta.ExecuteNonQuery();

                tx.Commit();
            }

            using (var rebuild = conn.CreateCommand())
            {
                rebuild.CommandText = "INSERT INTO pages_fts(pages_fts) VALUES('rebuild')";
                rebuild.ExecuteNonQuery();
            }
        }

        if (File.Exists(dbPath)) File.Delete(dbPath);
        File.Move(temp, dbPath);

        return file.Pages.Count;
    }
}
