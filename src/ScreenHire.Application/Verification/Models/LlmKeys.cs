using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ScreenHire.Application.Verification;

/// <summary>
/// An organization's own model API key at rest: ASP.NET Core Data Protection, whose key ring the API and the worker share in
/// Postgres (<c>AddScreenHireDataProtection</c>). Each organization has its own purpose, so a key copied into another
/// organization's row does not read. The key is write-only: it is protected here when saved and unprotected only where the
/// model client is built, and no contract, log or History line carries it.
/// </summary>
public sealed class LlmKeys(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "ScreenHire.Verification.LlmKey.v1";

    public string Protect(Guid organizationId, string key) => Protector(organizationId).Protect(key);

    /// <summary>The key, or null when it no longer reads (the key ring lost its key, or the row was tampered with).</summary>
    public string? Unprotect(Guid organizationId, string protectedKey)
    {
        try
        {
            return Protector(organizationId).Unprotect(protectedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid organizationId) => dataProtection.CreateProtector(Purpose, organizationId.ToString("N"));
}
