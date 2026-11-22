using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// The heading fonts a person may pick (docs/architecture/branding.md section 3): every Google Fonts family the source
/// lists under the OFL or Apache 2.0 with a latin subset, plus the six ScreenHire bundles, sorted by name. Each has its
/// picker styles: Sans, Serif and Display from the source's category, Rounded and Slab from Google's own tags
/// (<c>google-font-styles.json</c>, refreshed by <c>scripts/heading-font-styles.mjs</c>). The list is kept in memory for
/// <see cref="FontsOptions.CatalogueLifetime"/>. When the source cannot be reached, the bundled six and every family
/// already hosted are still listed, marked incomplete, and nothing is cached, so the next request tries again.
/// </summary>
internal sealed partial class HeadingFontCatalogue(IFontSource source, IMemoryCache cache, IOptions<FontsOptions> options, ILogger<HeadingFontCatalogue> logger)
{
    private const string CacheKey = "heading-font-catalogue";

    // Bricolage Grotesque is not a choice: it is the default, which is no font at all.
    private const string DefaultFontId = "bricolage-grotesque";

    private static readonly Lazy<GoogleStyles> Google = new(LoadGoogleStyles);

    /// <summary>The catalogue, from memory when it was listed within the lifetime.</summary>
    public async Task<HeadingFontCatalogueListing> ListAsync(ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyList<HeadingFontOption>>(CacheKey, out var cached) && cached is not null)
        {
            return new(cached, true);
        }

        IReadOnlyList<SourceFont> listed;
        try
        {
            listed = await source.ListAsync(cancellationToken);
        }
        catch (Exception exception) when (FontDownloads.IsUnreachable(exception) && !cancellationToken.IsCancellationRequested)
        {
            CatalogueUnreachable(logger, exception);
            var hosted = await db.HostedFonts.AsNoTracking().ToListAsync(cancellationToken);
            return new(Build(hosted.Select(f => new SourceFont(
                f.Id, f.Family, f.Category, f.License == FontLicense.Apache ? "Apache-2.0" : "OFL-1.1", true, ["latin"], [], [])), Google.Value), false);
        }

        var fonts = Build(listed, Google.Value);
        cache.Set(CacheKey, fonts, options.Value.CatalogueLifetime);
        return new(fonts, true);
    }

    /// <summary>
    /// The families that may be picked, from what a source listed: Google's, under the OFL or Apache 2.0, with a latin
    /// subset, an id and name of the safe shape, never icons; then the bundled six, whatever the source said of them.
    /// </summary>
    internal static IReadOnlyList<HeadingFontOption> Build(IEnumerable<SourceFont> listed, GoogleStyles google)
    {
        var options = new Dictionary<string, HeadingFontOption>(StringComparer.Ordinal);
        foreach (var font in listed)
        {
            if (!font.Google || FontLicenses.FromSpdx(font.License) is not { } license || !font.Subsets.Contains("latin")
                || font.Category is "icons" or "other" || font.Id == DefaultFontId
                || !HeadingFonts.IsValidId(font.Id) || !HeadingFonts.IsValidFamily(font.Family))
            {
                continue;
            }

            options[font.Id] = new HeadingFontOption(font.Id, font.Family, Styles(font, google), license, HeadingFonts.IsBundled(font.Id));
        }

        foreach (var (id, family) in HeadingFonts.Bundled)
        {
            if (!options.ContainsKey(id))
            {
                var font = new SourceFont(id, family, HeadingFonts.BundledCategories[id], "OFL-1.1", true, ["latin"], [], []);
                options[id] = new HeadingFontOption(id, family, Styles(font, google), FontLicense.Ofl, Bundled: true);
            }
        }

        return [.. options.Values.OrderBy(o => o.Family, StringComparer.OrdinalIgnoreCase)];
    }

    private static HeadingFontStyle[] Styles(SourceFont font, GoogleStyles google)
    {
        var styles = new List<HeadingFontStyle>();
        switch (font.Category)
        {
            case "sans-serif":
                styles.Add(HeadingFontStyle.Sans);
                break;
            case "serif":
                styles.Add(HeadingFontStyle.Serif);
                break;
            case "display" or "handwriting":
                styles.Add(HeadingFontStyle.Display);
                break;
        }

        if (google.Rounded.Contains(font.Id) || font.Styles.Contains("Rounded"))
        {
            styles.Add(HeadingFontStyle.Rounded);
        }

        if (google.Slab.Contains(font.Id) || font.Styles.Contains("Slab"))
        {
            styles.Add(HeadingFontStyle.Slab);
        }

        return [.. styles];
    }

    private static GoogleStyles LoadGoogleStyles()
    {
        using var stream = typeof(HeadingFontCatalogue).Assembly.GetManifestResourceStream("HeadingFonts.google-font-styles.json")
            ?? throw new InvalidOperationException("The embedded google-font-styles.json is missing.");
        var file = JsonSerializer.Deserialize<GoogleStylesFile>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The embedded google-font-styles.json is empty.");
        return new GoogleStyles(file.Rounded.ToHashSet(StringComparer.Ordinal), file.Slab.ToHashSet(StringComparer.Ordinal));
    }

    private sealed record GoogleStylesFile(string[] Rounded, string[] Slab);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The heading font catalogue could not be listed; offering the bundled and hosted fonts only")]
    private static partial void CatalogueUnreachable(ILogger logger, Exception exception);
}

/// <summary>The families Google tags Rounded and Slab, by Fontsource id.</summary>
internal sealed record GoogleStyles(IReadOnlySet<string> Rounded, IReadOnlySet<string> Slab)
{
    public static GoogleStyles None { get; } = new(new HashSet<string>(), new HashSet<string>());
}

/// <summary>The catalogue, and whether it is all of it (false while the source cannot be reached).</summary>
internal sealed record HeadingFontCatalogueListing(IReadOnlyList<HeadingFontOption> Fonts, bool Complete);
