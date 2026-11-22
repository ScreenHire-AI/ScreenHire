using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// Heading fonts (<c>ScreenHire:Fonts</c>, docs/architecture/branding.md section 3, ADR-0031): where the catalogue comes
/// from and where downloaded families are kept. Fontsource needs no key, so production lists the whole Google Fonts
/// catalogue with no setting; Development and the test stacks use the sandbox, a dozen fonts inside the API, so nothing
/// needs the internet.
/// </summary>
public sealed class FontsOptions
{
    public const string Section = "ScreenHire:Fonts";

    /// <summary>The private blob container the downloaded families go to. The API serves them; storage never does.</summary>
    [Required, RegularExpression("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
    public string Container { get; set; } = "heading-fonts";

    /// <summary>How long the catalogue is kept in memory before it is listed again.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan CatalogueLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How many font previews one person may load a minute (each is one download the first time).</summary>
    [Range(10, 10_000)]
    public int PreviewsPerMinute { get; set; } = 240;

    public FontsSandboxOptions Sandbox { get; set; } = new();

    public FontsourceOptions Fontsource { get; set; } = new();
}

/// <summary>The local sandbox catalogue: about a dozen real OFL and Apache fonts bundled in the API.</summary>
public sealed class FontsSandboxOptions
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Fontsource (fontsource.org), which lists every Google Fonts family with its licence and serves its files. Read
/// only by the API, never by a browser.
/// </summary>
public sealed class FontsourceOptions
{
    /// <summary>The metadata API: <c>fonts</c> lists the catalogue and <c>fonts/{id}</c> one family's files.</summary>
    [Required]
    public Uri ApiUrl { get; set; } = new("https://api.fontsource.org/v1/");

    /// <summary>
    /// Where the files are. A file address in the API's answer is downloaded only when it starts with this, so a changed
    /// or tampered answer cannot point the server anywhere else.
    /// </summary>
    [Required]
    public Uri FilesUrl { get; set; } = new("https://cdn.jsdelivr.net/fontsource/fonts/");

    /// <summary>Where each package's licence text is (<c>@fontsource/{id}@{version}/LICENSE</c> under it).</summary>
    [Required]
    public Uri PackageUrl { get; set; } = new("https://cdn.jsdelivr.net/npm/");
}
