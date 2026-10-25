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

    // The body is plain text: markup its author types reads as text, so a template cannot carry a hidden link or an image.
    [Fact]
    public void Markup_in_a_template_is_text_in_the_html_part()
    {
        var email = Templates.Render(new EmailTemplateContent("Hello", "<img src=\"https://tracker.example.com/p.gif\"><a href=\"x\">Click</a>"), "sam@example.com", Sam);

        email.HtmlBody.Should().NotContain("<img").And.NotContain("<a href=\"x\"").And.Contain("&lt;img");
    }

    [Fact]
    public void Blank_lines_make_paragraphs_and_addresses_become_links_that_show_themselves()
    {
        var email = Templates.Render(
            new EmailTemplateContent("Next steps", "Hi {{ candidateName }},\nwelcome.\n\nBook a time at https://cal.example.com/brightwater?ref=a&b=1.\n\nThanks"),
            "sam@example.com",
            Sam);

        email.HtmlBody.Should().Contain("<p>Hi Sam Rivera,<br>\nwelcome.</p>")
            .And.Contain("<a href=\"https://cal.example.com/brightwater?ref=a&amp;b=1\">https://cal.example.com/brightwater?ref=a&amp;b=1</a>.</p>")
            .And.Contain("<p>Thanks</p>");
        email.TextBody.Should().Contain("Book a time at https://cal.example.com/brightwater?ref=a&b=1.");
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
