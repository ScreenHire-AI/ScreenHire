using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>Organization API keys, <c>ScreenHire:ApiKeys:*</c> (docs/architecture/sign-in-and-tenancy.md, "API keys").</summary>
public sealed class ApiKeyOptions
{
    public const string Section = "ScreenHire:ApiKeys";

    /// <summary>
    /// Requests per minute per key: the limit of a key created without one, and the most a key may be given. Counted
    /// per API instance.
    /// </summary>
    [Range(1, 100_000)]
    public int PermitsPerMinute { get; set; } = 600;

    /// <summary>
    /// Failed key attempts per minute per client address. Beyond it the address gets 429 for every key until the minute
    /// is up. Counted per API instance.
    /// </summary>
    [Range(1, 10_000)]
    public int FailedAttemptsPerMinute { get; set; } = 30;
}
