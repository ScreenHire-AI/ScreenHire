namespace ScreenHire.Domain.Organizations;

/// <summary>
/// An employer account — the tenant boundary. Every tenant-owned row carries an <c>OrganizationId</c>.
/// </summary>
public sealed class Organization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>URL-safe identifier used in careers-page hosts and public API routes.</summary>
    public required string Slug { get; set; }

    /// <summary>Data-residency home region (e.g. <c>eu</c>, <c>us</c>); see ADR-0007.</summary>
    public required string HomeRegion { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
