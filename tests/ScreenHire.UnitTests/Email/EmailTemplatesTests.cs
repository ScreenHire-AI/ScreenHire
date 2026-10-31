using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Email;

public sealed class EmailTemplatesTests
{
    private static readonly EmailTemplates Templates = new();

    private static readonly EmailTemplateModel Sam = new("Sam Rivera", "Warehouse lead", "Brightwater", "Phone screen", "No right to work.");

    [Theory]
    [InlineData(EmailTemplateIds.ApplicationReceived, "We received your application for Warehouse lead")]
    [InlineData(EmailTemplateIds.StageChange, "An update on your application for Warehouse lead")]
    [InlineData(EmailTemplateIds.ApplicationRejected, "Your application for Warehouse lead")]
    public void Every_built_in_template_renders_a_subject_a_text_part_and_an_html_part(string templateId, string subject)
    {
        var email = Templates.Render(templateId, "sam@example.com", Sam);

        email.To.Should().Be("sam@example.com");
        email.Subject.Should().Be(subject);
        email.TextBody.Should().StartWith("Hi Sam Rivera,").And.Contain("Brightwater");
        email.HtmlBody.Should().Contain("<p>Hi Sam Rivera,</p>").And.Contain("Warehouse lead");
    }

    [Fact]
    public void An_email_can_greet_the_candidate_by_their_first_name_and_use_their_last()
    {
        var email = Templates.Render(
            new EmailTemplateContent("For {{ firstName }} {{ lastName }}", "Hi {{ firstName }}, {{ candidateName }}."),
            "sam@example.com", Sam with { FirstName = "Sam", LastName = "Rivera" });

        email.Subject.Should().Be("For Sam Rivera");
        email.TextBody.Should().StartWith("Hi Sam, Sam Rivera.");
    }

    // A subject's wording is at most 200 characters, but variables can render it far longer, and a kept copy holds 1000.
    [Fact]
    public void A_subject_that_variables_make_too_long_is_cut_to_what_a_kept_copy_holds()
    {
        var longName = new string('n', 300);
        var email = Templates.Render(
            new EmailTemplateContent(string.Concat(Enumerable.Repeat("{{ candidateName }} ", 10)), "Hi."),
            "sam@example.com", Sam with { CandidateName = longName });

        email.Subject.Length.Should().Be(ApplicationEmail.MaxSubjectLength);
        email.Subject.Should().StartWith(longName + " " + longName);
    }

    [Fact]
    public void A_cut_subject_never_ends_half_way_through_a_character()
    {
        var email = Templates.Render(
            new EmailTemplateContent("{{ candidateName }}", "Hi."), "sam@example.com",
            Sam with { CandidateName = new string('n', ApplicationEmail.MaxSubjectLength - 1) + "\U0001F600" });

        email.Subject.Should().Be(new string('n', ApplicationEmail.MaxSubjectLength - 1));
    }

    // A candidate from before the name was split has their whole name as the first name and no last name.
    [Fact]
    public void A_candidate_with_one_name_is_greeted_by_all_of_it()
    {
        var email = Templates.Render(new EmailTemplateContent("Hi", "Hi {{ firstName }}{{ lastName }}."), "sam@example.com", Sam);

        email.TextBody.Should().StartWith("Hi Sam Rivera.");
    }

    [Fact]
    public void Wording_a_person_writes_by_hand_may_use_the_names_but_not_an_actions_own_variables()
    {
        Templates.Validate(null, new EmailTemplateContent("Hi {{ firstName }}", "{{ lastName }} {{ candidateName }}")).Should().BeEmpty();
        Templates.Validate(null, new EmailTemplateContent("Hi", "{{ reason }}")).Should().ContainSingle(p => p.Field == "body");
        Templates.Validate(null, new EmailTemplateContent("Hi", "{{ questionnaireLink }}")).Should().ContainSingle(p => p.Field == "body");
    }

    [Fact]
    public void The_rejection_email_tells_the_candidate_why()
    {
        var email = Templates.Render(EmailTemplateIds.ApplicationRejected, "sam@example.com", Sam with { Reason = "This role needs a driving licence." });

        email.TextBody.Should().Contain("The reason: This role needs a driving licence.")
            .And.Contain("made automatically").And.Contain("a person will review your application");
    }

    // The automated decision and the offer of a person's review come from ScreenHire, so rewording cannot drop them.
    [Fact]
    public void A_reworded_rejection_email_still_says_the_decision_was_automatic_and_offers_a_person()
    {
        var email = Templates.Render(
            new EmailTemplateContent("Your application", "Hi {{ candidateName }}. Not this time: {{ reason }}"), "sam@example.com", Sam,
            EmailTemplateIds.ApplicationRejected);

        email.TextBody.Should().Contain("Not this time: No right to work.")
            .And.EndWith(EmailTemplates.AutomatedDecisionNotice("Brightwater") + Environment.NewLine);
        email.HtmlBody.Should().Contain("a person will review your application");
        Templates.Render(new EmailTemplateContent("Hi", "Hello"), "sam@example.com", Sam, EmailTemplateIds.StageChange)
            .TextBody.Should().NotContain("made automatically");
    }

    [Fact]
    public void Every_built_in_template_passes_its_own_checks()
    {
        foreach (var id in EmailTemplateIds.BuiltIn)
        {
            Templates.Validate(id, Templates.Default(id)).Should().BeEmpty(id);
        }
    }

    [Fact]
    public void Values_cannot_inject_markup_or_extra_headers()
    {
        var email = Templates.Render(
            EmailTemplateIds.StageChange, "sam@example.com", new("<script>alert(1)</script>", "Lead\r\nBcc: someone@example.org", "Org", "Screening"));

        email.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        email.Subject.Should().NotContain("\n").And.NotContain("\r");
    }

    // The body is rich text limited to the editor's elements: an image, a script or a relative link never reaches an email.
    [Fact]
    public void Markup_outside_the_editors_elements_never_reaches_the_html_part()
    {
        var email = Templates.Render(
            new EmailTemplateContent("Hello", "<p><img src=\"https://tracker.example.com/p.gif\"><script>steal()</script><a href=\"x\">Click</a></p>"),
            "sam@example.com", Sam);

        email.HtmlBody.Should().NotContain("<img").And.NotContain("<script").And.NotContain("steal()").And.NotContain("href=\"x\"")
            .And.Contain("<p>Click</p>");
    }

    [Fact]
    public void The_editors_formatting_reaches_both_parts()
    {
        var email = Templates.Render(
            new EmailTemplateContent("Next steps", "<h2>Next steps</h2><p>Hi <strong>{{ firstName }}</strong>,</p><ol><li>Book a time</li><li>Bring ID</li></ol>"
                + "<p>Read <a href=\"https://example.com/faq\">our FAQ</a>.</p>"),
            "sam@example.com", Sam);

        email.HtmlBody.Should().Contain("<h2>Next steps</h2><p>Hi <strong>Sam Rivera</strong>,</p><ol><li>Book a time</li><li>Bring ID</li></ol>")
            .And.Contain("<a href=\"https://example.com/faq\" rel=\"nofollow noopener noreferrer\">our FAQ</a>");
        email.TextBody.ReplaceLineEndings("\n").Should().Be("Next steps\n\nHi Sam Rivera,\n\n1. Book a time\n2. Bring ID\n\nRead our FAQ (https://example.com/faq).\n");
    }

    [Fact]
    public void Plain_text_paragraphs_still_read_and_addresses_become_links_that_show_themselves()
    {
        var email = Templates.Render(
            new EmailTemplateContent("Next steps", "Hi {{ candidateName }},\nwelcome.\n\nBook a time at https://cal.example.com/brightwater?ref=a&b=1.\n\nThanks"),
            "sam@example.com",
            Sam);

        email.HtmlBody.Should().Contain("<p>Hi Sam Rivera,<br>welcome.</p>")
            .And.Contain("<a href=\"https://cal.example.com/brightwater?ref=a&amp;b=1\" rel=\"nofollow noopener noreferrer\">https://cal.example.com/brightwater?ref=a&amp;b=1</a>.</p>")
            .And.Contain("<p>Thanks</p>");
        email.TextBody.Should().Contain("Book a time at https://cal.example.com/brightwater?ref=a&b=1.");
    }

    [Fact]
    public void The_questionnaire_link_is_a_link()
    {
        var email = Templates.Render(
            EmailTemplateIds.QuestionnaireRequest, "sam@example.com", Sam with { QuestionnaireLink = "https://careers.example.com/q/abc?t=1&s=2" });

        email.HtmlBody.Should().Contain("<a href=\"https://careers.example.com/q/abc?t=1&amp;s=2\"");
        email.TextBody.Should().Contain("Answer them here: https://careers.example.com/q/abc?t=1&s=2");
    }

    [Fact]
    public void A_value_with_several_lines_keeps_them()
    {
        var email = Templates.Render(EmailTemplateIds.ApplicationRejected, "sam@example.com", Sam with { Reason = "No right to work.\nNo licence." });

        email.HtmlBody.Should().Contain("The reason: No right to work.<br>No licence.");
        email.TextBody.ReplaceLineEndings("\n").Should().Contain("The reason: No right to work.\nNo licence.");
    }

    [Fact]
    public void Liquid_in_the_html_reads_as_the_author_wrote_it()
    {
        // The editor stores a comparison entity-encoded, and bold can cover part of a tag.
        var content = new EmailTemplateContent("Hi", "<p>{% if stageName &gt; \"A\" %}In <strong>{{ stageName</strong> }}{% endif %}</p><p>{% if reason == \"x\" %}Never{% endif %}</p>");

        Templates.Validate(null, content with { Body = content.Body.Replace("reason", "lastName", StringComparison.Ordinal) }).Should().BeEmpty();
        var email = Templates.Render(content, "sam@example.com", Sam);
        email.HtmlBody.Should().Contain("<p>In <strong>Phone screen</strong></p>").And.NotContain("<p></p>").And.NotContain("Never");
    }

    [Fact]
    public void A_link_address_cannot_hold_a_variable()
    {
        Templates.Validate(null, new("Hi", "<p><a href=\"https://example.com/?name={{ firstName }}\">Go</a></p>"))
            .Should().ContainSingle().Which.Message.Should().Be("A link's address cannot hold a variable.");
    }

    [Fact]
    public void Every_built_in_template_is_already_sanitized()
    {
        foreach (var id in EmailTemplateIds.BuiltIn)
        {
            var body = Templates.Default(id).Body;
            ScreenHire.Infrastructure.Content.RichText.Sanitize(body).Should().Be(body, id);
            body.Should().StartWith("<p>", id);
        }
    }

    [Fact]
    public void A_template_may_branch_on_a_variable()
    {
        var content = new EmailTemplateContent("Hi", "{% if stageName %}You are in {{ stageName | downcase }}.{% else %}Thanks.{% endif %}");

        Templates.Validate(null, content).Should().BeEmpty();
        Templates.Render(content, "sam@example.com", Sam).TextBody.Should().StartWith("You are in phone screen.");
    }

    [Theory]
    [InlineData("{{ candidateEmail }}", "There is no variable \"candidateEmail\"")]
    [InlineData("{{ reason }}", "There is no variable \"reason\"")]
    [InlineData("{{ candidateName.size }}", "Use a variable on its own")]
    [InlineData("{% assign x = 1 %}", "The {% assign %} tag is not available")]
    [InlineData("{% for i in (1..100000) %}x{% endfor %}", "The {% for %} tag is not available")]
    [InlineData("{% include 'other' %}", "The {% include %} tag is not available")]
    [InlineData("{% raw %}{{ jobTitle }}{% endraw %}", "The {% raw %} tag is not available")]
    [InlineData("{{ jobTitle | date: '%Y' }}", "The filter \"date\" is not available")]
    [InlineData("{{ jobTitle ", "does not parse")]
    public void A_template_uses_only_the_listed_variables_and_safe_tags(string body, string problem)
    {
        var problems = Templates.Validate(null, new("Subject", body));

        problems.Should().ContainSingle().Which.Should().Match<EmailTemplateProblem>(p => p.Field == "body" && p.Message.Contains(problem));
    }

    [Fact]
    public void The_subject_is_checked_too()
    {
        Templates.Validate(null, new("Hi {{ password }}", "Body")).Should().ContainSingle().Which.Field.Should().Be("subject");
    }

    // A Reject action's reason goes into this email: the candidate must be told why (workflow-engine.md §2).
    [Fact]
    public void The_rejection_email_must_give_its_reason()
    {
        Templates.Validate(EmailTemplateIds.ApplicationRejected, new("Your application", "Hi {{ candidateName }}, no thanks."))
            .Should().ContainSingle().Which.Message.Should().Contain("{{ reason }}");
        Templates.Validate(EmailTemplateIds.ApplicationRejected, new("Your application", "Hi {{ candidateName }}. The reason: {{ reason }}"))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("application-received", true)]
    [InlineData("0199f0c4-0000-7000-8000-000000000001", true)]
    [InlineData("0199F0C4-0000-7000-8000-000000000001", true)]
    [InlineData("{0199f0c4-0000-7000-8000-000000000001}", false)]
    [InlineData("rejection", false)]
    public void A_template_id_is_a_built_in_id_or_a_guid(string id, bool wellFormed) =>
        EmailTemplateIds.IsWellFormed(id).Should().Be(wellFormed);
}
