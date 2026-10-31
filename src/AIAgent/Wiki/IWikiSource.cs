using System.Collections.Generic;

namespace AIAgent.Wiki;

/// <summary>知识库后端：JSON 全内存，或 SQLite + FTS5。</summary>
public interface IWikiSource
{
    int Count { get; }

    string Engine { get; }

    IReadOnlyList<WikiHit> Search(string query, int limit = 3, int excerptChars = 600);
}
