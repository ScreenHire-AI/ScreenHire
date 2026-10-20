using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Emailed sign-in links: single use, short-lived, stored only as a hash, and capped per address. Sending is the same
/// for known and unknown addresses (an unknown one gets an account when its link is used), so the reply never says
/// whether someone has an account.
/// </summary>
/// <remarks>The email goes out inside the request for now; it moves to a Wolverine message when messaging lands.</remarks>
internal sealed partial class MagicLinks(
    ScreenHireDbContext db,
    TimeProvider time,
    IEmailSender emailSender,
    IOptions<DomainOptions> domains,
    IOptions<AuthOptions> auth,
    ILogger<MagicLinks> logger)
{
    /// <summary>The dashboard page that redeems a link. The token is in the fragment, which browsers never send to a server.</summary>
    public const string VerifyPath = "/sign-in/verify";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>256 random bits, URL-safe.</summary>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task SendAsync(string address, CancellationToken cancellationToken)
    {
        var options = auth.Value.MagicLink;
        var now = time.GetUtcNow();
        var email = NormalizeEmail(address);

        var since = now - options.Window;
        var recent = await db.MagicLinkTokens.CountAsync(t => t.Email == email && t.CreatedAt > since, cancellationToken);
        if (recent >= options.PerAddressLimit)
        {
            LogLimitReached(logger, options.PerAddressLimit);
            return;
        }

        var token = NewToken();
        db.MagicLinkTokens.Add(new MagicLinkToken
        {
            Email = email,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + options.Lifetime,
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = domains.Value.AppUrl($"{VerifyPath}#token={token}");
        await emailSender.SendAsync(Compose(email, link, options.Lifetime), cancellationToken);
    }

    /// <summary>
    /// Uses up the link and returns the address it was sent to, or null when the token is unknown, used or expired.
    /// One conditional UPDATE, so two simultaneous clicks cannot both succeed.
    /// </summary>
    public async Task<string?> RedeemAsync(string token, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hash = Hash(token);

        var redeemed = await db.MagicLinkTokens
            .Where(t => t.TokenHash == hash && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        return redeemed == 0
            ? null
            : await db.MagicLinkTokens.Where(t => t.TokenHash == hash).Select(t => t.Email).SingleAsync(cancellationToken);
    }

    internal static EmailMessage Compose(string to, Uri link, TimeSpan lifetime)
    {
        var minutes = (int)lifetime.TotalMinutes;
        var text = $"""
            Here is your link to sign in to ScreenHire:

            {link}

            It works once, for the next {minutes} minutes. If you didn't ask to sign in, you can ignore this email.
            """;
        var href = WebUtility.HtmlEncode(link.ToString());
        var html = $"""
            <p>Here is your link to sign in to ScreenHire:</p>
            <p><a href="{href}">Sign in to ScreenHire</a></p>
            <p>It works once, for the next {minutes} minutes. If you didn't ask to sign in, you can ignore this email.</p>
            """;
        return new EmailMessage(to, "Your ScreenHire sign-in link", text, html);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in link not sent: the address already had {Limit} links in the window")]
    private static partial void LogLimitReached(ILogger logger, int limit);
}
