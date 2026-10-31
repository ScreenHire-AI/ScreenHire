using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Branding;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Branding;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Integrity.Llm;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The signed-in user's organization: its name, its branding and its integrity settings. Branding colours are checked
/// against WCAG 2.2 AA before they save, and a colour that fails is refused with the nearest passing shade (ADR-0018);
/// brand images upload straight to storage and are checked, then scanned, before a save may use them
/// (docs/architecture/branding.md). Each settings page has its history (design system T6): the name, the branding and
/// the integrity settings, readable by every member like the settings themselves.
/// </summary>
internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization").WithTags("Organization").RequireAuthorization();
        group.MapGet("", GetOrganization).WithName("GetOrganization").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/history", GetOrganizationHistory).WithName("GetOrganizationHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding/history", GetBrandingHistory).WithName("GetOrganizationBrandingHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/integrity-settings/history", GetIntegritySettingsHistory).WithName("GetOrganizationIntegritySettingsHistory")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("", UpdateOrganization).WithName("UpdateOrganization")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding", GetBranding).WithName("GetOrganizationBranding").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/branding", UpdateBranding).WithName("UpdateOrganizationBranding")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding/contrast", CheckBrandingContrast).WithName("CheckBrandingContrast").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/branding/images", CreateBrandImageUpload).WithName("CreateBrandImageUpload")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/branding/images/{imageId:guid}/verify", VerifyBrandImageUpload).WithName("VerifyBrandImageUpload")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/branding/images/{imageId:guid}", GetBrandImage).WithName("GetBrandImage").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/integrity-settings", GetIntegritySettings).WithName("GetOrganizationIntegritySettings")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("/integrity-settings", UpdateIntegritySettings).WithName("UpdateOrganizationIntegritySettings")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Results<Ok<OrganizationProfile>, NotFound>> GetOrganization(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null ? TypedResults.NotFound() : TypedResults.Ok(await ToProfileAsync(db, organization, domains.Value, cancellationToken));
    }

    // Renames only. The slug is the careers address boards and candidates already link to, so it stays.
    private static async Task<Results<Ok<OrganizationProfile>, NotFound, ValidationProblem>> UpdateOrganization(
        UpdateOrganizationRequest request, IValidator<UpdateOrganizationRequest> validator, ScreenHireDbContext db, ITenantContext tenant,
        IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        organization.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToProfileAsync(db, organization, domains.Value, cancellationToken));
    }

    private static async Task<OrganizationProfile> ToProfileAsync(
        ScreenHireDbContext db, Organization organization, DomainOptions domains, CancellationToken cancellationToken)
    {
        var last = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Organization, SubjectId(organization.Id), cancellationToken);
        return new(organization.Id, organization.Name, organization.Slug, organization.HomeRegion, domains.CareersBoardUrl(organization.Slug),
            organization.CreatedAt, LastUpdate.From(last, organization.CreatedAt));
    }

    // Every settings history's subject is the organization itself.
    private static string SubjectId(Guid organizationId) => organizationId.ToString("D");

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetOrganizationHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.Organization, before, limit, db, tenant, cancellationToken);

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetBrandingHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.Branding, before, limit, db, tenant, cancellationToken);

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetIntegritySettingsHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        HistoryAsync(ChangeSubjectKind.IntegritySettings, before, limit, db, tenant, cancellationToken);

    private static async Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> HistoryAsync(
        ChangeSubjectKind kind, Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (ChangeHistoryRoutes.LimitProblem(limit) is { } problem)
        {
            return problem;
        }

        if (tenant.OrganizationId is not { } organizationId)
        {
            return TypedResults.NotFound();
        }

        return await ChangeHistoryRoutes.PageAsync(db, kind, SubjectId(organizationId), before, limit, cancellationToken) is { } page
            ? TypedResults.Ok(page)
            : ChangeHistoryRoutes.CursorProblem();
    }

    private static async Task<Ok<OrganizationIntegritySettings>> GetIntegritySettings(
        ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var settings = await db.IntegritySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(await ToIntegritySettingsAsync(db, settings, time.GetUtcNow(), cancellationToken));
    }

    // Upserts the organization's row. The check runs per application when it is next triggered; turning it on does not
    // reach back to applications already checked or skipped.
    private static async Task<Results<Ok<OrganizationIntegritySettings>, ValidationProblem>> UpdateIntegritySettings(
        UpdateIntegritySettingsRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (request.MonthlyTokenBudget is { } budget && budget is < 1 or > IntegritySettings.MaxMonthlyTokenBudget)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "monthlyTokenBudget", $"A monthly budget is between 1 and {IntegritySettings.MaxMonthlyTokenBudget.ToString("N0", CultureInfo.InvariantCulture)} tokens."));
        }

        var now = time.GetUtcNow();
        var settings = await db.IntegritySettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new IntegritySettings { OrganizationId = tenant.OrganizationId!.Value };
            db.IntegritySettings.Add(settings);
        }

        Apply(settings, request, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two first saves at once: the other created the row. A PUT replaces the settings, so this one is applied
            // on top of it (last write wins) instead of failing.
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.IntegritySettings.SingleAsync(cancellationToken);
            Apply(settings, request, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ToIntegritySettingsAsync(db, settings, now, cancellationToken));
    }

    private static void Apply(IntegritySettings settings, UpdateIntegritySettingsRequest request, DateTimeOffset now)
    {
        settings.LlmConsistencyCheckEnabled = request.LlmConsistencyCheck;
        settings.MonthlyTokenBudget = request.MonthlyTokenBudget;
        settings.UpdatedAt = now;
    }

    private static async Task<OrganizationIntegritySettings> ToIntegritySettingsAsync(
        ScreenHireDbContext db, IntegritySettings? settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var used = await db.TokensUsedThisMonthAsync(now, cancellationToken);
        var last = settings is null
            ? null
            : await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.IntegritySettings, SubjectId(settings.OrganizationId), cancellationToken);
        // No row is ScreenHire's defaults, which nobody in the organization has changed.
        var lastUpdated = settings is null ? LastUpdate.ByScreenHire(null) : LastUpdate.From(last, settings.UpdatedAt);
        return new OrganizationIntegritySettings(settings?.LlmConsistencyCheckEnabled ?? false, settings?.MonthlyTokenBudget, used, lastUpdated);
    }

    private static async Task<Results<Ok<OrganizationBranding>, NotFound>> GetBranding(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        return organization is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(await ToBrandingAsync(db, organization, domains.Value, cancellationToken));
    }

    // Replaces the whole branding. Every colour is checked before anything saves (ADR-0018): a colour that fails a pair is
    // refused with the pair in words and the nearest shade that passes. Each image must be this organization's (the
    // tenant filter scopes the lookup), of its slot's kind, and past its scan.
    private static async Task<Results<Ok<OrganizationBranding>, NotFound, ValidationProblem>> UpdateBranding(
        UpdateBrandingRequest request, ScreenHireDbContext db, ITenantContext tenant, IOptions<DomainOptions> domains,
        CancellationToken cancellationToken)
    {
        var problems = new Dictionary<string, string[]>();
        CheckColor(problems, "brandColor", request.BrandColor, "A brand colour", BrandColorCheck.CheckBrand);
        CheckColor(problems, "secondColor", request.SecondColor, "A second colour", BrandColorCheck.CheckSecond);
        if (request.HeadingFont is { } font && !Enum.IsDefined(font))
        {
            problems["headingFont"] = ["Pick one of the heading fonts."];
        }

        (string Field, Guid? Id, BrandImageKind Kind)[] slots =
        [
            ("logoId", request.LogoId, BrandImageKind.Logo),
            ("faviconId", request.FaviconId, BrandImageKind.Favicon),
            ("sharingImageId", request.SharingImageId, BrandImageKind.SharingImage),
        ];
        var ids = slots.Select(s => s.Id).OfType<Guid>().ToList();
        var images = ids.Count == 0 ? [] : await db.BrandImages.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        foreach (var (field, id, kind) in slots)
        {
            if (id is { } imageId && ImageProblem(images.SingleOrDefault(i => i.Id == imageId), kind) is { } problem)
            {
                problems[field] = [problem];
            }
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        organization.SetBranding(
            request.BrandColor, request.SecondColor, request.HeadingFont, request.LogoId, request.FaviconId, request.SharingImageId);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToBrandingAsync(db, organization, domains.Value, cancellationToken));
    }

    private static void CheckColor(
        Dictionary<string, string[]> problems, string field, string? color, string what, Func<string, ColorCheck> check)
    {
        if (color is null)
        {
            return;
        }

        if (!BrandPalette.IsValid(color))
        {
            problems[field] = [$"{what} is a hex colour such as #1f4e5a."];
        }
        else if (check(color) is { Passes: false } result)
        {
            problems[field] = [.. result.Problems()];
        }
    }

    private static string? ImageProblem(BrandImage? image, BrandImageKind kind)
    {
        var noun = BrandImageRules.For(kind).Noun;
        return image switch
        {
            null => $"This {noun} is not one of your uploads. Upload it again.",
            _ when image.Kind != kind => $"This image was uploaded as another kind. Upload it as the {noun}.",
            { ScanStatus: MalwareScanStatus.Pending } => $"The {noun} is still being scanned for malware. Save again in a moment.",
            { ScanStatus: MalwareScanStatus.Infected } => $"The {noun} did not pass the malware scan. Upload another file.",
            _ => null,
        };
    }

    // Both colours at once, for the page to show as someone types. Nothing is saved.
    private static Results<Ok<BrandingContrast>, ValidationProblem> CheckBrandingContrast(string? brandColor, string? secondColor)
    {
        var problems = new Dictionary<string, string[]>();
        if (brandColor is not null && !BrandPalette.IsValid(brandColor))
        {
            problems["brandColor"] = ["A brand colour is a hex colour such as #1f4e5a."];
        }

        if (secondColor is not null && !BrandPalette.IsValid(secondColor))
        {
            problems["secondColor"] = ["A second colour is a hex colour such as #1f4e5a."];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        return TypedResults.Ok(new BrandingContrast(
            brandColor is null ? null : BrandColorCheck.CheckBrand(brandColor).ToContrast(),
            secondColor is null ? null : BrandColorCheck.CheckSecond(secondColor).ToContrast()));
    }

    // A create-only link for one image, after its name and claimed size pass; the bytes are checked on verify.
    private static async Task<Results<Ok<UploadTicket>, ValidationProblem>> CreateBrandImageUpload(
        BrandImageUploadRequest request, IValidator<BrandImageUploadRequest> validator, BrandImageUploads uploads, ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var rules = BrandImageRules.For(request.Kind);
        if (!Enum.IsDefined(request.Kind) || FileTypes.SafeFileName(request.FileName) is not { } fileName
            || !BrandImageUploads.HasImageExtension(fileName))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("fileName", "Upload a PNG or JPEG image."));
        }

        if (request.SizeBytes > rules.MaxBytes)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("sizeBytes", $"This file is larger than {rules.MaxSize}."));
        }

        return TypedResults.Ok(await uploads.IssueAsync(tenant.OrganizationId!.Value, request.Kind, fileName, cancellationToken));
    }

    // Checks the uploaded bytes, keeps the image and queues its malware scan in one transaction. Verifying an upload
    // that was already kept returns it as it is.
    private static async Task<Results<Ok<BrandingImage>, NotFound, ValidationProblem>> VerifyBrandImageUpload(
        Guid imageId, IDbContextOutbox<ScreenHireDbContext> outbox, BrandImageUploads uploads, ITenantContext tenant,
        IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.NotFound();
        }

        var board = domains.Value.CareersBoardUrl(organization.Slug);
        if (await db.BrandImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken) is { } kept)
        {
            return TypedResults.Ok(kept.ToBrandingImage(board));
        }

        var check = await uploads.CheckAsync(organization.Id, imageId, tenant.UserId, cancellationToken);
        if (check.Image is not { } image)
        {
            return check.Found
                ? TypedResults.ValidationProblem(ValidationExtensions.Problem("file", check.Problem!))
                : TypedResults.NotFound();
        }

        db.BrandImages.Add(image);
        await outbox.PublishAsync(new ScanBrandImage(organization.Id, image.Id, uploads.ContainerName));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        // A second verify of the same upload (a double click, a retry) kept it between the read above and this insert.
        catch (DbUpdateException exception) when (exception.IsUniqueViolation()
            && exception.InnerException is Npgsql.PostgresException { TableName: "brand_images" })
        {
            db.ChangeTracker.Clear();
            image = await db.BrandImages.AsNoTracking().SingleAsync(i => i.Id == imageId, cancellationToken);
        }

        return TypedResults.Ok(image.ToBrandingImage(board));
    }

    // For the page to follow a scan; every member reads the branding.
    private static async Task<Results<Ok<BrandingImage>, NotFound>> GetBrandImage(
        Guid imageId, ScreenHireDbContext db, ITenantContext tenant, IOptions<DomainOptions> domains, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OrganizationId, cancellationToken);
        var image = await db.BrandImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        return organization is null || image is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(image.ToBrandingImage(domains.Value.CareersBoardUrl(organization.Slug)));
    }

    private static async Task<OrganizationBranding> ToBrandingAsync(
        ScreenHireDbContext db, Organization organization, DomainOptions domains, CancellationToken cancellationToken)
    {
        var last = await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.Branding, SubjectId(organization.Id), cancellationToken);
        // ScreenHire's default branding, never changed, is by ScreenHire; branding set before the history began has only a time.
        var isDefault = organization.BrandColor is null && organization.SecondColor is null && organization.HeadingFont is null
            && organization.LogoImageId is null && organization.FaviconImageId is null && organization.SharingImageId is null;
        var lastUpdated = last is null && isDefault ? LastUpdate.ByScreenHire(null) : LastUpdate.From(last, organization.CreatedAt);

        Guid?[] wanted = [organization.LogoImageId, organization.FaviconImageId, organization.SharingImageId];
        var ids = wanted.OfType<Guid>().ToList();
        var images = ids.Count == 0 ? [] : await db.BrandImages.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var board = domains.CareersBoardUrl(organization.Slug);
        BrandingImage? Image(Guid? id) => images.SingleOrDefault(i => i.Id == id)?.ToBrandingImage(board);
        return new(organization.BrandColor, organization.SecondColor, organization.HeadingFont,
            Image(organization.LogoImageId), Image(organization.FaviconImageId), Image(organization.SharingImageId), lastUpdated);
    }
}
