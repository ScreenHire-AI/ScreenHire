namespace ScreenHire.Domain.History;

/// <summary>
/// One change in words, as people say it: "added <b>Did they finish the exercise in the time?</b>". The sentence has no
/// subject; the screen puts the person in front of it. <see cref="Details"/> are the before and after the History sheet
/// shows on request. Stored as JSONB, so members are only ever added, as optional.
/// </summary>
public sealed record ChangeLine(IReadOnlyList<ChangeText> Parts, IReadOnlyList<ChangeDetail>? Details = null)
{
    /// <summary>"renamed the template from Tech interview to " then <b>Technical interview</b>, then <paramref name="tail"/>.</summary>
    public static ChangeLine Of(string lead, string name, string tail = "", IReadOnlyList<ChangeDetail>? details = null) =>
        new([.. Part(lead, false), .. Part(name, true), .. Part(tail, false)], details is { Count: > 0 } ? details : null);

    /// <summary>A line with nothing to emphasise, such as "changed the subject".</summary>
    public static ChangeLine Plain(string text, IReadOnlyList<ChangeDetail>? details = null) =>
        new([new(text)], details is { Count: > 0 } ? details : null);

    /// <summary>The sentence as plain text.</summary>
    public string Sentence() => string.Concat(Parts.Select(p => p.Text));

    private static IEnumerable<ChangeText> Part(string text, bool strong) => text.Length == 0 ? [] : [new(text, strong)];
}

/// <summary>A run of a line's text; <see cref="Strong"/> marks the name of the thing that changed.</summary>
public sealed record ChangeText(string Text, bool Strong = false);

/// <summary>
/// One value before and after the change. Equal values mean it did not change and is shown for context; a null
/// <see cref="Before"/> is something new and a null <see cref="After"/> something removed.
/// </summary>
public sealed record ChangeDetail(string Label, string? Before, string? After, ChangeValueKind Kind = ChangeValueKind.Text);

/// <summary>How the screen shows a detail's values.</summary>
public enum ChangeValueKind
{
    /// <summary>A short value on one line.</summary>
    Text,

    /// <summary>Text with line breaks, such as an email's message.</summary>
    Paragraphs,

    /// <summary>A time as ISO 8601, which the browser formats in its own locale (the API never sends formatted dates).</summary>
    Instant,
}
