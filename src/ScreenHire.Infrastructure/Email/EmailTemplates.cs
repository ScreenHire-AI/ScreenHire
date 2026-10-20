using System.Collections.Frozen;
using System.Text.Encodings.Web;
using Fluid;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Infrastructure.Email;

/// <summary>What a transactional email may say about the application. Every value is plain text; HTML bodies encode it.</summary>
public sealed record EmailTemplateModel(
    string CandidateName, string JobTitle, string OrganizationName, string? StageName = null, string? Reason = null);

/// <summary>
/// Renders the transactional emails named in <see cref="EmailTemplateIds"/> with Fluid (Liquid templates, MIT). Each
/// template is three embedded files under <c>Email/Templates</c>: <c>{id}.subject.liquid</c>, <c>{id}.txt.liquid</c>
/// and <c>{id}.html.liquid</c>. The templates see only the variables <see cref="EmailTemplateModel"/> sets, and the HTML
/// body encodes them, so a candidate's name or a job title can never inject markup. Parsed once; thread-safe.
/// </summary>
public sealed class EmailTemplates
{
    private readonly FrozenDictionary<string, (IFluidTemplate Subject, IFluidTemplate Text, IFluidTemplate Html)> templates;
    private readonly TemplateOptions options = new();

    public EmailTemplates()
    {
        var parser = new FluidParser();
        templates = EmailTemplateIds.All.ToFrozenDictionary(
            id => id,
            id => (Parse(parser, $"{id}.subject"), Parse(parser, $"{id}.txt"), Parse(parser, $"{id}.html")),
            StringComparer.Ordinal);
    }

    /// <summary>The email <paramref name="templateId"/> renders for <paramref name="to"/>.</summary>
    public EmailMessage Render(string templateId, string to, EmailTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!templates.TryGetValue(templateId, out var template))
        {
            throw new ArgumentException($"There is no email template '{templateId}'.", nameof(templateId));
        }

        var context = new TemplateContext(options);
        context.SetValue("candidateName", model.CandidateName);
        context.SetValue("jobTitle", model.JobTitle);
        context.SetValue("organizationName", model.OrganizationName);
        context.SetValue("stageName", model.StageName ?? "");
        context.SetValue("reason", model.Reason ?? "");

        // A subject is one line of plain text: no encoding, and no line breaks a header could carry.
        var subject = template.Subject.Render(context, NullEncoder.Default).Trim().ReplaceLineEndings(" ");
        return new EmailMessage(
            to,
            subject,
            template.Text.Render(context, NullEncoder.Default).Trim() + Environment.NewLine,
            template.Html.Render(context, HtmlEncoder.Default));
    }

    private static IFluidTemplate Parse(FluidParser parser, string name)
    {
        using var stream = typeof(EmailTemplates).Assembly.GetManifestResourceStream($"ScreenHire.Email.{name}.liquid")
            ?? throw new InvalidOperationException($"The email template file {name}.liquid is not embedded.");
        using var reader = new StreamReader(stream);
        return parser.TryParse(reader.ReadToEnd(), out var template, out var error)
            ? template
            : throw new InvalidOperationException($"The email template {name}.liquid does not parse: {error}");
    }
}
