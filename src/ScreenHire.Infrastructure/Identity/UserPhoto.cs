namespace ScreenHire.Infrastructure.Identity;

/// <summary>
/// A person's profile photo, from Your account. Global like the user (the same face in every organization), so this table
/// has no <c>OrganizationId</c> and no row-level security; the API serves it only to people who share an organization with
/// them. The browser crops and scales the picture to <see cref="MaxSide"/> pixels before it uploads, so a row is a few
/// dozen kilobytes and the server needs no image library (ADR-0008).
/// </summary>
public sealed class UserPhoto
{
    /// <summary>The largest upload the API accepts.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>The square the dashboard scales a photo to.</summary>
    public const int MaxSide = 256;

    public const int MaxContentTypeLength = 40;

    public Guid UserId { get; init; }

    public byte[] Content { get; set; } = [];

    public string ContentType { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}
