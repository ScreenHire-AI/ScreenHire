namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// How one email to a candidate is addressed beyond the candidate's own address: the From of the organization's
/// verified sending domain, the reply address for the send, and the ids the provider echoes back in its event webhook.
/// Any part may be null; <see cref="None"/> changes nothing. <see cref="PortalLink"/> is the candidate's own link to their
/// page (docs/architecture/candidate-portal.md): <see cref="Apply"/> ends the email with it. It is a capability, so it is
/// added to what goes out and never to the copy the team keeps.
/// </summary>
public sealed record MailRoute(
    string? From = null,
    string? ReplyTo = null,
    IReadOnlyDictionary<string, string>? CustomArgs = null,
    string? PortalLink = null)
{
    public static MailRoute None { get; } = new();

    public EmailMessage Apply(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var routed = message with
        {
            From = From ?? message.From,
            ReplyTo = ReplyTo ?? message.ReplyTo,
            CustomArgs = CustomArgs ?? message.CustomArgs,
        };
        return PortalLink is { Length: > 0 } link ? CandidatePortalFooter.Append(routed, link) : routed;
    }
}
