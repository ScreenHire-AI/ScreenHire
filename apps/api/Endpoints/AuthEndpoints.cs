using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Realtime;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// Sign-in, onboarding and sign-out for the recruiter dashboard. There are no passwords: a person signs in with an
/// identity provider or an emailed link, and the first sign-in creates their account. Every POST needs the antiforgery
/// header and counts against the per-IP rate limit.
/// </summary>
internal static class AuthEndpoints
{
    private const string AlreadyUsedLink = "This sign-in link has expired or has already been used. Ask for a new one.";

    private const string UnusableInvitation =
        "This invitation has expired, has been withdrawn or has already been used. Ask the person who invited you for a new one.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/auth").WithTags("Auth");

        group.MapGet("/providers", GetSignInMethods).WithName("GetSignInMethods").AllowAnonymous();
        group.MapGet("/me", GetCurrentUser).WithName("GetCurrentUser").AllowAnonymous().Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/organizations", ListMyOrganizations).WithName("ListMyOrganizations").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/challenge/{provider}", ChallengeProvider).WithName("ChallengeProvider").AllowAnonymous()
            .RequireRateLimiting(AuthenticationSetup.RateLimitPolicy);
        // Where a provider's handler sends the browser once it has validated the response; a redirect, not an API call.
        group.MapGet("/callback", CompleteExternalSignIn).WithName("CompleteExternalSignIn").AllowAnonymous()
            .ExcludeFromDescription();

        var mutations = group.MapGroup("").RequireAntiforgeryToken().RequireRateLimiting(AuthenticationSetup.RateLimitPolicy);
        mutations.MapPost("/magic-link", RequestMagicLink).WithName("RequestMagicLink").AllowAnonymous();
        mutations.MapPost("/magic-link/verify", VerifyMagicLink).WithName("VerifyMagicLink").AllowAnonymous();
        mutations.MapPost("/onboarding", CompleteOnboarding).WithName("CompleteOnboarding").Produces(StatusCodes.Status401Unauthorized);
        mutations.MapPost("/invitations/accept", AcceptInvitation).WithName("AcceptInvitation").AllowAnonymous();
        mutations.MapPost("/switch-organization", SwitchOrganization).WithName("SwitchOrganization").Produces(StatusCodes.Status401Unauthorized);
        // Cast: a handler taking only HttpContext would otherwise bind as a RequestDelegate and drop its result.
        mutations.MapPost("/sign-out", (Delegate)SignOut).WithName("SignOut").AllowAnonymous();

        return app;
    }

    // Also hands out the antiforgery token the sign-in page's POSTs need.
    private static Ok<SignInMethods> GetSignInMethods(HttpContext context, ExternalProviders providers, IServiceProviderIsService services)
    {
        Antiforgery.IssueRequestToken(context);
        return TypedResults.Ok(new SignInMethods(
            [.. providers.All.Select(p => new SignInProvider(p.Id, p.DisplayName))],
            services.IsService(typeof(MagicLinks))));
    }

    // Signed in or not, the answer carries a fresh antiforgery token: the dashboard asks on every page load, and a page
    // such as the email-link one posts before anyone is signed in.
    private static async Task<Results<Ok<CurrentUser>, UnauthorizedHttpResult>> GetCurrentUser(
        HttpContext context, UserManager<ApplicationUser> users, ITenantContext tenant, ScreenHireDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(context.User);
        Antiforgery.IssueRequestToken(context);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await DescribeAsync(user, tenant, db, cancellationToken));
    }

    private static Results<ChallengeHttpResult, NotFound> ChallengeProvider(
        string provider, string? returnUrl, ExternalProviders providers, SignInManager<ApplicationUser> signIn)
    {
        if (providers.Find(provider) is null)
        {
            return TypedResults.NotFound();
        }

        var callback = $"/v1/auth/callback?returnUrl={Uri.EscapeDataString(ReturnUrls.Sanitize(returnUrl))}";
        return TypedResults.Challenge(signIn.ConfigureExternalAuthenticationProperties(provider, callback), [provider]);
    }

    private static async Task<RedirectHttpResult> CompleteExternalSignIn(
        string? returnUrl, HttpContext context, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
        ExternalProviders providers, IOptions<DomainOptions> domains, ScreenHireDbContext db, TimeProvider time)
    {
        var appDomains = domains.Value;
        RedirectHttpResult Failed(string reason) => TypedResults.Redirect(appDomains.AppUrl($"/sign-in?error={reason}").ToString());

        var login = await signIn.GetExternalLoginInfoAsync();
        try
        {
            if (login is null || providers.Find(login.LoginProvider) is not { } provider)
            {
                return Failed("provider");
            }

            var existing = await signIn.ExternalLoginSignInAsync(login.LoginProvider, login.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (!existing.Succeeded)
            {
                // A new identity: link it to the user with the same email, or create one, but only when the provider vouches
                // for the address. Otherwise anyone could claim someone else's account by putting their email on theirs.
                var email = login.Principal.FindFirstValue("email");
                if (email is null || !EmailVerification.IsVerified(provider.Kind, login.Principal))
                {
                    return Failed("email-unverified");
                }

                var user = await FindOrCreateUserAsync(users, db, email, login.Principal.FindFirstValue("name"), time.GetUtcNow());
                if (user is null || !await TryAddLoginAsync(users, db, user, login))
                {
                    return Failed("provider");
                }

                await signIn.SignInAsync(user, isPersistent: true, login.LoginProvider);
            }
        }
        finally
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
        }

        return TypedResults.Redirect(appDomains.AppUrl(ReturnUrls.Sanitize(returnUrl)).ToString());
    }

    // The same reply whether or not the address has an account, and whether or not its link limit was reached.
    private static async Task<Results<Accepted, ValidationProblem, NotFound>> RequestMagicLink(
        MagicLinkRequest request, IValidator<MagicLinkRequest> validator, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<MagicLinks>() is not { } magicLinks)
        {
            return TypedResults.NotFound(); // no email sender is configured, so the sign-in page hides the form
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        await magicLinks.SendAsync(request.Email, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, NotFound>> VerifyMagicLink(
        MagicLinkVerification request, IValidator<MagicLinkVerification> validator, IServiceProvider services,
        HttpContext context, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ScreenHireDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (services.GetService<MagicLinks>() is not { } magicLinks)
        {
            return TypedResults.NotFound();
        }

        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await magicLinks.RedeemAsync(request.Token, cancellationToken) is not { } email)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["token"] = [AlreadyUsedLink] });
        }

        var user = await FindOrCreateUserAsync(users, db, email, null, time.GetUtcNow())
            ?? throw new InvalidOperationException("Could not create the user for a redeemed sign-in link.");
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true; // following the link proves the address
            await users.UpdateAsync(user);
        }

        await SignInAsync(context, signIn, user);
        return TypedResults.NoContent();
    }

    // Every organization the signed-in user belongs to, for the organization switcher. Starts from their own memberships,
    // which the filter and row-level security show whatever the current tenant is.
    // A custom role is named only in the organization the user works in: row-level security keeps every other
    // organization's roles from them until they switch (ADR-0014).
    private static async Task<Ok<List<MyOrganization>>> ListMyOrganizations(
        ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var memberships = await (
            from member in db.OrganizationMembers.AsNoTracking()
            where member.UserId == tenant.UserId
            join organization in db.Organizations on member.OrganizationId equals organization.Id
            orderby organization.Name
            select new { organization.Id, organization.Name, organization.Slug, member.Role, member.CustomRoleId })
            .ToListAsync(cancellationToken);
        var names = tenant.OrganizationId is null
            ? new Dictionary<Guid, string>()
            : await RoleAccess.CustomNamesAsync(db, cancellationToken);
        return TypedResults.Ok(memberships
            .Select(m => new MyOrganization(
                m.Id, m.Name, m.Slug, m.Role,
                m.Role == OrganizationRole.Custom ? names.GetValueOrDefault(m.CustomRoleId ?? Guid.Empty) : m.Role.ToString()))
            .ToList());
    }

    // Remembered on the user (ApplicationUser.ActiveOrganizationId), so it holds on every device until they switch again.
    // Not a member is 404, like any other organization's id. Their live-update connections into the previous organization
    // close, on every device, so each reconnects into the one its next request reads.
    private static async Task<Results<Ok<CurrentUser>, NotFound, UnauthorizedHttpResult>> SwitchOrganization(
        SwitchOrganizationRequest request, HttpContext context, UserManager<ApplicationUser> users, TenantContext tenant,
        ScreenHireDbContext db, RealtimeConnections connections, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.OrganizationMembers.AnyAsync(m => m.UserId == user.Id && m.OrganizationId == request.OrganizationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var previous = tenant.OrganizationId;
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ActiveOrganizationId, request.OrganizationId), cancellationToken);
        tenant.SetOrganization(request.OrganizationId);
        if (previous is { } left && left != request.OrganizationId)
        {
            connections.Close(user.Id, left);
        }

        return TypedResults.Ok(await DescribeAsync(user, tenant, db, cancellationToken));
    }

    // The emailed link signs in as the invited address, whoever this browser was signed in as: following it proves the
    // address, as a sign-in link does. The token names its organization (Invitations), which becomes the tenant before
    // the lookup; only a hash match inside that organization's rows accepts anything. The user is found or created before
    // the transaction, because a unique violation inside it would abort the whole transaction.
    private static async Task<Results<Ok<CurrentUser>, ValidationProblem>> AcceptInvitation(
        InvitationAcceptance request, IValidator<InvitationAcceptance> validator, HttpContext context,
        UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, TenantContext tenant, ScreenHireDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (Invitations.OrganizationOf(request.Token) is not { } organizationId)
        {
            return InvitationRefused();
        }

        tenant.SetOrganization(organizationId);
        var hash = MagicLinks.Hash(request.Token);
        var now = time.GetUtcNow();
        var invitation = await db.OrganizationInvitations.AsNoTracking()
            .SingleOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, cancellationToken);
        if (invitation is null)
        {
            return InvitationRefused();
        }

        var user = await FindOrCreateUserAsync(users, db, invitation.Email, null, now)
            ?? throw new InvalidOperationException("Could not create the user for an accepted invitation.");
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        tenant.SetUser(user.Id);
        if (!await JoinAsync(db, invitation, user.Id, now, cancellationToken))
        {
            return InvitationRefused(); // a second click, or a revoke, got there between the read and the update
        }

        await SignInAsync(context, signIn, user);
        return TypedResults.Ok(await DescribeAsync(user, tenant, db, cancellationToken));
    }

    // One transaction under the organization's member lock: use up the invitation (one conditional UPDATE, so two clicks
    // cannot both succeed), add the membership with its log entry, and make the organization the user's active one. A
    // person who is already a member keeps the role they have.
    private static async Task<bool> JoinAsync(
        ScreenHireDbContext db, OrganizationInvitation invitation, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Invitations.LockMembersAsync(db, invitation.OrganizationId, cancellationToken);
        var accepted = await db.OrganizationInvitations
            .Where(i => i.Id == invitation.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now).SetProperty(i => i.AcceptedByUserId, userId), cancellationToken);
        if (accepted == 0)
        {
            return false;
        }

        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == invitation.OrganizationId && m.UserId == userId, cancellationToken))
        {
            // An open invitation keeps its custom role from being deleted, so this finds it; refuse rather than guess if not.
            if (await RoleAccess.ForRoleAsync(db, invitation.Role, invitation.CustomRoleId, cancellationToken) is not { } role)
            {
                return false;
            }

            db.OrganizationMembers.Add(OrganizationMember.Create(
                invitation.OrganizationId, userId, role.Role, now, role.CustomRoleId, invitation.JobAccess));
            db.MembershipEvents.Add(new MembershipEvent
            {
                OrganizationId = invitation.OrganizationId,
                Kind = MembershipEventKind.Joined,
                ActorUserId = userId,
                MemberUserId = userId,
                InvitationId = invitation.Id,
                Role = role.Role,
                CustomRoleId = role.CustomRoleId,
                CustomRoleName = role.Role == OrganizationRole.Custom ? role.Name : null,
                JobAccess = invitation.JobAccess,
                OccurredAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ActiveOrganizationId, invitation.OrganizationId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static ValidationProblem InvitationRefused() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["token"] = [UnusableInvitation] });

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, Conflict<ProblemDetails>, UnauthorizedHttpResult>> CompleteOnboarding(
        OnboardingRequest request, IValidator<OnboardingRequest> validator, HttpContext context, UserManager<ApplicationUser> users,
        TenantContext tenant, ScreenHireDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        if (await validator.ProblemsAsync(request, cancellationToken) is { } problems)
        {
            return TypedResults.ValidationProblem(problems);
        }

        if (await users.GetUserAsync(context.User) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if (tenant.OrganizationId is not null)
        {
            return AlreadyOnboarded();
        }

        // One transaction holding a per-user advisory lock, so a double submit cannot create two organizations: the
        // second request waits, then finds the first one's membership. Inside the execution strategy, because Aspire's
        // retrying strategy refuses transactions started outside it.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts clean: the failed attempt's organization and membership must not be inserted again.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({user.Id.ToString()}, 0))", cancellationToken);
            if (await db.OrganizationMembers.AnyAsync(m => m.UserId == user.Id, cancellationToken))
            {
                return AlreadyOnboarded();
            }

            return await CreateOrganizationAsync(request, user, tenant, db, time, transaction, cancellationToken);
        });
    }

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, Conflict<ProblemDetails>, UnauthorizedHttpResult>> CreateOrganizationAsync(
        OnboardingRequest request, ApplicationUser user, TenantContext tenant, ScreenHireDbContext db, TimeProvider time,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        if (await db.Organizations.AnyAsync(o => o.Slug == request.Slug, cancellationToken))
        {
            return SlugTaken();
        }

        var organization = new Organization
        {
            Name = request.OrganizationName.Trim(),
            Slug = request.Slug,
            // Data residency (ADR-0007) is not chosen at sign-up yet; every organization starts in the EU region.
            HomeRegion = "eu",
        };
        // The new organization is the tenant from here on, so the membership row passes row-level security's WITH CHECK.
        tenant.SetOrganization(organization.Id);
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.Id, user.Id, OrganizationRole.Owner, time.GetUtcNow()));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return SlugTaken(); // another sign-up took the slug between the check and the insert
        }

        return TypedResults.Ok(await DescribeAsync(user, tenant, db, cancellationToken));
    }

    private static Conflict<ProblemDetails> AlreadyOnboarded() =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Already onboarded",
            Detail = "You already belong to an organization.",
            Status = StatusCodes.Status409Conflict,
        });

    private static async Task<NoContent> SignOut(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        Antiforgery.IssueRequestToken(context);
        return TypedResults.NoContent();
    }

    private static ValidationProblem SlugTaken() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["slug"] = ["That address is taken. Choose another."] });

    // Two first sign-ins for one new address can both miss the lookup. The unique email index lets only one of them
    // create the user, and the other then finds it.
    private static async Task<ApplicationUser?> FindOrCreateUserAsync(
        UserManager<ApplicationUser> users, ScreenHireDbContext db, string email, string? displayName, DateTimeOffset now) =>
        await users.FindByEmailAsync(email)
        ?? await CreateUserAsync(users, db, email, displayName, now)
        ?? await users.FindByEmailAsync(email);

    // The same identity finishing two sign-ins at once: the second insert hits the login's primary key.
    private static async Task<bool> TryAddLoginAsync(
        UserManager<ApplicationUser> users, ScreenHireDbContext db, ApplicationUser user, UserLoginInfo login)
    {
        try
        {
            return (await users.AddLoginAsync(user, login)).Succeeded;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear(); // the failed row stays tracked and would be inserted again by the next save
            return false;
        }
    }

    private static async Task<ApplicationUser?> CreateUserAsync(
        UserManager<ApplicationUser> users, ScreenHireDbContext db, string email, string? displayName, DateTimeOffset now)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true, // only verified addresses reach here
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            CreatedAt = now,
        };
        try
        {
            return (await users.CreateAsync(user)).Succeeded ? user : null;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Identity's uniqueness check passed, but a simultaneous sign-in inserted the same address first.
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Signs in and re-issues the antiforgery token: tokens are bound to the user, so the anonymous one stops working.
    private static async Task SignInAsync(HttpContext context, SignInManager<ApplicationUser> signIn, ApplicationUser user)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties { IsPersistent = true });
        context.User = principal;
        Antiforgery.IssueRequestToken(context);
    }

    private static async Task<CurrentUser> DescribeAsync(
        ApplicationUser user, ITenantContext tenant, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        CurrentOrganization? organization = null;
        if (tenant.OrganizationId is { } organizationId
            && await RoleAccess.ForMemberAsync(db, organizationId, user.Id, cancellationToken) is { } role)
        {
            var found = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == organizationId)
                .Select(o => new { o.Id, o.Name, o.Slug })
                .SingleAsync(cancellationToken);
            var jobAccess = await db.OrganizationMembers.AsNoTracking()
                .Where(m => m.OrganizationId == organizationId && m.UserId == user.Id)
                .Select(m => m.JobAccess)
                .SingleAsync(cancellationToken);
            organization = new CurrentOrganization(
                found.Id, found.Name, found.Slug, role.Role, role.CustomRoleId, role.Name,
                [.. RolePermissions.Permissions.Select(p => p.Permission).Where(role.Permissions.Contains)],
                JobAccessRules.SeesAllJobs(role.Role, jobAccess));
        }

        var email = user.Email ?? "";
        return new CurrentUser(user.Id, email, user.DisplayName ?? email, organization);
    }
}
