using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Hiring;

/// <summary>
/// One of an organization's email templates: its own (<see cref="BuiltInId"/> null), or its wording of a built-in one
/// (<see cref="EmailTemplateIds.BuiltIn"/>). A built-in with no row sends ScreenHire's default wording, so deleting the
/// row restores it. Subject and body are Liquid over a fixed list of variables; the body is plain text, and the HTML
/// part of the email is built from it, so a template can never carry markup.
/// </summary>
public sealed class EmailTemplate : ITenantOwned
{
    public const int MaxNameLength = 100;

    public const int MaxSubjectLength = 200;

    public const int MaxBodyLength = 10_000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>The built-in template this row rewords, or null for the organization's own template.</summary>
    public string? BuiltInId { get; init; }

    /// <summary>The organization's own name for its template. A built-in's name is ScreenHire's, so it is null here.</summary>
    public string? Name { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The id automations and sends name it by: the built-in id, or this row's id.</summary>
    public string Key => BuiltInId ?? Id.ToString("D");

    public static EmailTemplate CreateCustom(Guid organizationId, string name, string subject, string body, DateTimeOffset now) =>
        new() { OrganizationId = organizationId, Name = name, Subject = subject, Body = body, CreatedAt = now, UpdatedAt = now };

    public static EmailTemplate Reword(Guid organizationId, string builtInId, string subject, string body, DateTimeOffset now) =>
        EmailTemplateIds.All.Contains(builtInId)
            ? new() { OrganizationId = organizationId, BuiltInId = builtInId, Subject = subject, Body = body, CreatedAt = now, UpdatedAt = now }
            : throw new ArgumentException($"There is no built-in email template '{builtInId}'.", nameof(builtInId));
}
