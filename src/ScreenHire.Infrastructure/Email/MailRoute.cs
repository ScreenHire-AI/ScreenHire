namespace ScreenHire.Infrastructure.Email;

/// <summary>
/// How one email to a candidate is addressed beyond the candidate's own address: the From of the organization's
/// verified sending domain, the reply address for the send, and the ids the provider echoes back in its event webhook.
/// Any part may be null; <see cref="None"/> changes nothing.
/// </summary>
public sealed record MailRoute(string? From = null, string? ReplyTo = null, IReadOnlyDictionary<string, string>? CustomArgs = null)
{
    public static MailRoute None { get; } = new();

    public EmailMessage Apply(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message with
        {
            From = From ?? message.From,
            ReplyTo = ReplyTo ?? message.ReplyTo,
            CustomArgs = CustomArgs ?? message.CustomArgs,
        };
    }
}
