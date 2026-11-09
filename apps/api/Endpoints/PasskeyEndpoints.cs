using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Passkeys (WebAuthn) as a way to sign in, and the signed-in person's own list of them. The ceremonies are
/// <c>SignInManager</c>'s: it makes the options, keeps the challenge in the <c>sh.passkey</c> cookie, and checks the
/// browser's answer against the configured relying party and origin (<c>AuthenticationSetup</c>). Nothing here is tenant
/// data: a passkey belongs to the person, whatever organizations they are in. Every POST and DELETE needs the
/// antiforgery header; the two anonymous routes have a per-IP rate limit of their own.
/// </summary>
internal static partial class PasskeyEndpoints
{
    private const string LogCategory = "ScreenHire.Api.Passkeys";

    private const string NotAdded = "That passkey could not be added. Try again, or use another device.";

    public static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder app)
    {
        // The body cap stops an anonymous caller making the API buffer and parse megabytes before the validator's length
        // check refuses them; a credential with its attestation is a few kilobytes.
        var group = app.MapGroup("/v1/auth/passkeys").WithTags("Auth").RequireAntiforgeryToken()
            .RequireRateLimiting(AuthenticationSetup.RateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(PasskeyLimits.BodyBytes));

        // Sign-in comes before anyone is known, so these two are open; the closest rate-limit metadata wins.
        group.MapPost("/sign-in/options", MakeSignInOptions).WithName("MakePasskeySignInOptions").AllowAnonymous()
            .RequireRateLimiting(AuthenticationSetup.PasskeyRateLimitPolicy);
        group.MapPost("/sign-in", SignIn).WithName("SignInWithPasskey").AllowAnonymous()
            .RequireRateLimiting(AuthenticationSetup.PasskeyRateLimitPolicy);

        group.MapGet("/", List).WithName("ListPasskeys").Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/register/options", MakeRegistrationOptions).WithName("MakePasskeyRegistrationOptions")
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/register", Register).WithName("RegisterPasskey").Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("/{passkeyId}", Remove).WithName("RemovePasskey").Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    // No username: the authenticator offers the passkeys it holds for this site (a discoverable credential).
    private static async Task<Ok<PasskeyOptions>> MakeSignInOptions(SignInManager<ApplicationUser> signIn) =>
        TypedResults.Ok(new PasskeyOptions(await signIn.MakePasskeyRequestOptionsAsync(user: null)));

    // The credential names its own user, so the answer to anything wrong is one refusal: it never says whether the
    // credential was known, whose it was or why it failed.
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> SignIn(
        PasskeySignInRequest request, IValidator<PasskeySignInRequest> validator, HttpContext context,
        SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, ScreenHireDbContext db,
        TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        // PasskeySignInAsync would also sign in, but it hides which passkey signed in, and the list shows when each was last used.
        PasskeyAssertionResult<ApplicationUser> assertion;
        try
        {
            assertion = await signIn.PerformPasskeyAssertionAsync(request.CredentialJson);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            // InvalidOperationException: no ceremony is underway (no challenge cookie, or one that has expired or was used up).
            // JsonException: a body that is not the credential shape. Either way the answer is the one refusal; the type is
            // logged, never the body, so a store or database fault does not pass unseen.
            LogRefused(loggers.CreateLogger(LogCategory), "sign-in", exception.GetType().Name);
            return TypedResults.Unauthorized();
        }

        if (!assertion.Succeeded || assertion.User is not { } user
            || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            return TypedResults.Unauthorized();
        }

        // Stores the passkey's new signature counter, which is how a cloned authenticator is noticed.
        if (!(await users.AddOrUpdatePasskeyAsync(user, assertion.Passkey)).Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        var credentialId = assertion.Passkey.CredentialId;
        var now = time.GetUtcNow();
        await db.UserPasskeys.Where(p => p.CredentialId == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, now), cancellationToken);

        await AuthEndpoints.SignInAsync(context, signIn, user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PasskeyList>, UnauthorizedHttpResult>> List(
        HttpContext context, UserManager<ApplicationUser> users, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await db.UserPasskeys.AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => new { p.CredentialId, p.Data.Name, p.Data.CreatedAt, p.LastUsedAt })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new PasskeyList(
            [.. rows.OrderByDescending(p => p.CreatedAt).Select(p =>
                new PasskeySummary(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name ?? PasskeyLimits.DefaultName, p.CreatedAt, p.LastUsedAt))],
            PasskeyLimits.PerUser));
    }

    private static async Task<Results<Ok<PasskeyOptions>, Conflict<ProblemDetails>, UnauthorizedHttpResult>> MakeRegistrationOptions(
        HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if ((await users.GetPasskeysAsync(user)).Count >= PasskeyLimits.PerUser)
        {
            return AtLimit();
        }

        var email = user.Email ?? user.UserName ?? "";
        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = email,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? email : user.DisplayName,
        });
        return TypedResults.Ok(new PasskeyOptions(options));
    }

    private static async Task<Results<Ok<PasskeySummary>, ValidationProblem, Conflict<ProblemDetails>, UnauthorizedHttpResult>> Register(
        RegisterPasskeyRequest request, IValidator<RegisterPasskeyRequest> validator, HttpContext context,
        UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ScreenHireDbContext db, ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if ((await users.GetPasskeysAsync(user)).Count >= PasskeyLimits.PerUser)
        {
            return AtLimit();
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            // No ceremony is underway, or the body is not the credential shape: see SignIn.
            LogRefused(loggers.CreateLogger(LogCategory), "registration", exception.GetType().Name);
            return NotAddedProblem();
        }

        // The options were made for a user; if the browser has since signed in as someone else, the answer is not theirs to add.
        if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
        {
            return NotAddedProblem();
        }

        var passkey = attestation.Passkey;
        passkey.Name = PasskeyLimits.CleanName(request.Name);
        var stamp = await users.GetSecurityStampAsync(user);
        // The count and the add run under the per-user lock Remove takes, so two registrations at once cannot both see
        // nine passkeys and leave eleven. The count above only spares the person a ceremony that would be refused.
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockUserAsync(db, user, cancellationToken);
            if ((await users.GetPasskeysAsync(user)).Count >= PasskeyLimits.PerUser)
            {
                return AtLimit();
            }

            if (!(await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded)
            {
                return NotAddedProblem();
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await KeepSessionAsync(signIn, users, user, stamp);
        return TypedResults.Ok(new PasskeySummary(WebEncoders.Base64UrlEncode(passkey.CredentialId), passkey.Name, passkey.CreatedAt, null));
    }

    // Another person's passkey, one that does not exist and a malformed id are all the same 404.
    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>, UnauthorizedHttpResult>> Remove(
        string passkeyId, HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        ExternalProviders providers, IServiceProvider services, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if (!TryDecode(passkeyId, out var credentialId)
            || await users.GetPasskeyAsync(user, credentialId) is null)
        {
            return TypedResults.NotFound();
        }

        // The count and the delete run under one per-user advisory lock, so two removals at once cannot both see two
        // passkeys and leave none.
        var stamp = await users.GetSecurityStampAsync(user);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockUserAsync(db, user, cancellationToken);
            var passkeys = (await users.GetPasskeysAsync(user)).Count;
            var usableLogins = (await users.GetLoginsAsync(user)).Count(login => providers.Find(login.LoginProvider) is not null);
            if (!PasskeyLimits.MayRemove(passkeys, usableLogins, services.GetService<MagicLinks>() is not null))
            {
                return TypedResults.Conflict(new ProblemDetails
                {
                    Title = "Last way to sign in",
                    Detail = "This is the only way you have to sign in. Add another passkey or a sign-in method first.",
                    Status = StatusCodes.Status409Conflict,
                });
            }

            if (!(await users.RemovePasskeyAsync(user, credentialId)).Succeeded)
            {
                return TypedResults.NotFound();
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await KeepSessionAsync(signIn, users, user, stamp);
        return TypedResults.NoContent();
    }

    // Adding or removing a credential may rotate the security stamp, and the cookie's validator would then sign the
    // person out of this very session. Refresh it, so it carries on.
    private static async Task KeepSessionAsync(
        SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, ApplicationUser user, string? stampBefore)
    {
        if (!string.Equals(await users.GetSecurityStampAsync(user), stampBefore, StringComparison.Ordinal))
        {
            await signIn.RefreshSignInAsync(user);
        }
    }

    // A transaction-scoped advisory lock on the person: their passkey count stays still until the transaction ends.
    private static async Task LockUserAsync(ScreenHireDbContext db, ApplicationUser user, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({user.Id.ToString()}, 0))", cancellationToken);

    private static bool TryDecode(string id, out byte[] credentialId)
    {
        try
        {
            credentialId = WebEncoders.Base64UrlDecode(id);
            return credentialId.Length is > 0 and <= 1024;
        }
        catch (FormatException)
        {
            credentialId = [];
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A passkey {Operation} was refused before it was checked: {ExceptionType}")]
    private static partial void LogRefused(ILogger logger, string operation, string exceptionType);

    private static ValidationProblem NotAddedProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["credentialJson"] = [NotAdded] });

    private static Conflict<ProblemDetails> AtLimit() =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Too many passkeys",
            Detail = $"You can keep {PasskeyLimits.PerUser} passkeys. Remove one to add another.",
            Status = StatusCodes.Status409Conflict,
        });
}
