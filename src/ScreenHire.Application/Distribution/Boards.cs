namespace ScreenHire.Application.Distribution;

/// <summary>Board ids. They are stored on postings and appear in feed URLs, so never rename one.</summary>
public static class Boards
{
    public const string CareersPage = "careers";
    public const string GoogleIndexing = "google";
    public const string JobBoardApi = "job-board-api";
    public const string GenericFeed = "xml-feed";
    public const string Careerjet = "careerjet";
    public const string TalentCom = "talent";
    public const string Adzuna = "adzuna";
    public const string Jooble = "jooble";
    public const string Sandbox = "sandbox";

    /// <summary>
    /// Every automatic board's id, configured or not. A posting the team makes themselves cannot take one as its source key,
    /// or its applications would count for that board.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CareersPage, GoogleIndexing, JobBoardApi, GenericFeed, Careerjet, TalentCom, Adzuna, Jooble, Sandbox,
    };
}
