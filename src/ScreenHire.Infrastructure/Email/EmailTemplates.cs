using System.Collections.Frozen;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fluid;
using Fluid.Ast;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Content;

namespace ScreenHire.Infrastructure.Email;

/// <summary>What an email template may say about the application. Every value is plain text.</summary>
/// <remarks>
/// <c>candidateName</c>: the full name. <c>firstName</c>: the first name, or the full name when none is given (a
/// candidate from before the name was split keeps their whole name as the first name). <c>lastName</c>: empty when the
/// candidate gave none. <c>questionnaireLink</c>: the signed link to a follow-up questionnaire, for the questionnaire
/// request email only.
/// </remarks>
public sealed record EmailTemplateModel(
    string CandidateName,
    string JobTitle,
    string OrganizationName,
    string? StageName = null,
    string? Reason = null,
    string? QuestionnaireLink = null,
    string? FirstName = null,
    string? LastName = null)
{
    /// <summary>The made-up candidate a template's preview and test send use.</summary>
    public static EmailTemplateModel Sample(string organizationName) =>
        new("Jordan Lee", "Product designer", organizationName, "Interview", "This role needs a driving licence.",
            "https://careers.example.com/questionnaires/sample", "Jordan", "Lee");
}

/// <summary>
/// A template's wording: a Liquid plain-text subject and a Liquid rich-text body (sanitized HTML, <see cref="RichText"/>).
/// </summary>
public sealed record EmailTemplateContent(string Subject, string Body);

/// <summary>One variable a template may use, and what it holds.</summary>
public sealed record EmailTemplateVariable(string Name, string Label);

/// <summary>A problem with a template's wording, on its <c>subject</c> or <c>body</c>.</summary>
public sealed record EmailTemplateProblem(string Field, string Message);

/// <summary>
/// Renders email templates with Fluid (Liquid, MIT). A template is a plain-text subject and a rich-text body over the
/// variables in <see cref="Variables"/>. The body is sanitized HTML limited to the editor's elements, every value is
/// HTML-encoded as it renders, and the result is sanitized again, so neither a value nor the template's author can put a
/// script, an image or a style in an email (docs/architecture/rich-text.md). The text part is the HTML as plain text.
/// ScreenHire's wording of the built-in templates is two
/// embedded files each under <c>Email/Templates</c>, <c>{id}.subject.liquid</c> and <c>{id}.body.liquid</c>; an
/// organization's wording comes from its <see cref="EmailTemplate"/> rows. Thread-safe.
/// </summary>
public sealed partial class EmailTemplates
{
    /// <summary>Enough for any email a person writes; a loop that would run longer stops with an error.</summary>
    private const int MaxSteps = 10_000;

    /// <summary>The variables every template may use. <c>reason</c> is the rejection email's alone.</summary>
    public static IReadOnlyList<EmailTemplateVariable> Variables { get; } =
    [
        new("candidateName", "Full name"),
        new("firstName", "First name"),
        new("lastName", "Last name"),
        new("jobTitle", "Job title"),
        new("organizationName", "Organization name"),
        new("stageName", "Stage"),
    ];

    /// <summary>The variable only <see cref="EmailTemplateIds.ApplicationRejected"/> may use, and must.</summary>
    public static EmailTemplateVariable Reason { get; } = new("reason", "Reason");

    /// <summary>The variable only <see cref="EmailTemplateIds.QuestionnaireRequest"/> may use, and must.</summary>
    public static EmailTemplateVariable QuestionnaireLink { get; } = new("questionnaireLink", "Questionnaire link");

    /// <summary>The variables <paramref name="templateId"/> may use: every template's, plus its own required one.</summary>
    public static IReadOnlyList<EmailTemplateVariable> VariablesFor(string? templateId) => templateId switch
    {
        EmailTemplateIds.ApplicationRejected => [.. Variables, Reason],
        EmailTemplateIds.QuestionnaireRequest => [.. Variables, QuestionnaireLink],
        _ => Variables,
    };

    // Filters that only change how a value reads. Nothing that reaches outside the value (date, json, and so on).
    private static readonly FrozenSet<string> AllowedFilters =
        FrozenSet.Create(StringComparer.Ordinal, "capitalize", "default", "downcase", "strip", "truncate", "upcase");

    // Values are HTML-encoded in the body. Every Unicode range is let through, so names read as they are written.
    private static readonly HtmlEncoder BodyEncoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static readonly HtmlParser HtmlParser = new();

    private readonly FluidParser parser = new();
    private readonly TemplateOptions options = new() { MaxSteps = MaxSteps, StrictFilters = true };
    private readonly FrozenDictionary<string, EmailTemplateContent> defaults;

    public EmailTemplates() =>
        defaults = EmailTemplateIds.BuiltIn.ToFrozenDictionary(
            id => id, id => new EmailTemplateContent(Read($"{id}.subject").Trim(), RichText.Sanitize(Read($"{id}.body"))), StringComparer.Ordinal);

    /// <summary>ScreenHire's wording of the built-in template <paramref name="builtInId"/>.</summary>
    public EmailTemplateContent Default(string builtInId) =>
        defaults.TryGetValue(builtInId, out var content)
            ? content
            : throw new ArgumentException($"There is no built-in email template '{builtInId}'.", nameof(builtInId));

    /// <summary>
    /// What is wrong with <paramref name="content"/> as the wording of template <paramref name="templateId"/> (null for
    /// a new organization template): Liquid that does not parse, a tag other than <c>if</c>, <c>unless</c> and
    /// <c>case</c>, a variable outside the list, a rejection email that does not give its reason, or a questionnaire
    /// request that does not give its link.
    /// </summary>
    public IReadOnlyList<EmailTemplateProblem> Validate(string? templateId, EmailTemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var allowed = VariablesFor(templateId).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<EmailTemplateProblem>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var body = RichText.Sanitize(content.Body);
        if (LinkAddresses().Matches(body).Any(link => HoldsLiquid(WebUtility.HtmlDecode(link.Groups[1].Value)))
            || TextAddresses(body).Any(HoldsLiquid))
        {
            // A candidate's details never go to another site inside a link's address, written as a link or as an address
            // in the text that Finish would turn into one.
            problems.Add(new("body", "A link's address cannot hold a variable."));
        }

        foreach (var (field, text) in new[] { ("subject", content.Subject), ("body", Prepare(body)) })
        {
            if (!parser.TryParse(text, out var template, out var error))
            {
                problems.Add(new(field, $"The template does not parse: {error}"));
                continue;
            }

            var checker = new TemplateChecker(allowed);
            checker.VisitTemplate(template);
            problems.AddRange(checker.Problems.Distinct().Select(message => new EmailTemplateProblem(field, message)));
            used.UnionWith(checker.Used);
        }

        if (templateId == EmailTemplateIds.ApplicationRejected && problems.Count == 0 && !used.Contains(Reason.Name))
        {
            // The candidate must be told why (workflow-engine.md §2): a Reject action's reason goes into this email.
            problems.Add(new("body", "The rejection email must give the reason: add {{ reason }}."));
        }

        if (templateId == EmailTemplateIds.QuestionnaireRequest && problems.Count == 0 && !used.Contains(QuestionnaireLink.Name))
        {
            // The email exists to carry the link: without it the candidate has no way to answer.
            problems.Add(new("body", "The follow-up questions email must give the link: add {{ questionnaireLink }}."));
        }

        if (problems.Count == 0)
        {
            // A template that parses can still fail as it runs, such as a loop past the step limit.
            try
            {
                Render(content, "preview@example.com", EmailTemplateModel.Sample("Organization"));
            }
            catch (Exception exception) when (exception is InvalidOperationException or FluidException or ArgumentException)
            {
                // A fixed message: the exception's own text is the engine's, not something to show (dotnet-api.md).
                problems.Add(new("body", "The template does not render. Simplify the last change and try again."));
            }
        }

        return problems;
    }

    /// <summary>The built-in email <paramref name="builtInId"/> in ScreenHire's wording, for <paramref name="to"/>.</summary>
    public EmailMessage Render(string builtInId, string to, EmailTemplateModel model) => Render(Default(builtInId), to, model, builtInId);

    /// <summary>
    /// The email <paramref name="content"/> renders for <paramref name="to"/>, as template <paramref name="templateId"/>.
    /// The rejection email always ends with <see cref="AutomatedDecisionNotice"/>, whatever the organization's wording.
    /// Validate content a person wrote first.
    /// </summary>
    public EmailMessage Render(EmailTemplateContent content, string to, EmailTemplateModel model, string? templateId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(model);
        var context = new TemplateContext(options);
        context.SetValue("candidateName", model.CandidateName);
        context.SetValue("firstName", model.FirstName ?? model.CandidateName);
        context.SetValue("lastName", model.LastName ?? "");
        context.SetValue("jobTitle", model.JobTitle);
        context.SetValue("organizationName", model.OrganizationName);
        context.SetValue("stageName", model.StageName ?? "");
        context.SetValue("reason", model.Reason ?? "");
        context.SetValue("questionnaireLink", model.QuestionnaireLink ?? "");

        // A subject is one line of plain text: no line breaks a header could carry. Variables can make it far longer than
        // its wording, so it is cut to what a kept copy holds, and the candidate gets the same subject the copy keeps.
        var subject = Parse(content.Subject).Render(context, NullEncoder.Default).Trim().ReplaceLineEndings(" ");
        if (subject.Length > ApplicationEmail.MaxSubjectLength)
        {
            var end = char.IsHighSurrogate(subject[ApplicationEmail.MaxSubjectLength - 1])
                ? ApplicationEmail.MaxSubjectLength - 1
                : ApplicationEmail.MaxSubjectLength;
            subject = subject[..end].TrimEnd();
        }
        var body = RichText.Sanitize(content.Body);
        var rendered = Parse(Prepare(body)).Render(context, BodyEncoder);
        if (templateId == EmailTemplateIds.ApplicationRejected)
        {
            rendered += $"<p>{WebUtility.HtmlEncode(AutomatedDecisionNotice(model.OrganizationName))}</p>";
        }

        // Only the addresses the template's author wrote, and the questionnaire link, become links: a value a candidate
        // typed (a name that is a web address) stays text.
        var linkable = TextAddresses(body).Where(address => !HoldsLiquid(address)).ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(model.QuestionnaireLink))
        {
            linkable.Add(model.QuestionnaireLink);
        }

        var html = RichText.Sanitize(Finish(rendered, linkable));
        var text = RichText.ToPlainText(html);
        return new EmailMessage(to, subject, text.ReplaceLineEndings() + Environment.NewLine, ToHtml(html));
    }

    /// <summary>
    /// What ScreenHire adds to every rejection email. Only a Reject action sends it, and that decision is automatic, so
    /// the candidate is told so and offered a person's review (docs/research/legal.md, principle 1: GDPR Art. 22 and the
    /// human-review rules). An organization rewords the rest of the email, never this.
    /// </summary>
    public static string AutomatedDecisionNotice(string organizationName) =>
        "This decision was made automatically from your answer to a screening question. If you think it is wrong, "
        + $"contact the hiring team at {organizationName} and a person will review your application.";

    /// <summary>
    /// The HTML part: the rendered body in a plain page. Email clients ignore stylesheets and CSS variables, so the few
    /// styles are inline and literal: the Warm Grid --card and --foreground tokens (packages/ui/src/tokens.css), light.
    /// </summary>
    internal static string ToHtml(string content) =>
        "<!doctype html>\n<html lang=\"en\">\n<body style=\"margin: 16px; background: #ffffff; color: #1c1b19; font-family: sans-serif; line-height: 1.5;\">\n"
        + content + "\n</body>\n</html>\n";

    /// <summary>
    /// The body as Fluid reads it. The stored body is HTML, so inside <c>{{ ... }}</c> and <c>{% ... %}</c> a comparison
    /// is entity-encoded (<c>&amp;gt;</c>) and formatting can cover part of a tag; Liquid sees the plain characters.
    /// </summary>
    private static string Prepare(string html) =>
        LiquidSpans().Replace(html, span => WebUtility.HtmlDecode(Markup().Replace(span.Value, "")).Replace('\u00a0', ' '));

    private static bool HoldsLiquid(string address) =>
        address.Contains("{{", StringComparison.Ordinal) || address.Contains("{%", StringComparison.Ordinal)
        || address.Contains("%7B%7B", StringComparison.OrdinalIgnoreCase) || address.Contains("%7B%25", StringComparison.OrdinalIgnoreCase);

    /// <summary>The web addresses in the text of <paramref name="html"/>, outside its links' attributes.</summary>
    private static List<string> TextAddresses(string html)
    {
        var body = HtmlParser.ParseDocument("").Body!;
        body.InnerHtml = html;
        return body.Descendants<IText>().SelectMany(text => WebAddress().Matches(text.Data)).Select(match => match.Value).ToList();
    }

    /// <summary>
    /// The rendered body finished for sending: a line break for each newline a value brought (the stored body has none
    /// inside its blocks), and the web addresses in <paramref name="linkable"/> as links that show their own address, such
    /// as <c>{{ questionnaireLink }}</c>.
    /// </summary>
    private static string Finish(string rendered, HashSet<string> linkable)
    {
        var body = HtmlParser.ParseDocument("").Body!;
        body.InnerHtml = rendered;
        var document = body.Owner!;
        foreach (var text in body.Descendants<IText>().ToList())
        {
            var inLink = text.Ancestors<IElement>().Any(element => element.LocalName == "a");
            var parts = new List<INode>();
            var lines = text.Data.ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    parts.Add(document.CreateElement("br"));
                }

                var start = 0;
                foreach (var address in inLink ? [] : WebAddress().Matches(lines[i]).Where(match => linkable.Contains(match.Value)).ToList())
                {
                    parts.Add(document.CreateTextNode(lines[i][start..address.Index]));
                    var link = document.CreateElement("a");
                    link.SetAttribute("href", address.Value);
                    link.TextContent = address.Value;
                    parts.Add(link);
                    start = address.Index + address.Length;
                }

                parts.Add(document.CreateTextNode(lines[i][start..]));
            }

            if (parts.Count > 1)
            {
                text.Replace([.. parts]);
            }
        }

        return body.InnerHtml;
    }

    private IFluidTemplate Parse(string text) =>
        parser.TryParse(text, out var template, out var error)
            ? template
            : throw new InvalidOperationException($"The email template does not parse: {error}");

    private static string Read(string name)
    {
        using var stream = typeof(EmailTemplates).Assembly.GetManifestResourceStream($"ScreenHire.Email.{name}.liquid")
            ?? throw new InvalidOperationException($"The email template file {name}.liquid is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{.*?\}\}|\{%.*?%\}", RegexOptions.Singleline)]
    private static partial Regex LiquidSpans();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"href=""([^""]*)""")]
    private static partial Regex LinkAddresses();

    // An https or http address up to the next space, leaving off punctuation that usually ends the sentence around it.
    [GeneratedRegex(@"https?://[^\s<>""]+[^\s<>"".,;:!?)\]']")]
    private static partial Regex WebAddress();

    /// <summary>
    /// Walks a parsed template and reports anything outside what an email template may do: output and the conditional
    /// tags over the allowed variables, with filters that only change how a value reads. No assignments, loops,
    /// includes or other tags, so a template can neither run long nor reach anything but its own values.
    /// </summary>
    private sealed class TemplateChecker(IReadOnlySet<string> allowed) : AstVisitor
    {
        public List<string> Problems { get; } = [];

        /// <summary>The allowed variables the template uses.</summary>
        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitMemberExpression(MemberExpression memberExpression)
        {
            var segments = memberExpression.Segments;
            if (segments.Count != 1 || segments[0] is not IdentifierSegment { Identifier: var name })
            {
                Problems.Add($"Use a variable on its own, such as {{{{ {Variables[0].Name} }}}}.");
            }
            else if (!allowed.Contains(name))
            {
                Problems.Add($"There is no variable \"{name}\". Use {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
            }
            else
            {
                Used.Add(name);
            }

            return base.VisitMemberExpression(memberExpression);
        }

        protected override Expression VisitFilterExpression(FilterExpression filterExpression)
        {
            if (!AllowedFilters.Contains(filterExpression.Name))
            {
                Problems.Add($"The filter \"{filterExpression.Name}\" is not available. Use {string.Join(", ", AllowedFilters.Order(StringComparer.Ordinal))}.");
            }

            return base.VisitFilterExpression(filterExpression);
        }

        protected override Statement VisitAssignStatement(AssignStatement assignStatement) => Refuse(assignStatement, "assign");

        protected override Statement VisitCaptureStatement(CaptureStatement captureStatement) => Refuse(captureStatement, "capture");

        protected override Statement VisitCycleStatement(CycleStatement cycleStatement) => Refuse(cycleStatement, "cycle");

        protected override Statement VisitDecrementStatement(DecrementStatement decrementStatement) => Refuse(decrementStatement, "decrement");

        protected override Statement VisitIncrementStatement(IncrementStatement incrementStatement) => Refuse(incrementStatement, "increment");

        protected override Statement VisitForStatement(ForStatement forStatement) => Refuse(forStatement, "for");

        protected override Statement VisitTableRowStatement(TableRowStatement tableRowStatement) => Refuse(tableRowStatement, "tablerow");

        protected override Statement VisitIncludeStatement(IncludeStatement includeStatement) => Refuse(includeStatement, "include");

        protected override Statement VisitRenderStatement(RenderStatement renderStatement) => Refuse(renderStatement, "render");

        protected override Statement VisitMacroStatement(MacroStatement macroStatement) => Refuse(macroStatement, "macro");

        protected override Statement VisitFromStatement(FromStatement fromStatement) => Refuse(fromStatement, "from");

        protected override Statement VisitLiquidStatement(LiquidStatement liquidStatement) => Refuse(liquidStatement, "liquid");

        // Raw output is the template's own text, unencoded: in an HTML body it could carry markup for the sanitizer to
        // catch, and an email never needs it.
        protected override Statement VisitRawStatement(RawStatement rawStatement) => Refuse(rawStatement, "raw");

        public override Statement VisitOtherStatement(Statement statement) => Refuse(statement, statement.GetType().Name);

        protected override Expression VisitRangeExpression(RangeExpression rangeExpression)
        {
            Problems.Add("Ranges are not available in email templates.");
            return rangeExpression;
        }

        private Statement Refuse(Statement statement, string tag)
        {
            Problems.Add($"The {{% {tag} %}} tag is not available in email templates. Use {{{{ variables }}}} and {{% if %}}.");
            return statement;
        }
    }
}
