using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.SendingDomains;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's own sending domain (Settings, Sending domain; docs/architecture/email.md, ADR-0022). Every member
/// reads it; owners and admins add, check and remove it. Adding sets the domain up with the email provider and lists the DNS
/// records to add; checking looks each one up and asks the provider to confirm. Mail comes from the domain only once the
/// provider has confirmed it. With no provider on this deployment the page is hidden (<c>available</c> is false).
/// </summary>
internal static class SendingDomainEndpoints
{
    // Domains no company can own: mail providers' own, which would let an organization claim to send as Gmail.
    private static readonly string[] MailProviders =
    [
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "aol.com", "icloud.com",
        "me.com", "proton.me", "protonmail.com", "gmx.com", "mail.com", "zoho.com",
    ];

    public static IEndpointRouteBuilder MapSendingDomainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/organization/sending-domain").WithTags("Organization").RequireAuthorization();
        group.MapGet("", GetSendingDomain).WithName("GetOrganizationSendingDomain").Produces(StatusCodes.Status401Unauthorized);
        group.MapGet("/history", GetSendingDomainHistory).WithName("GetOrganizationSendingDomainHistory").Produces(StatusCodes.Status401Unauthorized);
        group.MapPut("", SetSendingDomain).WithName("SetOrganizationSendingDomain")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/check", CheckSendingDomain).WithName("CheckOrganizationSendingDomain")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        group.MapDelete("", DeleteSendingDomain).WithName("DeleteOrganizationSendingDomain")
            .RequireAntiforgeryToken()
            .RequirePermission(Permission.EditSettings)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<Ok<OrganizationSendingDomain>> GetSendingDomain(
        ScreenHireDbContext db, SendingDomainService service, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var domain = await db.SendingDomains.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var last = domain is null
            ? null
            : await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.SendingDomain, ChangeHistoryRoutes.SubjectId(tenant.OrganizationId!.Value), cancellationToken);
        return TypedResults.Ok(new OrganizationSendingDomain(
            service.IsAvailable, service.Mode == SendingDomainMode.Sandbox, domain?.ToDetail(),
            domain is null ? null : LastUpdate.FromChange(last, domain.CreatedAt)));
    }

    private static Task<Results<Ok<HistoryPage>, NotFound, ValidationProblem>> GetSendingDomainHistory(
        Guid? before, int? limit, ScreenHireDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
        ChangeHistoryRoutes.OrganizationPageAsync(ChangeSubjectKind.SendingDomain, before, limit, db, tenant, cancellationToken);

    private static async Task<Results<Ok<SendingDomainDetail>, NotFound, ValidationProblem, ProblemHttpResult>> SetSendingDomain(
        SetSendingDomainRequest request,
        ScreenHireDbContext db,
        SendingDomainService service,
        ITenantContext tenant,
        IOptions<DomainOptions> domains,
        IOptions<EmailOptions> email,
        CancellationToken cancellationToken)
    {
        if (!service.IsAvailable)
        {
            return TypedResults.NotFound(); // no email provider here, so the page is hidden
        }

        if (!SendingDomain.TryNormalize(request.Domain, out var domain))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "domain", "Enter a domain name such as example.com, without http:// or an @."));
        }

        if (IsNotYours(domain, domains.Value))
        {
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "domain", "Enter a domain your company owns. This one belongs to a mail provider or to ScreenHire."));
        }

        if (InboundUrl(service.Mode, domains.Value, email.Value) is not { } inbound)
        {
            return TypedResults.Problem(
                "Replies are not set up on this deployment: the inbound webhook has no address or no secret.", statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            var created = await service.SetAsync(db, tenant.OrganizationId!.Value, domain, inbound, cancellationToken);
            return TypedResults.Ok(created.ToDetail());
        }
        catch (SendingDomainProviderException exception)
        {
            return ProviderRefused(exception);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Only the unique index can see another organization's row (row-level security hides it from a query). Say no
            // more than that: who it is would be theirs to tell.
            return TypedResults.ValidationProblem(ValidationExtensions.Problem(
                "domain", "Another organization already uses this domain. Contact support if it is yours."));
        }
    }

    private static async Task<Results<Ok<SendingDomainDetail>, NotFound, ProblemHttpResult>> CheckSendingDomain(
        ScreenHireDbContext db, SendingDomainService service, CancellationToken cancellationToken)
    {
        var domain = await db.SendingDomains.SingleOrDefaultAsync(cancellationToken);
        if (domain is null || !service.IsAvailable)
        {
            return TypedResults.NotFound();
        }

        try
        {
            return TypedResults.Ok((await service.CheckAsync(db, domain, cancellationToken)).ToDetail());
        }
        catch (SendingDomainProviderException exception)
        {
            return ProviderRefused(exception);
        }
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteSendingDomain(
        ScreenHireDbContext db, SendingDomainService service, CancellationToken cancellationToken)
    {
        var domain = await db.SendingDomains.SingleOrDefaultAsync(cancellationToken);
        if (domain is null || !service.IsAvailable)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await service.RemoveAsync(db, domain, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (SendingDomainProviderException exception)
        {
            return ProviderRefused(exception);
        }
    }

    // The provider's own short reason, which SendingDomainProviderException guarantees is safe to show.
    private static ProblemHttpResult ProviderRefused(SendingDomainProviderException exception) =>
        TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "The email provider refused the request");

    /// <summary>A mail provider's domain, or ScreenHire's own (the product, its careers hosts and anything under them).</summary>
    internal static bool IsNotYours(string domain, DomainOptions domains)
    {
        bool Under(string parent) => parent.Length > 0 && (domain == parent || domain.EndsWith("." + parent, StringComparison.Ordinal));
        return MailProviders.Contains(domain) || Under(domains.BaseDomain.ToLowerInvariant()) || Under(domains.CareersDomain.ToLowerInvariant());
    }

    /// <summary>
    /// The address the provider posts a reply to, with the inbound secret as HTTP Basic credentials. Null when the
    /// deployment has no API host or, with SendGrid, no secret. The sandbox needs neither: nothing posts to it.
    /// </summary>
    internal static Uri? InboundUrl(SendingDomainMode mode, DomainOptions domains, EmailOptions email)
    {
        if (mode == SendingDomainMode.Sandbox)
        {
            return new Uri("https://sandbox.example.test" + EmailWebhookEndpoints.InboundPath);
        }

        if (domains.ApiHost.Length == 0 || string.IsNullOrWhiteSpace(email.SendGrid.InboundSecret))
        {
            return null;
        }

        return new UriBuilder(domains.Scheme, domains.ApiHost)
        {
            Path = EmailWebhookEndpoints.InboundPath,
            UserName = EmailWebhookEndpoints.InboundUser,
            Password = Uri.EscapeDataString(email.SendGrid.InboundSecret),
        }.Uri;
    }
}
