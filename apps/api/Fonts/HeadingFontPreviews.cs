using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// The file the font picker draws a family's name in (docs/architecture/branding.md section 3): its latin subset at
/// the weight nearest 700, downloaded the first time anyone opens it and kept in storage, so the dashboard only ever
/// asks the API. A bundled font has no preview here: the dashboard ships it. A kept preview is never fetched again,
/// even when the family has a newer release: it only draws the family's name in the picker, and the careers pages
/// use the hosted files.
/// </summary>
internal sealed partial class HeadingFontPreviews(
    IFontSource source, HeadingFontCatalogue catalogue, HeadingFontStore store, ILogger<HeadingFontPreviews> logger)
{
    /// <summary>The preview of <paramref name="id"/>, or null for a font that has none (not in the catalogue, or bundled).</summary>
    public async Task<Stream?> OpenAsync(string id, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (!HeadingFonts.IsValidId(id) || HeadingFonts.IsBundled(id))
        {
            return null;
        }

        if (await store.OpenReadAsync(HeadingFontStore.PreviewBlob(id), cancellationToken) is { } kept)
        {
            return kept;
        }

        var listing = await catalogue.ListAsync(db, cancellationToken);
        if (!listing.Fonts.Any(f => f.Id == id))
        {
            return null;
        }

        try
        {
            if (await source.GetAsync(id, cancellationToken) is not { } font
                || font.Files.Where(f => f.Subset == "latin").OrderBy(f => Math.Abs(f.Weight - 700)).ThenByDescending(f => f.Weight).FirstOrDefault()
                    is not { } file)
            {
                return null;
            }

            var bytes = await source.DownloadAsync(font, file, cancellationToken);
            if (!FontDownloads.IsWoff2(bytes))
            {
                return null;
            }

            await store.WriteAsync(HeadingFontStore.PreviewBlob(id), bytes, "font/woff2", cancellationToken);
            return new MemoryStream(bytes, writable: false);
        }
        catch (Exception exception) when (FontDownloads.IsUnreachable(exception) && !cancellationToken.IsCancellationRequested)
        {
            // The picker draws the name in the page's own font instead.
            PreviewFailed(logger, exception, id);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The preview of heading font {FontId} could not be downloaded")]
    private static partial void PreviewFailed(ILogger logger, Exception exception, string fontId);
}
