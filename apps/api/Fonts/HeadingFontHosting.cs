using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// Makes a heading font servable before a branding save names it (ADR-0031). A bundled font needs nothing. Any other
/// family is downloaded once, for everyone: the weights the careers titles use (<see cref="PickWeights"/>), latin and
/// latin-ext where the family has it, and its licence text, all into our own storage, and then one
/// <see cref="HostedFont"/> row. The download happens in the save's request, not in the worker, because the save must
/// not name a font the careers pages cannot serve; a family is a handful of files of 10 to 50 KB.
/// </summary>
internal sealed partial class HeadingFontHosting(
    IFontSource source, HeadingFontCatalogue catalogue, HeadingFontStore store, TimeProvider time, ILogger<HeadingFontHosting> logger)
{
    /// <summary>The weights the careers pages' titles use: 600, 700 and 800 (a title set at 650 is drawn with the 700 file).</summary>
    private static readonly int[] TitleWeights = [600, 700, 800];

    private static readonly string[] Subsets = ["latin", "latin-ext"];

    /// <summary>
    /// The font <paramref name="id"/> names, hosted, or the reason it cannot be: a message for the person saving.
    /// </summary>
    public async Task<HostingResult> EnsureAsync(string id, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!HeadingFonts.IsValidId(id))
        {
            return HostingResult.Refused(NotAChoice);
        }

        if (HeadingFonts.Bundled.TryGetValue(id, out var bundled))
        {
            return HostingResult.Hosted(new HeadingFont(id, bundled));
        }

        if (await db.HostedFonts.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken) is { } hosted)
        {
            return HostingResult.Hosted(new HeadingFont(hosted.Id, hosted.Family));
        }

        // Only a family the catalogue offers is ever fetched, so a crafted id cannot make the server download anything else.
        var listing = await catalogue.ListAsync(db, cancellationToken);
        if (!listing.Fonts.Any(f => f.Id == id))
        {
            return HostingResult.Refused(listing.Complete ? NotAChoice : Unreachable);
        }

        try
        {
            return await HostAsync(id, db, cancellationToken);
        }
        catch (Exception exception) when (FontDownloads.IsUnreachable(exception) && !cancellationToken.IsCancellationRequested)
        {
            DownloadFailed(logger, exception, id);
            return HostingResult.Refused(Unreachable);
        }
    }

    private async Task<HostingResult> HostAsync(string id, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (await source.GetAsync(id, cancellationToken) is not { } font || font.Id != id || !font.Google
            || !HeadingFonts.IsValidFamily(font.Family) || !VersionPattern().IsMatch(font.Version))
        {
            return HostingResult.Refused(NotAChoice);
        }

        if (FontLicenses.FromSpdx(font.License) is not { } license)
        {
            return HostingResult.Refused("This font is not under the Open Font License or Apache 2.0, so ScreenHire cannot host it. Pick another.");
        }

        var files = ChooseFiles(font);
        if (files.Count == 0)
        {
            return HostingResult.Refused("This font has no latin letters to download. Pick another.");
        }

        if (await source.LicenseTextAsync(font, cancellationToken) is not { } licenseText)
        {
            return HostingResult.Refused("This font came without its licence, so ScreenHire cannot host it. Pick another.");
        }

        var stored = new List<HostedFontFile>();
        foreach (var file in files)
        {
            var bytes = await source.DownloadAsync(font, file, cancellationToken);
            if (!FontDownloads.IsWoff2(bytes))
            {
                NotWoff2(logger, id);
                return HostingResult.Refused(Unreachable);
            }

            var name = $"{file.Subset}-{file.Weight}.woff2";
            await store.WriteAsync(HeadingFontStore.FileBlob(id, font.Version, name), bytes, "font/woff2", cancellationToken);
            stored.Add(new HostedFontFile(file.Subset, file.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                font.UnicodeRanges[file.Subset], name, bytes.Length));
        }

        await store.WriteAsync(
            HeadingFontStore.FileBlob(id, font.Version, HostedFont.LicenseFileName), Encoding.UTF8.GetBytes(licenseText), "text/plain; charset=utf-8",
            cancellationToken);

        var added = db.HostedFonts.Add(new HostedFont(id, font.Family, font.Category, license, font.Version, stored, time.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        // Another save of the same family won the race; its files are the same bytes under the same names. Only this row
        // is let go: the branding save's other tracked rows (its images, which it marks saved) stay tracked.
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            added.State = EntityState.Detached;
        }

        var row = await db.HostedFonts.AsNoTracking().SingleAsync(f => f.Id == id, cancellationToken);
        Hosted(logger, id, row.Version);
        return HostingResult.Hosted(new HeadingFont(row.Id, row.Family));
    }

    /// <summary>
    /// Latin and latin-ext files at <see cref="PickWeights"/> of the weights the family's latin subset has. A subset the
    /// family lacks a chosen weight in is left out at that weight; latin-ext is a bonus, latin is required. A file is
    /// kept only in the shape the careers stylesheet accepts (<c>apps/careers/src/lib/brand.ts</c>): a weight of 100 to
    /// 900 in hundreds, and a subset with a well-formed unicode range. The careers pages refuse anything else, so storing
    /// it would break every page of the organization that saved it.
    /// </summary>
    internal static List<SourceFontFile> ChooseFiles(SourceFontDetail font)
    {
        var usable = font.Files.Where(f => f.Weight is >= 100 and <= 900 && f.Weight % 100 == 0
            && font.UnicodeRanges.TryGetValue(f.Subset, out var range) && UnicodeRangePattern().IsMatch(range)).ToList();
        var latin = usable.Where(f => f.Subset == "latin").ToList();
        var weights = PickWeights(latin.Select(f => f.Weight));
        return [.. Subsets.SelectMany(subset => weights.Select(weight => usable.FirstOrDefault(f => f.Subset == subset && f.Weight == weight)))
            .OfType<SourceFontFile>()];
    }

    /// <summary>
    /// For each title weight, the nearest the family has (a tie goes to the heavier), without repeats: a family of
    /// 100 to 900 gives 600, 700 and 800; one of 400 and 700 gives 700; one of 400 only gives 400.
    /// </summary>
    internal static int[] PickWeights(IEnumerable<int> available)
    {
        var weights = available.Distinct().ToArray();
        if (weights.Length == 0)
        {
            return [];
        }

        return [.. TitleWeights
            .Select(wanted => weights.OrderBy(w => Math.Abs(w - wanted)).ThenByDescending(w => w).First())
            .Distinct()
            .Order()];
    }

    private const string NotAChoice = "Pick one of the heading fonts.";

    private const string Unreachable = "We could not download this font just now. Try again in a moment, or pick another.";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heading font {FontId} could not be downloaded")]
    private static partial void DownloadFailed(ILogger logger, Exception exception, string fontId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heading font {FontId} sent a file that is not a WOFF2 font")]
    private static partial void NotWoff2(ILogger logger, string fontId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hosted heading font {FontId} {Version}")]
    private static partial void Hosted(ILogger logger, string fontId, string version);

    // The same pattern as `unicodeRange` in apps/careers/src/lib/brand.ts.
    [GeneratedRegex(@"^U\+[0-9A-F?]{1,6}(-[0-9A-F]{1,6})?(,\s*U\+[0-9A-F?]{1,6}(-[0-9A-F]{1,6})?)*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UnicodeRangePattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

/// <summary>The font to save, or why it cannot be saved.</summary>
internal sealed record HostingResult(HeadingFont? Font, string? Problem)
{
    public static HostingResult Hosted(HeadingFont font) => new(font, null);

    public static HostingResult Refused(string problem) => new(null, problem);
}
