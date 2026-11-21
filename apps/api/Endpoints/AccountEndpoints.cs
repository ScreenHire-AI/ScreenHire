using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Realtime;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Slack;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Your account: what belongs to the signed-in person rather than the organization (docs/architecture/sign-in-and-tenancy.md,
/// "Your account", and docs/architecture/notifications.md, "Each person's choices"). Session only: an API key is no person.
/// No permission, because each route reads or changes only the caller's own rows, as switching organization needs none. Not
/// in the change history: personal preferences, not configuration the team shares.
/// </summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/account").WithTags("Account").RequireAuthorization();
        group.MapGet("", GetAccount).WithName("GetAccount").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/notifications", GetAccountNotifications).WithName("GetAccountNotifications").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/sign-in", GetAccountSignIn).WithName("GetAccountSignIn").Produces(StatusCodes.Status401Unauthorized);

        var mutations = group.MapGroup("").RequireAntiforgeryToken();
        mutations.MapPut("/profile", UpdateProfile).WithName("UpdateProfile").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPost("/time-zone", FillTimeZone).WithName("FillTimeZone").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/preferences", UpdateAccountPreferences).WithName("UpdateAccountPreferences").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/notifications", UpdateAccountNotifications).WithName("UpdateAccountNotifications").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPut("/photo", UploadPhoto).WithName("UploadPhoto")
            .WithMetadata(new RequestSizeLimitAttribute(PhotoBodyBytes))
            .Produces(StatusCodes.Status401Unauthorized);
        mutations.MapDelete("/photo", RemovePhoto).WithName("RemovePhoto").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPost("/sign-out-everywhere", SignOutEverywhere).WithName("SignOutEverywhere").Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/v1/users/{userId:guid}/photo", GetUserPhoto).WithTags("Account").WithName("GetUserPhoto").RequireAuthorization()
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // Base64 is a third larger than the bytes, plus the JSON around it.
    private const long PhotoBodyBytes = UserPhoto.MaxBytes * 4 / 3 + 1024;

    /// <summary>The photo's address, which changes when the photo does, so a browser may keep it.</summary>
    internal static string PhotoUrl(Guid userId, DateTimeOffset updatedAt) => $"/v1/users/{userId}/photo?v={updatedAt.ToUnixTimeMilliseconds()}";

    private static async Task<Results<Ok<AccountDetails>, NotFound>> GetAccount(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || await DetailsAsync(db, tenant, userId, cancellationToken) is not { } details)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(details);
    }

    private static async Task<AccountDetails?> DetailsAsync(ScreenHireDbContext db, ITenantContext tenant, Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName }).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var photo = await db.UserPhotos.AsNoTracking().Where(p => p.UserId == userId).Select(p => (DateTimeOffset?)p.UpdatedAt).SingleOrDefaultAsync(cancellationToken);
        var preferences = tenant.MemberId is { } memberId
            ? await db.MemberPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.MemberId == memberId, cancellationToken)
            : null;
        var email = user.Email ?? "";
        return new AccountDetails(
            email, AuthEndpoints.UsableName(user.DisplayName, email), user.DisplayName ?? email, photo is { } at ? PhotoUrl(userId, at) : null,
            preferences?.TimeZone, preferences?.Language, preferences?.DateFormat ?? DateFormat.Browser, preferences?.ShortcutsEnabled, preferences?.ConfirmMoves);
    }

    private static async Task<Results<Ok<AccountDetails>, NotFound, ValidationProblem>> UpdateProfile(
        UpdateProfileRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) is not { } user)
        {
            return TypedResults.NotFound();
        }

        var problems = new Dictionary<string, string[]>();
        var name = AuthEndpoints.UsableName(request.Name, user.Email);
        if (name is null)
        {
            problems["name"] = ["Enter your name, up to 200 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZone) && !IsIanaZone(request.TimeZone))
        {
            problems["timeZone"] = ["Choose a time zone from the list."];
        }

        if (!string.IsNullOrWhiteSpace(request.Language) && !IsLanguage(request.Language))
        {
            problems["language"] = ["Choose a language from the list."];
        }

        if (!Enum.IsDefined(request.DateFormat))
        {
            problems["dateFormat"] = ["Choose a date format from the list."];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        user.DisplayName = name;
        if (await PreferencesAsync(db, tenant, time, cancellationToken) is { } preferences)
        {
            preferences.SetProfile(request.TimeZone?.Trim(), request.Language?.Trim(), request.DateFormat, time.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await DetailsAsync(db, tenant, userId, cancellationToken))!);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> FillTimeZone(
        FillTimeZoneRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!IsIanaZone(request.TimeZone))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("timeZone", "Send an IANA time zone, such as Europe/London."));
        }

        if (await PreferencesAsync(db, tenant, time, cancellationToken) is not { } preferences)
        {
            return TypedResults.NotFound();
        }

        if (preferences.FillTimeZone(request.TimeZone.Trim(), time.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AccountDetails>, NotFound>> UpdateAccountPreferences(
        UpdateAccountPreferencesRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId || await PreferencesAsync(db, tenant, time, cancellationToken) is not { } preferences)
        {
            return TypedResults.NotFound();
        }

        preferences.SetHabits(request.ShortcutsEnabled, request.ConfirmMoves, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await DetailsAsync(db, tenant, userId, cancellationToken))!);
    }

    private static async Task<Results<Ok<AccountNotifications>, NotFound>> GetAccountNotifications(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (await NotificationsAsync(db, tenant, cancellationToken) is not { } notifications)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(notifications);
    }

    private static async Task<AccountNotifications?> NotificationsAsync(ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant is not { MemberId: { } memberId, UserId: { } userId })
        {
            return null;
        }

        var settings = await db.OrganizationMembers.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.Notifications).SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return null;
        }

        var preferences = await db.MemberPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.MemberId == memberId, cancellationToken);
        var permissions = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        var alerts = await OrganizationAlertDefaults.ReadAsync(db, cancellationToken);
        var (slackAvailable, link) = await SlackAsync(db, userId, cancellationToken);
        // Someone who handles billing gets the trial's reminders, which start at Instantly, so the row starts there for them and
        // Off is a choice they can save; for anyone else it follows the organization's AI usage email.
        var handlesBilling = HandlesBilling(permissions);
        var rows = NoticeRows.All.Where(r => r.IsFor(permissions)).Select(row => new NoticeRowState(
            row.Kind,
            row.Toasts,
            PersonNotices.Email(preferences, row.Kind, alerts, handlesBilling),
            NoticeDefaults.Email(row.Kind, alerts, handlesBilling),
            !row.HasSlack ? null : row.Kind switch
            {
                // The Slack link keeps these two, as the direct messages have always read them.
                NoticeKind.Mentions => link?.Mentions ?? true,
                NoticeKind.Tasks => link?.Tasks ?? true,
                _ => PersonNotices.Slack(preferences, row.Kind, alerts),
            },
            row.Scoped));
        return new AccountNotifications(
            preferences?.Scope ?? NoticeScope.MyJobs, !settings.Toasts, settings.CloseToasts, settings.Off, [.. rows], slackAvailable, link is not null);
    }

    private static async Task<(bool Available, SlackAccountLink? Link)> SlackAsync(
        ScreenHireDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var installation = await db.SlackInstallations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (installation is not { IsDisconnected: false })
        {
            return (false, null);
        }

        return (true, await db.SlackAccountLinks.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken));
    }

    private static async Task<Results<Ok<AccountNotifications>, NotFound, ValidationProblem>> UpdateAccountNotifications(
        UpdateAccountNotificationsRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        // A PUT replaces the grid, so missing lists are bad input rather than "nothing".
        if (request.ToastsOff is null || request.Rows is null)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("rows", "Send every row of the grid and the toasts that are off."));
        }

        // The string enum converter also reads numbers, so a value outside the lists would bind; Instantly, Daily digest and
        // Off are the only email choices there are.
        if (!Enum.IsDefined(request.Scope))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("scope", "Choose My jobs or Everything I can see."));
        }

        if (request.ToastsOff.Any(kind => !Enum.IsDefined(kind)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("toastsOff", "Name only kinds of toast there are."));
        }

        if (request.Rows.Any(row => !Enum.IsDefined(row.Kind) || !Enum.IsDefined(row.Email)))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("rows", "Choose Instantly, Daily digest or Off for each row of the grid."));
        }

        if (request.ToastsOff.Distinct().Count() != request.ToastsOff.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("toastsOff", "Name each kind of toast once."));
        }

        if (request.Rows.Select(r => r.Kind).Distinct().Count() != request.Rows.Count)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("rows", "Send each row once."));
        }

        if (tenant is not { MemberId: { } memberId, UserId: { } userId }
            || await db.OrganizationMembers.SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken) is not { } member
            || await PreferencesAsync(db, tenant, time, cancellationToken) is not { } preferences)
        {
            return TypedResults.NotFound();
        }

        var permissions = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        var alerts = await OrganizationAlertDefaults.ReadAsync(db, cancellationToken);
        var handlesBilling = HandlesBilling(permissions);
        var visible = NoticeRows.All.Where(r => r.IsFor(permissions)).Select(r => r.Kind).ToHashSet();
        var (_, link) = await SlackAsync(db, userId, cancellationToken);

        // Rows the person does not see keep what they held, so losing and regaining a permission loses no choice.
        var choices = preferences.Notices.Choices.Where(c => !visible.Contains(c.Kind)).ToList();
        foreach (var row in request.Rows.Where(r => visible.Contains(r.Kind)))
        {
            var hasOwnSlack = NoticeRows.For(row.Kind).HasSlack && row.Kind is not (NoticeKind.Mentions or NoticeKind.Tasks);
            choices.Add(new NoticeChoice(row.Kind, row.Email, hasOwnSlack ? row.Slack : null));
            if (link is not null && row.Slack is { } slack)
            {
                if (row.Kind == NoticeKind.Mentions)
                {
                    link.Mentions = slack;
                }
                else if (row.Kind == NoticeKind.Tasks)
                {
                    link.Tasks = slack;
                }
            }
        }

        var kept = PersonNotices.Normalize(
            choices.Where(c => visible.Contains(c.Kind)),
            kind => NoticeDefaults.Email(kind, alerts, handlesBilling),
            kind => NoticeDefaults.Slack(kind, alerts));
        var hidden = choices.Where(c => !visible.Contains(c.Kind));
        preferences.SetNotices(request.Scope, new NoticeChoices { Choices = [.. kept.Choices, .. hidden] }, time.GetUtcNow());
        member.SetNotifications(new NotificationSettings { Toasts = !request.PauseToasts, CloseToasts = request.CloseToasts, Off = request.ToastsOff });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await NotificationsAsync(db, tenant, cancellationToken))!);
    }

    private static async Task<Results<Ok<AccountSignIn>, NotFound>> GetAccountSignIn(
        HttpContext context, UserManager<ApplicationUser> users, ExternalProviders providers, IServiceProviderIsService services)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.NotFound();
        }

        var logins = await users.GetLoginsAsync(user);
        var passkeys = await users.GetPasskeysAsync(user);
        return TypedResults.Ok(new AccountSignIn(
            user.Email ?? "",
            services.IsService(typeof(MagicLinks)),
            [.. logins.Select(l => new LinkedSignIn(l.LoginProvider, providers.Find(l.LoginProvider)?.DisplayName ?? l.ProviderDisplayName ?? l.LoginProvider))
                .DistinctBy(l => l.Id)],
            passkeys.Count,
            PasskeyLimits.PerUser));
    }

    private static async Task<Results<Ok<AccountDetails>, NotFound, ValidationProblem>> UploadPhoto(
        UploadPhotoRequest request, ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId)
        {
            return TypedResults.NotFound();
        }

        var bytes = new byte[UserPhoto.MaxBytes];
        if (request.Data is null || !Convert.TryFromBase64String(request.Data, bytes, out var length))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("data", "Choose a PNG, JPEG or WebP picture of at most 512 KB."));
        }

        var content = bytes[..length];
        if (PhotoType(content) is not { } contentType)
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem("data", "Choose a PNG, JPEG or WebP picture."));
        }

        var photo = await db.UserPhotos.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (photo is null)
        {
            photo = new UserPhoto { UserId = userId };
            db.UserPhotos.Add(photo);
        }

        photo.Content = content;
        photo.ContentType = contentType;
        photo.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await DetailsAsync(db, tenant, userId, cancellationToken))!);
    }

    private static async Task<Results<Ok<AccountDetails>, NotFound>> RemovePhoto(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId)
        {
            return TypedResults.NotFound();
        }

        await db.UserPhotos.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.Ok((await DetailsAsync(db, tenant, userId, cancellationToken))!);
    }

    // Only the person themselves and people who share the current organization with them: the members filter starts from this
    // organization, and the users table has no row-level security (docs/architecture/sign-in-and-tenancy.md, "Listing people").
    private static async Task<Results<FileContentHttpResult, NotFound>> GetUserPhoto(
        Guid userId, HttpContext context, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var shares = userId == tenant.UserId
            || (tenant.OrganizationId is { } organizationId
                && await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken));
        var photo = shares ? await db.UserPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken) : null;
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        // The address carries the photo's version, so a browser may keep it; private, because it is behind a sign-in.
        context.Response.Headers.CacheControl = "private, max-age=86400";
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return TypedResults.File(photo.Content, photo.ContentType, lastModified: photo.UpdatedAt);
    }

    private static async Task<Results<NoContent, NotFound>> SignOutEverywhere(
        HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, RealtimeConnections connections)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.NotFound();
        }

        // A new stamp ends every session at its next check (ScreenHire:Auth:SecurityStampInterval); this one is signed in
        // again at once, so the person stays here. Live connections close now and the dashboard opens its own again.
        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        connections.CloseUser(user.Id);
        return TypedResults.NoContent();
    }

    /// <summary>The caller's preferences row here, made on the first save (tracked, so the caller saves it).</summary>
    private static async Task<MemberPreferences?> PreferencesAsync(
        ScreenHireDbContext db, ITenantContext tenant, TimeProvider time, CancellationToken cancellationToken)
    {
        if (tenant is not { OrganizationId: { } organizationId, MemberId: { } memberId, UserId: { } userId })
        {
            return null;
        }

        var preferences = await db.MemberPreferences.SingleOrDefaultAsync(p => p.MemberId == memberId, cancellationToken);
        if (preferences is null)
        {
            preferences = MemberPreferences.Create(organizationId, memberId, userId, time.GetUtcNow());
            db.MemberPreferences.Add(preferences);
        }

        return preferences;
    }

    // The same rule as the trial's reminders in the worker (EmailTrialEnding), so the grid shows the default the emails follow.
    private static bool HandlesBilling(IReadOnlySet<Permission> permissions) => permissions.Contains(Permission.ManageBilling);

    private static bool IsIanaZone(string? timeZone) =>
        timeZone is { Length: > 0 and <= MemberPreferences.MaxTimeZoneLength }
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out var zone) && zone.HasIanaId;

    private static bool IsLanguage(string language)
    {
        if (language.Length > MemberPreferences.MaxLanguageLength)
        {
            return false;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language.Trim(), predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>The picture's type, read from its first bytes: PNG, JPEG or WebP, or null for anything else.</summary>
    internal static string? PhotoType(ReadOnlySpan<byte> content) => content switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        _ => null,
    };
}
