using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Emailed invitations to join an organization. The token is the organization's id followed by 256 random bits,
/// URL-safe. Accepting reads the id to know which organization's row-level security to look inside, and only a match
/// on the token's SHA-256 hash there accepts anything: the id is a locator, the random part is the secret. Only the
/// hash is stored, as for sign-in links (<see cref="MagicLinks"/>).
/// </summary>
internal static class Invitations
{
    /// <summary>The dashboard page that accepts an invitation. The token is in the fragment, which browsers never send.</summary>
    public const string AcceptPath = "/invitations/accept";

    private const int OrganizationBytes = 16;
    private const int SecretBytes = 32;

    public static string NewToken(Guid organizationId)
    {
        Span<byte> token = stackalloc byte[OrganizationBytes + SecretBytes];
        organizationId.TryWriteBytes(token[..OrganizationBytes]);
        RandomNumberGenerator.Fill(token[OrganizationBytes..]);
        return Base64Url.EncodeToString(token);
    }

    /// <summary>The organization a token names, or null when it is not an invitation token at all.</summary>
    public static Guid? OrganizationOf(string token)
    {
        Span<byte> bytes = stackalloc byte[OrganizationBytes + SecretBytes];
        return Base64Url.IsValid(token, out var length) && length == bytes.Length
            && Base64Url.TryDecodeFromChars(token, bytes, out var written) && written == bytes.Length
            ? new Guid(bytes[..OrganizationBytes])
            : null;
    }

    /// <summary>
    /// Serializes changes to one organization's members for the rest of the transaction: role changes, removals and
    /// joins. Two Owners demoting each other at once would otherwise both see the other as the remaining Owner. The key
    /// is prefixed, so it never shares a lock with onboarding's per-user one.
    /// </summary>
    public static Task LockMembersAsync(ScreenHireDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var key = $"organization-members:{organizationId}";
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    internal static EmailMessage Compose(
        string to, string organizationName, string inviterName, OrganizationRole role, Uri link, TimeSpan lifetime)
    {
        var days = (int)Math.Ceiling(lifetime.TotalDays);
        var duration = days == 1 ? "1 day" : $"{days} days";
        var roleName = role.ToString().ToLowerInvariant();
        var text = $"""
            {inviterName} invited you to join {organizationName} on ScreenHire as {Article(roleName)} {roleName}.

            Accept the invitation:

            {link}

            The link signs you in as {to}. It works once, for the next {duration}. If you weren't expecting this, you can ignore this email.
            """;
        var html = $"""
            <p>{Encode(inviterName)} invited you to join {Encode(organizationName)} on ScreenHire as {Article(roleName)} {roleName}.</p>
            <p><a href="{Encode(link.ToString())}">Accept the invitation</a></p>
            <p>The link signs you in as {Encode(to)}. It works once, for the next {duration}. If you weren't expecting this, you can ignore this email.</p>
            """;
        // A subject is one header line: an organization name never carries a line break into it.
        var subject = $"Join {organizationName} on ScreenHire".ReplaceLineEndings(" ");
        return new EmailMessage(to, subject, text, html);
    }

    private static string Article(string word) => word[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
