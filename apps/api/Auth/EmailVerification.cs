using System.Security.Claims;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Whether a provider vouches for the email address in its token. Sign-in links an identity to an existing user by
/// email, so an unverified address must never match: anyone can put any address on some providers' accounts.
/// </summary>
internal static class EmailVerification
{
    // Microsoft's tenant id for personal (consumer) accounts, whose email is the account itself.
    private const string MicrosoftConsumerTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    public static bool IsVerified(ProviderKind kind, ClaimsPrincipal principal) => kind switch
    {
        // Microsoft's email claim is not verified (and it sends no email_verified). Work accounts carry xms_edov, the
        // optional claim saying the domain owner verified the address; the app registration must request it.
        ProviderKind.Microsoft => IsTrue(principal.FindFirst("xms_edov"))
            || string.Equals(principal.FindFirst("tid")?.Value, MicrosoftConsumerTenant, StringComparison.OrdinalIgnoreCase),
        _ => IsTrue(principal.FindFirst("email_verified")),
    };

    // Providers send a JSON boolean; some send the string "true".
    private static bool IsTrue(Claim? claim) => claim is not null && bool.TryParse(claim.Value, out var value) && value;
}
