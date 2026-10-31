using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Applications;

/// <summary>What a valid draft link names: an organization and one of its drafts.</summary>
internal sealed record ApplicationDraftLink(Guid OrganizationId, Guid DraftId);

/// <summary>
/// Saving the careers apply form to finish later (apply-flow.md §7). A draft is kept under the candidate's email, and
/// its signed link goes to that address inside the request, as sign-in links do (<see cref="Auth.MagicLinks"/>): the
/// token lets whoever holds it read the draft, so it never sits in the outbox's message table. The link's token is the
/// organization and the draft, protected with Data Protection under its own purpose, like follow-up questionnaire links
/// (<c>QuestionnaireLinks</c>).
/// </summary>
/// <remarks>
/// Hidden, never an error, when the deployment sends no email or has no careers host to link to
/// (<see cref="IsEnabled"/>). An address is sent at most <see cref="ApplicationDraft.LinksPerAddress"/> links a day in
/// one organization, counted under a per-address advisory lock so that simultaneous saves cannot all slip under the cap.
/// </remarks>
internal sealed class ApplicationDrafts(
    IServiceProvider services, IDataProtectionProvider dataProtection, IOptions<DomainOptions> domains, TimeProvider time)
{
    /// <summary>How long after its draft's expiry a token still reads, so the page can say the link expired.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly IEmailSender? sender = services.GetService<IEmailSender>();

    private readonly ITimeLimitedDataProtector protector =
        dataProtection.CreateProtector("ScreenHire.ApplicationDraft.Link.v1").ToTimeLimitedDataProtector();

    /// <summary>Whether candidates can save: an email sender is registered and a careers host pattern is configured.</summary>
    public bool IsEnabled => sender is not null && domains.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>The token for <paramref name="draft"/>, valid until its expiry plus <see cref="Grace"/>.</summary>
    public string Issue(ApplicationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{draft.OrganizationId:N}.{draft.Id:N}"), draft.ExpiresAt + Grace);
    }

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged or past its own limit.</summary>
    public ApplicationDraftLink? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 1000)
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token, out _);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('.');
        return parts.Length == 2
            && Guid.TryParseExact(parts[0], "N", out var organization)
            && Guid.TryParseExact(parts[1], "N", out var draft)
                ? new ApplicationDraftLink(organization, draft)
                : null;
    }

    /// <summary>The apply page that opens <paramref name="token"/>'s draft, on the organization's careers board.</summary>
    public Uri Url(string organizationSlug, string jobSlug, string token) =>
        new($"{domains.Value.CareersJobUrl(organizationSlug, jobSlug)}/apply?draft={token}");

    /// <summary>
    /// Saves a new draft for <paramref name="email"/> and emails its link; null when the address has had
    /// <see cref="ApplicationDraft.LinksPerAddress"/> links in the last day. The caller has checked the answers.
    /// </summary>
    public async Task<(ApplicationDraft Draft, string Token)?> CreateAsync(
        ScreenHireDbContext db, Organization organization, Job job, string email, ApplicationDraftContent content, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var normalized = Candidate.Normalize(email);
        var since = now - ApplicationDraft.LinkWindow;
        var draft = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"application-draft:" + organization.Id.ToString("N") + ":" + normalized}, 0))",
                cancellationToken);
            var recent = await db.ApplicationDrafts.CountAsync(d => d.Email == normalized && d.CreatedAt > since, cancellationToken);
            if (recent >= ApplicationDraft.LinksPerAddress)
            {
                return null;
            }

            var created = ApplicationDraft.Create(organization.Id, job.Id, email, content, now);
            db.ApplicationDrafts.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        });

        if (draft is null)
        {
            return null;
        }

        var token = Issue(draft);
        var message = Compose(email.Trim(), organization.Name, job.Title, Url(organization.Slug, job.Slug, token), draft.ExpiresAt);
        await (sender ?? throw new InvalidOperationException("No email sender is configured.")).SendAsync(message, cancellationToken);
        return (draft, token);
    }

    /// <summary>
    /// The email with the link: ScreenHire's own words, not an organization template, because it goes before there is an
    /// application to name. The date is the day the draft is deleted, in UTC.
    /// </summary>
    internal static EmailMessage Compose(string to, string organizationName, string jobTitle, Uri link, DateTimeOffset expiresAt)
    {
        var until = expiresAt.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var subject = $"Finish your application for {jobTitle} at {organizationName}";
        var text = $"""
            You saved your application for {jobTitle} at {organizationName} to finish later.

            Finish it here: {link}

            The link opens the answers you saved last and works until {until}, when we delete them. Files are not kept,
            so attach them again when you come back. Nobody at {organizationName} sees your answers until you send them.

            If you didn't start this application, you can ignore this email.
            """;
        var href = WebUtility.HtmlEncode(link.ToString());
        var html = $"""
            <p>You saved your application for {WebUtility.HtmlEncode(jobTitle)} at {WebUtility.HtmlEncode(organizationName)} to finish later.</p>
            <p><a href="{href}">Finish your application</a></p>
            <p>The link opens the answers you saved last and works until {until}, when we delete them. Files are not kept, so attach them again when you come back. Nobody at {WebUtility.HtmlEncode(organizationName)} sees your answers until you send them.</p>
            <p>If you didn't start this application, you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }
}
