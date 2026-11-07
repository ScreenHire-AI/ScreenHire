using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Portal;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Portal;

/// <summary>
/// The candidate's page (docs/architecture/candidate-portal.md): the four-step funnel, the signed link, the line every email
/// to a candidate ends with, the sign-in email, and the reply validator. Data Protection checks a token's limit against the
/// system clock, so a token that has expired is one issued by a provider whose clock was weeks in the past.
/// </summary>
public sealed class CandidatePortalTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly CareersSiteOptions Careers = new() { Scheme = "https", CareersDomain = "careers.example.com", CareersHostPattern = "{tenant}.{CareersDomain}" };

    private readonly EphemeralDataProtectionProvider keyRing = new();

    private CandidatePortalLinks Links(TimeProvider? time = null, CareersSiteOptions? careers = null, PortalOptions? options = null) =>
        new(keyRing, Options.Create(careers ?? Careers), Options.Create(options ?? new PortalOptions()), time ?? TimeProvider.System);

    // The funnel -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(StageKind.Applied, 0)]
    [InlineData(StageKind.Screening, 1)]
    [InlineData(StageKind.Assessment, 1)]
    [InlineData(StageKind.Verification, 1)]
    [InlineData(StageKind.Custom, 1)]
    [InlineData(StageKind.Interview, 2)]
    [InlineData(StageKind.Offer, 3)]
    public void An_open_application_sits_on_one_of_four_fixed_steps_by_the_kind_of_its_stage(StageKind kind, int step)
    {
        var funnel = CandidateFunnel.For(Application(), Stage(kind));

        funnel.Should().Be(new CandidateFunnel(step, FunnelEnd.None));
    }

    [Theory]
    [InlineData(StageKind.Hired, FunnelEnd.Hired)]
    [InlineData(StageKind.Rejected, FunnelEnd.NotMovingForward)]
    [InlineData(StageKind.Withdrawn, FunnelEnd.Withdrawn)]
    public void An_application_in_an_ending_stage_has_ended_and_no_step_is_current(StageKind kind, FunnelEnd ended)
    {
        CandidateFunnel.For(Application(), Stage(kind)).Should().Be(new CandidateFunnel(null, ended));
    }

    [Fact]
    public void A_stage_the_employer_removed_reads_as_in_review()
    {
        CandidateFunnel.For(Application(), stage: null).Should().Be(new CandidateFunnel(1, FunnelEnd.None));
    }

    [Fact]
    public void A_saved_candidate_reads_as_not_moving_forward()
    {
        var application = Application(keepInMind: true);

        CandidateFunnel.For(application, Stage(StageKind.Screening)).Should().Be(new CandidateFunnel(null, FunnelEnd.NotMovingForward));
    }

    [Fact]
    public void An_ending_stage_wins_over_having_been_saved()
    {
        var application = Application(keepInMind: true);

        CandidateFunnel.For(application, Stage(StageKind.Withdrawn)).Ended.Should().Be(FunnelEnd.Withdrawn);
    }

    [Fact]
    public void The_four_steps_are_fixed_and_carry_no_employer_wording()
    {
        CandidateFunnel.Steps.Should().Equal("Applied", "In review", "Interview", "Decision");
    }

    // The signed link ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_link_round_trips_to_the_organization_and_the_candidate()
    {
        var organization = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var links = Links();

        links.Read(links.Issue(organization, candidate)).Should().Be(new CandidatePortalLink(organization, candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CfDJ8forged-token-that-reads-as-nothing")]
    public void Forged_or_empty_tokens_read_as_nothing(string? token)
    {
        Links().Read(token).Should().BeNull();
    }

    [Fact]
    public void A_token_from_another_key_ring_or_made_for_another_purpose_reads_as_nothing()
    {
        var token = Links().Issue(Guid.NewGuid(), Guid.NewGuid());
        var otherInstallation = new CandidatePortalLinks(new EphemeralDataProtectionProvider(), Options.Create(Careers), Options.Create(new PortalOptions()), TimeProvider.System);
        var questionnaireToken = keyRing.CreateProtector("ScreenHire.Questionnaire.Link.v1").Protect($"{Guid.NewGuid():N}.{Guid.NewGuid():N}");

        otherInstallation.Read(token).Should().BeNull();
        Links().Read(questionnaireToken).Should().BeNull("a token for another page cannot open this one");
    }

    [Fact]
    public void An_oversized_token_is_refused_without_being_read()
    {
        Links().Read(new string('A', 1001)).Should().BeNull();
    }

    [Fact]
    public void An_expired_token_reads_as_nothing_like_a_forged_one()
    {
        var link = Links(new FixedTime(DateTimeOffset.UtcNow.AddDays(-30))).Issue(Guid.NewGuid(), Guid.NewGuid());

        Links().Read(link).Should().BeNull("the link lasted 14 days, thirty days ago");
    }

    [Fact]
    public void A_token_just_inside_its_lifetime_still_reads()
    {
        var options = new PortalOptions { LinkLifetime = TimeSpan.FromDays(14) };
        var organization = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var recent = Links(new FixedTime(DateTimeOffset.UtcNow.AddDays(-13)), options: options).Issue(organization, candidate);

        Links(options: options).Read(recent).Should().Be(new CandidatePortalLink(organization, candidate));
    }

    [Fact]
    public void The_address_is_on_the_organizations_careers_board_and_absent_without_a_host_pattern()
    {
        var links = Links();
        var organization = Guid.NewGuid();
        var candidate = Guid.NewGuid();

        links.Url("acme", "tok").Should().Be("https://acme.careers.example.com/my-applications/tok");
        links.UrlFor("acme", organization, candidate).Should().StartWith("https://acme.careers.example.com/my-applications/");
        Links(careers: new CareersSiteOptions { CareersHostPattern = "" }).UrlFor("acme", organization, candidate).Should().BeNull();
    }

    // The line every email ends with -----------------------------------------------------------------------------------

    [Fact]
    public void The_footer_ends_both_parts_of_an_email_with_the_link()
    {
        var message = new EmailMessage("sam@example.com", "Hello", "Hi Sam.", "<p>Hi Sam.</p>");

        var routed = new MailRoute(PortalLink: "https://acme.careers.example.com/my-applications/tok?a=1&b=2").Apply(message);

        routed.TextBody.Should().StartWith("Hi Sam.").And.EndWith("https://acme.careers.example.com/my-applications/tok?a=1&b=2\n");
        routed.HtmlBody.Should().StartWith("<p>Hi Sam.</p>").And.Contain("href=\"https://acme.careers.example.com/my-applications/tok?a=1&amp;b=2\"")
            .And.Contain(CandidatePortalFooter.Sentence);
        routed.Subject.Should().Be("Hello");
    }

    [Fact]
    public void The_footer_goes_inside_the_body_of_a_rendered_email()
    {
        var rendered = new EmailMessage("sam@example.com", "Hello", "Hi Sam.", "<!doctype html>\n<html lang=\"en\">\n<body>\n<p>Hi Sam.</p>\n</body>\n</html>\n");

        var html = new MailRoute(PortalLink: "https://acme.careers.example.com/my-applications/tok").Apply(rendered).HtmlBody!;

        html.IndexOf(CandidatePortalFooter.Sentence, StringComparison.Ordinal).Should().BeGreaterThan(html.IndexOf("<p>Hi Sam.</p>", StringComparison.Ordinal))
            .And.BeLessThan(html.IndexOf("</body>", StringComparison.Ordinal), "a line after the end of the document is one a mail client may drop");
    }

    [Fact]
    public void A_route_without_a_link_changes_nothing_in_the_body_and_the_original_message_is_never_touched()
    {
        var message = new EmailMessage("sam@example.com", "Hello", "Hi Sam.", "<p>Hi Sam.</p>");

        MailRoute.None.Apply(message).Should().Be(message);
        CandidatePortalFooter.Append(message, "  ").Should().Be(message);
        new MailRoute(PortalLink: "https://x.example.com/p").Apply(message);
        message.TextBody.Should().Be("Hi Sam.", "the copy the team keeps is the message as rendered");
    }

    [Fact]
    public void The_footer_wording_has_no_dashes_and_makes_no_claim_about_decisions()
    {
        (CandidatePortalFooter.Sentence + CandidatePortalFooter.LinkText).Should().NotMatchRegex("[\u2013\u2014]");
    }

    // What a reply keeps ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_quoted_footer_gives_up_the_candidates_own_link_and_keeps_the_page_that_asks_for_one()
    {
        var links = Links();
        var token = links.Issue(Guid.NewGuid(), Guid.NewGuid());

        var kept = PortalMessageText.WithoutPortalToken($"Friday works.\n\n> {CandidatePortalFooter.Sentence} {links.Url("acme", token)}\n");

        kept.Should().NotContain(token).And.Contain("https://acme.careers.example.com/my-applications\n").And.StartWith("Friday works.");
    }

    [Fact]
    public void Text_about_the_page_with_no_token_in_it_is_left_alone()
    {
        const string text = "See /my-applications/sent or https://acme.careers.example.com/my-applications for more.";

        PortalMessageText.WithoutPortalToken(text).Should().Be(text);
    }

    [Fact]
    public void The_same_form_id_on_two_applications_is_two_replies()
    {
        const string key = "0123456789abcdef0123456789abcdef";

        PortalMessageText.MessageIdFor(Guid.NewGuid(), key).Should().NotBe(PortalMessageText.MessageIdFor(Guid.NewGuid(), key))
            .And.StartWith(PortalMessageText.MessageIdPrefix);
    }

    // The sign-in email ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_sign_in_email_names_the_organization_the_link_and_how_long_it_works()
    {
        var message = PortalEmails.LinkEmail("sam@example.com", "Acme & Sons", "https://acme.careers.example.com/my-applications/tok", TimeSpan.FromDays(14));

        message.To.Should().Be("sam@example.com");
        message.Subject.Should().Be("Your link to your applications at Acme & Sons");
        message.TextBody.Should().Contain("https://acme.careers.example.com/my-applications/tok").And.Contain("The link works for 14 days.")
            .And.Contain("If you didn't ask for this you can ignore this email.");
        message.HtmlBody.Should().Contain("href=\"https://acme.careers.example.com/my-applications/tok\"").And.Contain("Acme &amp; Sons");
        message.TextBody.Should().NotMatchRegex("[\u2013\u2014]");
        message.TextBody.ToUpperInvariant().Should().NotContain("VERIF").And.NotContain("SCORE").And.NotContain("FRAUD");
    }

    [Theory]
    [InlineData(14, "14 days")]
    [InlineData(1, "1 day")]
    public void A_lifetime_in_whole_days_is_said_in_days(int days, string said)
    {
        PortalEmails.Describe(TimeSpan.FromDays(days)).Should().Be(said);
    }

    [Fact]
    public void Shorter_lifetimes_are_said_in_hours_or_minutes_and_never_as_nothing()
    {
        PortalEmails.Describe(TimeSpan.FromHours(1)).Should().Be("1 hour");
        PortalEmails.Describe(TimeSpan.FromHours(36)).Should().Be("36 hours");
        PortalEmails.Describe(TimeSpan.FromMinutes(90)).Should().Be("90 minutes");
        PortalEmails.Describe(TimeSpan.FromSeconds(5)).Should().Be("1 minute");
    }

    [Fact]
    public void The_sign_in_message_key_is_stable_for_one_claim_and_differs_between_claims_and_candidates()
    {
        var candidate = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        SendPortalLink.KeyFor(candidate, at).Should().Be(SendPortalLink.KeyFor(candidate, at));
        SendPortalLink.KeyFor(candidate, at).Should().NotBe(SendPortalLink.KeyFor(candidate, at.AddMinutes(6)));
        SendPortalLink.KeyFor(candidate, at).Should().NotBe(SendPortalLink.KeyFor(Guid.NewGuid(), at));
    }

    // Messages -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_kept_copys_placeholder_reads_as_where_the_link_is_on_the_candidates_own_page()
    {
        var kept = $"Pick a time: {ApplicationEmails.QuestionnaireLinkNotKept}\nThanks";

        var shown = PortalMessageText.ForCandidate(kept);

        shown.Should().Be($"Pick a time: {PortalMessageText.LinkIsInTheEmail}\nThanks");
        shown.Should().NotContain("not kept");
    }

    [Theory]
    [InlineData("Your interview", "Re: Your interview")]
    [InlineData("Re: Your interview", "Re: Your interview")]
    [InlineData("  RE: Hello  ", "RE: Hello")]
    public void A_reply_subject_has_one_re_in_front(string subject, string expected)
    {
        PortalMessageText.ReplySubject(subject).Should().Be(expected);
    }

    [Fact]
    public void A_reply_from_the_page_is_stored_under_a_message_id_the_same_form_always_makes()
    {
        var application = Guid.NewGuid();
        var key = new string('a', 32);

        PortalMessageText.MessageIdFor(application, key).Should().Be(PortalMessageText.MessageIdFor(application, key)).And.StartWith(PortalMessageText.MessageIdPrefix);
        PortalMessageText.MessageIdFor(application, key).Should().NotBe(PortalMessageText.MessageIdFor(application, new string('b', 32)));
    }

    // The reply validator --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Thanks, Friday works.", true)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void A_reply_needs_words(string body, bool valid)
    {
        Validator().Validate(new PublicPortalReplyRequest(body, new string('a', 32))).IsValid.Should().Be(valid);
    }

    [Fact]
    public void A_reply_is_cut_off_at_the_configured_length_and_needs_the_forms_own_id()
    {
        var validator = Validator(new PortalOptions { ReplyBodyMax = 10 });

        validator.Validate(new PublicPortalReplyRequest(new string('x', 10), new string('a', 32))).IsValid.Should().BeTrue();
        validator.Validate(new PublicPortalReplyRequest(new string('x', 11), new string('a', 32))).IsValid.Should().BeFalse();
        validator.Validate(new PublicPortalReplyRequest("hi", "not-a-key")).IsValid.Should().BeFalse();
        validator.Validate(new PublicPortalReplyRequest("hi", "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_reply_takes_up_to_three_files()
    {
        var file = new PublicPortalAttachment("cv.pdf", "JVBERi0=");
        var validator = Validator();

        validator.Validate(new PublicPortalReplyRequest("hi", new string('a', 32), [file, file, file])).IsValid.Should().BeTrue();
        validator.Validate(new PublicPortalReplyRequest("hi", new string('a', 32), [file, file, file, file])).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("sam@example.com", true)]
    [InlineData("not an address", false)]
    [InlineData("", false)]
    public void The_sign_in_form_needs_an_email_address(string email, bool valid)
    {
        new PublicPortalLinkRequestValidator().Validate(new PublicPortalLinkRequest(email)).IsValid.Should().Be(valid);
    }

    private static PublicPortalReplyRequestValidator Validator(PortalOptions? options = null) => new(Options.Create(options ?? new PortalOptions()));

    private static JobApplication Application(bool keepInMind = false)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Barista", Slug = "barista" };
        var candidate = Candidate.Create(organization, "Alex", "Rivera", "alex@example.com", null, Monday);
        var intake = new ApplicationIntake(ApplicationSource.CareersSite, [], FutureRoles: new FutureRolesConsent(Monday));
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, null, intake);
        if (keepInMind)
        {
            application.KeepInMind(job.Workflow.Stages[0], Monday.AddDays(1), Guid.CreateVersion7(), "A note the candidate never sees");
        }

        return application;
    }

    private static WorkflowStage Stage(StageKind kind) => new("stage", "A name only the employer uses", kind);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
