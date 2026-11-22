using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Fonts;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Heading fonts (docs/architecture/branding.md section 3, ADR-0031): the catalogue and its previews for the dashboard's
/// picker, and the files of a hosted family for the careers pages, which serve them on their own origin. Nothing here
/// changes data: a family is hosted by the branding save (<c>PUT /v1/organization/branding</c>).
/// </summary>
internal static class HeadingFontEndpoints
{
    public static IEndpointRouteBuilder MapHeadingFontEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/heading-fonts").WithTags("Heading fonts").RequireAuthorization();
        group.MapGet("", ListHeadingFonts).WithName("ListHeadingFonts").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/{fontId}/preview", GetPreview).WithName("GetHeadingFontPreview")
            .RequireRateLimiting(HeadingFontSetup.PreviewRateLimitPolicy)
            .Produces(StatusCodes.Status200OK, contentType: "font/woff2")
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/v1/public/heading-fonts/{fontId}/{fileName}", GetPublicFile).WithName("GetPublicHeadingFontFile")
            .WithTags("Public careers")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "font/woff2", additionalContentTypes: ["text/plain"]);
        return app;
    }

    // Every member reads the branding, so every member may browse its fonts.
    private static async Task<Ok<HeadingFontCatalogueResponse>> ListHeadingFonts(
        HeadingFontCatalogue catalogue, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        var listing = await catalogue.ListAsync(db, cancellationToken);
        return TypedResults.Ok(new HeadingFontCatalogueResponse(listing.Fonts, listing.Complete));
    }

    // One file per family, the same for everyone, so a browser may keep it for a week.
    private static async Task<Results<FileStreamHttpResult, NotFound>> GetPreview(
        string fontId, HttpContext http, HeadingFontPreviews previews, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (await previews.OpenAsync(fontId, db, cancellationToken) is not { } bytes)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=604800";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(bytes, "font/woff2");
    }

    // A hosted family's file or its licence. The row names every file there is, so a name that is not in it is 404 and
    // nothing else in the container can be reached. The bytes under a name never change: cached for a year.
    private static async Task<Results<FileStreamHttpResult, NotFound>> GetPublicFile(
        string fontId, string fileName, HttpContext http, ScreenHireDbContext db, HeadingFontStore store, CancellationToken cancellationToken)
    {
        if (!HeadingFonts.IsValidId(fontId)
            || await db.HostedFonts.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fontId, cancellationToken) is not { } font)
        {
            return TypedResults.NotFound();
        }

        var isLicense = fileName == HostedFont.LicenseFileName;
        if ((!isLicense && !font.Files.Any(f => f.FileName == fileName))
            || await store.OpenReadAsync(HeadingFontStore.FileBlob(font.Id, font.Version, fileName), cancellationToken) is not { } bytes)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        return TypedResults.Stream(bytes, isLicense ? "text/plain; charset=utf-8" : "font/woff2");
    }
}
