using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.OAuth;

/// <summary>
/// The scope words an OAuth client asks for, mapped one to one onto an API key's scopes (ADR-0011, ADR-0021):
/// <c>read</c>, <c>write</c> and <c>verification_read</c>. <c>offline_access</c> is accepted and ignored, because a public
/// client always gets a refresh token. A word the server does not know is ignored, as RFC 6749 allows.
/// </summary>
internal static class OAuthScopes
{
    public const string Read = "read";
    public const string Write = "write";
    public const string VerificationRead = "verification_read";

    /// <summary>The words the metadata lists, in the order the consent page shows them.</summary>
    public static IReadOnlyList<string> Supported { get; } = [Read, Write, VerificationRead];

    public static string NameOf(ApiKeyScope scope) => scope switch
    {
        ApiKeyScope.Read => Read,
        ApiKeyScope.Write => Write,
        ApiKeyScope.VerificationRead => VerificationRead,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    /// <summary>
    /// The scopes a request names, in key order and always with Read: a request that names none, or only Write, still
    /// reads. Words the server does not know are dropped.
    /// </summary>
    public static IReadOnlyList<ApiKeyScope> Parse(string? scope)
    {
        var requested = new HashSet<ApiKeyScope> { ApiKeyScope.Read };
        foreach (var word in (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word)
            {
                case Write:
                    requested.Add(ApiKeyScope.Write);
                    break;
                case VerificationRead:
                    requested.Add(ApiKeyScope.VerificationRead);
                    break;
            }
        }

        return [.. requested.Order()];
    }

    /// <summary>The scope string of a token response.</summary>
    public static string Format(IEnumerable<ApiKeyScope> scopes) => string.Join(' ', scopes.Order().Select(NameOf));
}
