using FluentValidation;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's email templates and the variables they may use. <c>sendingEnabled</c> is false when this
/// deployment has no email sender: templates can still be edited, but nothing can be sent or tested.
/// </summary>
public sealed record EmailTemplateList(bool SendingEnabled, IReadOnlyList<EmailTemplateEntry> Templates, IReadOnlyList<EmailTemplateVariableEntry> Variables);

/// <summary>
/// One email template. Built-in templates have ScreenHire's ids and names; the organization may change their wording
/// (<c>edited</c>) and restore it. Its own templates have a Guid id and its own name.
/// </summary>
/// <remarks>
/// <c>rejectOnly</c>: only a Reject action sends it (the rejection email); a person cannot send it, and SendEmail cannot
/// name it. <c>questionnaireOnly</c>: only a SendQuestionnaire action sends it (the follow-up questions email, whose link
/// belongs to one request), with the same limits. <c>updatedAt</c> is null for a built-in in ScreenHire's wording, and
/// <c>lastUpdated</c> says who changed it last, or ScreenHire for a built-in nobody has.
/// </remarks>
public sealed record EmailTemplateEntry(
    string Id, string Name, bool BuiltIn, bool Edited, bool RejectOnly, string Subject, string Body, DateTimeOffset? UpdatedAt,
    LastUpdate LastUpdated, bool QuestionnaireOnly = false);

/// <summary>
/// A variable a template may use as <c>{{ name }}</c>. <c>rejectOnly</c>: only the rejection email has it.
/// <c>questionnaireOnly</c>: only the follow-up questions email has it.
/// </summary>
public sealed record EmailTemplateVariableEntry(string Name, string Label, bool RejectOnly, bool QuestionnaireOnly = false);

/// <summary>A new template of the organization's own: its name, a Liquid subject and a Liquid plain-text body.</summary>
public sealed record CreateEmailTemplateRequest(string Name, string Subject, string Body);

/// <summary>
/// New wording for a template. <c>name</c> renames the organization's own template; leave it out for a built-in,
/// whose name is ScreenHire's.
/// </summary>
public sealed record UpdateEmailTemplateRequest(string Subject, string Body, string? Name = null);

/// <summary>
/// Wording to render with the sample candidate, before it is saved. <c>templateId</c> is the template it is for, so
/// the rejection email's <c>reason</c> renders; leave it out for a new template.
/// </summary>
public sealed record EmailTemplateDraft(string Subject, string Body, string? TemplateId = null);

/// <summary>An email as the candidate would receive it: the subject, the HTML part and the plain-text part.</summary>
public sealed record EmailPreview(string Subject, string Html, string Text);

/// <summary>A test send went to the signed-in person's own address.</summary>
public sealed record TestEmailSent(string To);

/// <summary>Sends one of the organization's templates to the candidates of these applications, one email each.</summary>
public sealed record SendEmailsRequest(IReadOnlyList<Guid> ApplicationIds, string TemplateId);

/// <summary>
/// How many emails were queued. <c>skipped</c> counts applications the email does not fit: the stage-change email to a
/// candidate whose application has ended. Each sent email appears in its application's activity.
/// </summary>
public sealed record EmailsQueued(int Queued, int Skipped);

internal static class EmailMappings
{
    public static EmailTemplateEntry ToEntry(this OrganizationEmailTemplate template, ChangeStamp? lastChange) =>
        new(template.Id, template.Name, template.BuiltIn, template.Edited, template.Id == EmailTemplateIds.ApplicationRejected,
            template.Content.Subject, template.Content.Body, template.UpdatedAt,
            lastChange is null && template.BuiltIn && !template.Edited ? LastUpdate.ByScreenHire(null) : LastUpdate.From(lastChange, template.UpdatedAt),
            template.Id == EmailTemplateIds.QuestionnaireRequest);

    public static EmailPreview ToPreview(this EmailMessage message) => new(message.Subject, message.HtmlBody ?? "", message.TextBody);

    /// <summary>Wording problems in problem-details form, keyed <c>subject</c> and <c>body</c>, or null when there are none.</summary>
    public static IDictionary<string, string[]>? ToProblems(this IReadOnlyList<EmailTemplateProblem> problems) =>
        problems.Count == 0 ? null : problems.GroupBy(p => p.Field).ToDictionary(g => g.Key, g => g.Select(p => p.Message).ToArray());
}

internal sealed class CreateEmailTemplateRequestValidator : AbstractValidator<CreateEmailTemplateRequest>
{
    public CreateEmailTemplateRequestValidator()
    {
        RuleFor(r => r.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter a name.").MaximumLength(EmailTemplate.MaxNameLength);
        RuleFor(r => r.Subject).Must(subject => !string.IsNullOrWhiteSpace(subject)).WithMessage("Enter a subject.").MaximumLength(EmailTemplate.MaxSubjectLength);
        RuleFor(r => r.Body).Must(body => !string.IsNullOrWhiteSpace(body)).WithMessage("Enter the message.").MaximumLength(EmailTemplate.MaxBodyLength);
    }
}

internal sealed class UpdateEmailTemplateRequestValidator : AbstractValidator<UpdateEmailTemplateRequest>
{
    public UpdateEmailTemplateRequestValidator()
    {
        RuleFor(r => r.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter a name.").MaximumLength(EmailTemplate.MaxNameLength)
            .When(r => r.Name is not null);
        RuleFor(r => r.Subject).Must(subject => !string.IsNullOrWhiteSpace(subject)).WithMessage("Enter a subject.").MaximumLength(EmailTemplate.MaxSubjectLength);
        RuleFor(r => r.Body).Must(body => !string.IsNullOrWhiteSpace(body)).WithMessage("Enter the message.").MaximumLength(EmailTemplate.MaxBodyLength);
    }
}

internal sealed class EmailTemplateDraftValidator : AbstractValidator<EmailTemplateDraft>
{
    public EmailTemplateDraftValidator()
    {
        RuleFor(r => r.Subject).NotNull().MaximumLength(EmailTemplate.MaxSubjectLength);
        RuleFor(r => r.Body).NotNull().MaximumLength(EmailTemplate.MaxBodyLength);
        RuleFor(r => r.TemplateId).MaximumLength(40);
    }
}

internal sealed class SendEmailsRequestValidator : AbstractValidator<SendEmailsRequest>
{
    /// <summary>A selection a person makes on one screen; a larger mailing is a different feature.</summary>
    public const int MaxApplications = 200;

    public SendEmailsRequestValidator()
    {
        RuleFor(r => r.ApplicationIds).NotEmpty().WithMessage("Choose at least one candidate.")
            .Must(ids => ids is null || ids.Count <= MaxApplications).WithMessage($"Send to at most {MaxApplications} candidates at once.");
        RuleFor(r => r.TemplateId).NotEmpty().MaximumLength(40)
            .Must(id => id != EmailTemplateIds.ApplicationRejected)
            .WithMessage("The rejection email goes only with a Reject action, which tells the candidate why.")
            .Must(id => id != EmailTemplateIds.QuestionnaireRequest)
            .WithMessage("The follow-up questions email goes only with a SendQuestionnaire action, which makes its link.");
    }
}
