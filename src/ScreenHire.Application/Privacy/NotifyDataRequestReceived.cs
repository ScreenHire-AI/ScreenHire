using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Privacy;

/// <summary>
/// Emails the members who can answer a data request when one arrives (docs/architecture/data-requests.md, "The team's side").
/// The dashboard card an open dashboard shows vanishes on reload; this email stays. It goes to members who hold Erase
/// candidates and see every job, because only they can open the request. It carries the organization's name, the kind of
/// request and a link, never the candidate's name or address.
/// </summary>
/// <remarks>
/// Delivery is at least once and the envelope inbox drops redelivered copies, so the worst case after a crash between a send
/// and the commit is one repeated notification. Nobody is told when the host has no email sender or no dashboard address.
/// </remarks>
public static partial class DataRequestReceivedHandler
{
    public static async Task Handle(
        DataRequestReceived message,
        ScreenHireDbContext db,
        TransactionalEmail email,
        IOptions<DashboardLinkOptions> links,
        ILogger<DataRequestReceived> logger,
        CancellationToken cancellationToken)
    {
        var link = links.Value.Url(DataRequestNotificationEmail.RequestsPath);
        if (!email.IsConfigured || link is null)
        {
            LogNotSent(logger, message.RequestId, "this host has no email sender or no dashboard address");
            return;
        }

        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == message.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return;
        }

        var members = await (
            from member in db.OrganizationMembers.AsNoTracking()
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            join role in db.CustomRoles.AsNoTracking() on member.CustomRoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new { member.Role, member.JobAccess, Permissions = role == null ? null : role.Permissions, user.Email }).ToListAsync(cancellationToken);

        var recipients = members
            .Where(m => JobAccessRules.SeesAllJobs(m.Role, m.JobAccess)
                && (m.Role == OrganizationRole.Custom ? RolePermissions.ForCustom(m.Permissions ?? []) : RolePermissions.Of(m.Role)).Contains(Permission.EraseCandidates))
            .Select(m => m.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var to in recipients)
        {
            await email.SendAsync(DataRequestNotificationEmail.Compose(to, organization.Name, message.Kind, link), cancellationToken);
        }

        LogNotified(logger, message.RequestId, recipients.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: no one was emailed: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid requestId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Data request {RequestId}: emailed {Members} members who can answer it")]
    private static partial void LogNotified(ILogger logger, Guid requestId, int members);
}

/// <summary>The email a member gets when a data request arrives: ScreenHire's own wording, ids and a link only.</summary>
public static class DataRequestNotificationEmail
{
    /// <summary>Where the link goes: Settings, Data requests.</summary>
    public const string RequestsPath = "/settings/data-requests";

    public static EmailMessage Compose(string to, string organizationName, DataRequestKind kind, Uri link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);
        ArgumentNullException.ThrowIfNull(link);
        var line = kind == DataRequestKind.Copy
            ? $"A candidate asked {organizationName} for a copy of their data."
            : $"A candidate asked {organizationName} to erase their data.";
        var subject = kind == DataRequestKind.Copy ? "A candidate asked for a copy of their data" : "A candidate asked to erase their data";
        var text = $"""
            {line}

            Open Settings > Data requests to answer it: {link}
            """;
        var html = $"""
            <p>{WebUtility.HtmlEncode(line)}</p>
            <p><a href="{WebUtility.HtmlEncode(link.ToString())}">Open Settings &gt; Data requests</a> to answer it.</p>
            """;
        return new EmailMessage(to, subject, text, html);
    }
}
