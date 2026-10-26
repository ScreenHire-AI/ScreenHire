using FluentValidation;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// An organization API key as listed: never the key itself, which only <see cref="CreatedApiKey"/> carries.
/// <c>prefix</c> is the key's first characters. <c>permitsPerMinute</c> is null for the deployment's default.
/// <c>lastUsedAt</c> is accurate to about a minute. <c>createdByUserId</c> is the member who created it, or null once their
/// account is deleted; removing a member offers to revoke the keys they created.
/// </summary>
public sealed record ApiKeySummary(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<ApiKeyScope> Scopes,
    int? PermitsPerMinute,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    Guid? CreatedByUserId);

/// <summary>
/// A new key. <c>token</c> is shown this once and cannot be read again: send it as <c>Authorization: Bearer
/// {token}</c>.
/// </summary>
public sealed record CreatedApiKey(ApiKeySummary Key, string Token);

/// <summary>
/// Creates a key. <c>scopes</c> is <c>["Read"]</c> or <c>["Read", "Write"]</c>. <c>permitsPerMinute</c> lowers the
/// key's rate limit below the deployment's; null uses the deployment's.
/// </summary>
public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<ApiKeyScope> Scopes, int? PermitsPerMinute = null);

internal static class ApiKeyMapping
{
    public static ApiKeySummary ToSummary(this ApiKey key) =>
        new(key.Id, key.Name, key.Prefix, key.Scopes, key.PermitsPerMinute, key.CreatedAt, key.LastUsedAt, key.RevokedAt, key.CreatedByUserId);
}

internal sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator(IOptions<ApiKeyOptions> options)
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop).NotEmpty().Must(name => name.Trim().Length > 0).WithMessage("Give the key a name.")
            .MaximumLength(ApiKey.MaxNameLength);
        // JSON numbers bind to enums as well as names, so a number that names no scope is refused here.
        RuleFor(r => r.Scopes).Cascade(CascadeMode.Stop).NotNull()
            .Must(scopes => scopes.All(Enum.IsDefined))
            .WithMessage("Scopes are Read and Write.")
            .Must(scopes => scopes.Contains(ApiKeyScope.Read))
            .WithMessage("A key needs the Read scope.")
            .Must(scopes => scopes.Distinct().Count() == scopes.Count)
            .WithMessage("List each scope once.");
        RuleFor(r => r.PermitsPerMinute).InclusiveBetween(1, options.Value.PermitsPerMinute)
            .When(r => r.PermitsPerMinute is not null);
    }
}
