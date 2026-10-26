namespace ScreenHire.Domain.History;

/// <summary>The line for taking a template out of the pickers, or putting it back, shared by every kind of template.</summary>
internal static class Archive
{
    /// <param name="before">When it was archived before the save, or null.</param>
    /// <param name="after">When it is archived after the save, or null.</param>
    /// <param name="noun">What the object is called in the line: "template" or "scorecard".</param>
    public static ChangeLine? Line(DateTimeOffset? before, DateTimeOffset? after, string noun) =>
        (before, after) switch
        {
            (null, not null) => ChangeLine.Plain($"archived the {noun}"),
            (not null, null) => ChangeLine.Plain($"restored the {noun} from the archive"),
            _ => null,
        };
}
