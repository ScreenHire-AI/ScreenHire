using FluentValidation;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The WebAuthn options the browser passes to <c>navigator.credentials.create</c> or <c>get</c>, as the JSON text the
/// W3C <c>PublicKeyCredential*OptionsJSON</c> shape defines. The browser parses it with
/// <c>PublicKeyCredential.parseCreationOptionsFromJSON</c> or <c>parseRequestOptionsFromJSON</c>.
/// </summary>
public sealed record PasskeyOptions(string OptionsJson);

/// <summary>The browser's answer to a sign-in ceremony: <c>credential.toJSON()</c> as text.</summary>
public sealed record PasskeySignInRequest(string CredentialJson);

/// <summary>The browser's answer to a registration ceremony, and the name to give the passkey (a device, as the person typed it).</summary>
public sealed record RegisterPasskeyRequest(string CredentialJson, string? Name = null);

/// <summary>One of the signed-in person's passkeys. <c>id</c> is the credential id, base64url.</summary>
public sealed record PasskeySummary(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>The signed-in person's passkeys, newest first, and how many they may keep.</summary>
public sealed record PasskeyList(IReadOnlyList<PasskeySummary> Passkeys, int Limit);

internal sealed class PasskeySignInRequestValidator : AbstractValidator<PasskeySignInRequest>
{
    public PasskeySignInRequestValidator() => RuleFor(r => r.CredentialJson).NotEmpty().MaximumLength(PasskeyLimits.CredentialJsonLength);
}

internal sealed class RegisterPasskeyRequestValidator : AbstractValidator<RegisterPasskeyRequest>
{
    public RegisterPasskeyRequestValidator()
    {
        RuleFor(r => r.CredentialJson).NotEmpty().MaximumLength(PasskeyLimits.CredentialJsonLength);
        RuleFor(r => r.Name).MaximumLength(PasskeyLimits.NameLength);
    }
}

/// <summary>Sizes the passkey routes accept.</summary>
internal static class PasskeyLimits
{
    /// <summary>The most passkeys one person keeps.</summary>
    public const int PerUser = 10;

    /// <summary>A credential with its attestation is a few kilobytes; this bounds what the handler parses.</summary>
    public const int CredentialJsonLength = 20_000;

    public const int NameLength = 60;

    public const string DefaultName = "Passkey";

    /// <summary>The name to store: trimmed, with control characters dropped, or <see cref="DefaultName"/> when nothing is left.</summary>
    public static string CleanName(string? name)
    {
        var cleaned = new string([.. (name ?? "").Where(c => !char.IsControl(c))]).Trim();
        return cleaned.Length == 0 ? DefaultName : cleaned;
    }

    /// <summary>
    /// A person must keep a way to sign in. Removing a passkey is refused when it is their last one and nothing else
    /// works: no other passkey, no identity provider that is still configured, and no emailed link.
    /// </summary>
    public static bool MayRemove(int passkeys, int usableExternalLogins, bool emailLinksEnabled) =>
        passkeys > 1 || usableExternalLogins > 0 || emailLinksEnabled;
}
