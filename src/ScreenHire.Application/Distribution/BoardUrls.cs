namespace ScreenHire.Application.Distribution;

/// <summary>
/// The link a job board sends candidates to carries <c>src=&lt;board id&gt;</c>, so an application can say which board
/// sent it (the Reports page's Sources panel). Only the boards that pass applicants on through a link of their own are
/// tagged. Google for Jobs and the public job-board API stay on the canonical address, because it is what the page's
/// JSON-LD, the sitemap and the indexing notifications name; their applications count as the careers page.
/// </summary>
public static class BoardUrls
{
    /// <summary>The query parameter the careers app reads.</summary>
    public const string SourceParameter = "src";

    /// <summary>The boards whose links are tagged, and so the only ids an application may carry as its source.</summary>
    public static IReadOnlySet<string> TaggedBoards { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Boards.GenericFeed,
        Boards.Careerjet,
        Boards.TalentCom,
        Boards.Adzuna,
        Boards.Jooble,
        Boards.Sandbox,
    };

    /// <summary>
    /// <paramref name="url"/> with <c>src=boardId</c> in its query, replacing an earlier <c>src</c> and keeping every other
    /// parameter and the fragment. A board that is not tagged gets the address back unchanged.
    /// </summary>
    public static string Tag(string url, string boardId)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentException.ThrowIfNullOrEmpty(boardId);
        if (!TaggedBoards.Contains(boardId))
        {
            return url;
        }

        var fragmentAt = url.IndexOf('#', StringComparison.Ordinal);
        var fragment = fragmentAt < 0 ? string.Empty : url[fragmentAt..];
        var rest = fragmentAt < 0 ? url : url[..fragmentAt];

        var queryAt = rest.IndexOf('?', StringComparison.Ordinal);
        var path = queryAt < 0 ? rest : rest[..queryAt];
        var kept = queryAt < 0
            ? []
            : rest[(queryAt + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !pair.StartsWith(SourceParameter + "=", StringComparison.Ordinal) && pair != SourceParameter)
                .ToList();
        kept.Add($"{SourceParameter}={Uri.EscapeDataString(boardId)}");

        return $"{path}?{string.Join('&', kept)}{fragment}";
    }
}
