namespace ScreenHire.Domain.Organizations;

/// <summary>
/// The heading fonts an employer can pick for its careers pages (ADR-0018). Each is under the SIL Open Font License
/// and self-hosted in <c>packages/ui</c> (<c>heading-fonts.ts</c> maps these names to their font families). An
/// organization with none uses ScreenHire's, Bricolage Grotesque.
/// </summary>
public enum HeadingFont
{
    Inter,
    Montserrat,
    Nunito,
    Fraunces,
    PlayfairDisplay,
}

public static class HeadingFonts
{
    /// <summary>The name people know <paramref name="font"/> by, as History words it.</summary>
    public static string Label(this HeadingFont font) => font switch
    {
        HeadingFont.PlayfairDisplay => "Playfair Display",
        _ => font.ToString(),
    };
}
