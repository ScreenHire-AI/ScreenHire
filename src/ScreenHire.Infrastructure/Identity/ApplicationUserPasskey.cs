using Microsoft.AspNetCore.Identity;

namespace ScreenHire.Infrastructure.Identity;

/// <summary>
/// A passkey a person registered: Identity's row (credential id, user, and the public key and counters in
/// <see cref="IdentityUserPasskey{TKey}.Data"/>) plus when it last signed them in, which Identity does not keep.
/// Global like <see cref="ApplicationUser"/>: no <c>OrganizationId</c> and no row-level security.
/// </summary>
public sealed class ApplicationUserPasskey : IdentityUserPasskey<Guid>
{
    /// <summary>When this passkey last signed the person in; null until it has. Set by the sign-in endpoint.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
