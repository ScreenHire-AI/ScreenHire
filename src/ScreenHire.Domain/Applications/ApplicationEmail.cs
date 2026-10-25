using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// One email to an application's candidate as it goes out: the address, the subject and both bodies, rendered for this
/// candidate. Its id is the send's id, which is also the id of the <see cref="ApplicationEventType.EmailSent"/> entry
/// that records it went (<see cref="ApplicationEventData.EmailId"/>), so a template changed later never changes what the
/// candidate was sent. A person's send is rendered in the request and waits here for the worker; an automation's is
/// stored when it goes. Never updated: the database lets the application role insert, read and delete these rows, and
/// the worker deletes one only when it decides not to send it.
/// </summary>
public sealed class ApplicationEmail : IApplicationScoped
{
    /// <summary>The most a rendered subject may hold (one header line).</summary>
    public const int MaxSubjectLength = 1000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The template it started from; null for a blank email.</summary>
    public string? TemplateId { get; init; }

    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string TextBody { get; init; }

    public required string HtmlBody { get; init; }

    /// <summary>When it was rendered. When it went is the <see cref="ApplicationEventType.EmailSent"/> entry's time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
