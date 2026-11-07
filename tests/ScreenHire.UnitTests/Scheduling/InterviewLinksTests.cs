using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The signed link to the careers page where a candidate picks an interview time. Data Protection checks a token's limit
/// against the system clock, so the tests set the window relative to the real now, or far in the past.
/// </summary>
public sealed class InterviewLinksTests
{
    private static readonly CareersSiteOptions Careers = new() { Scheme = "https", CareersDomain = "careers.example.com", CareersHostPattern = "{tenant}.{CareersDomain}" };

    private readonly EphemeralDataProtectionProvider keyRing = new();

    private InterviewLinks Links(IDataProtectionProvider? provider = null) => new(provider ?? keyRing, Options.Create(Careers));

    [Fact]
    public void A_token_round_trips_to_the_organization_and_the_interview()
    {
        var interview = Request(DateTimeOffset.UtcNow);
        var links = Links();

        var link = links.Read(links.Issue(interview));

        link.Should().Be(new InterviewLink(interview.OrganizationId, interview.Id));
    }

    [Fact]
    public void The_same_token_serves_many_reads_for_pick_move_and_cancel()
    {
        var interview = Request(DateTimeOffset.UtcNow);
        var links = Links();
        var token = links.Issue(interview);

        links.Read(token).Should().NotBeNull();
        links.Read(token).Should().NotBeNull();
    }

    [Fact]
    public void A_token_from_another_key_ring_does_not_read()
    {
        var interview = Request(DateTimeOffset.UtcNow);

        Links(new EphemeralDataProtectionProvider()).Read(Links().Issue(interview)).Should().BeNull();
    }

    [Fact]
    public void A_token_made_for_another_purpose_does_not_read()
    {
        var interview = Request(DateTimeOffset.UtcNow);
        var questionnaireLink = new QuestionnaireLinks(keyRing, Options.Create(Careers));
        var application = Application(interview.OrganizationId);
        var request = QuestionnaireRequest.Create(
            application,
            new FollowUpQuestionnaire("followup", "Follow up", []),
            DateTimeOffset.UtcNow);

        Links().Read(questionnaireLink.Issue(request)).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void Nothing_or_garbage_reads_as_null(string? token) => Links().Read(token).Should().BeNull();

    [Fact]
    public void A_tampered_or_overlong_token_reads_as_null()
    {
        var links = Links();
        var token = links.Issue(Request(DateTimeOffset.UtcNow));
        var tampered = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        links.Read(tampered).Should().BeNull();
        links.Read(token + token + token + token + token + token).Should().BeNull("a token this long is not ours");
    }

    [Fact]
    public void A_token_reads_until_thirty_days_after_the_window_ends_and_not_after()
    {
        var links = Links();

        // The window ended 29 days ago: the link reads, so the page can say it expired.
        links.Read(links.Issue(Request(DateTimeOffset.UtcNow.AddDays(-29)))).Should().NotBeNull();
        // The window ended 31 days ago: the token is past its own limit.
        links.Read(links.Issue(Request(DateTimeOffset.UtcNow.AddDays(-31)))).Should().BeNull();
        InterviewLinks.Grace.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void A_token_reads_past_a_booked_time_that_is_later_than_the_offered_window()
    {
        var links = Links();
        var booked = DateTimeOffset.UtcNow.AddDays(-60);
        var interview = InterviewRequest.Create(Application(), 30, booked.AddDays(1), booked.AddDays(80), "UTC", null, booked);
        interview.Book(DateTimeOffset.UtcNow.AddDays(10), booked);

        // New days that ended long ago: the reminders for the booked time still link to the page.
        interview.OfferNewTimes(booked.AddDays(2), booked.AddDays(5), booked.AddDays(1));

        (interview.WindowEnd + InterviewLinks.Grace).Should().BeBefore(DateTimeOffset.UtcNow);
        links.Read(links.Issue(interview)).Should().NotBeNull();
    }

    [Fact]
    public void The_link_is_the_interview_page_on_the_organizations_careers_board()
    {
        Links().Url("acme", "TOKEN").Should().Be("https://acme.careers.example.com/interviews/TOKEN");
        Links().IsConfigured.Should().BeTrue();
        new InterviewLinks(keyRing, Options.Create(new CareersSiteOptions())).IsConfigured.Should().BeFalse("no careers host pattern means a link would name no host");
    }

    [Fact]
    public void The_messages_carry_keys_made_from_ids_so_a_redelivery_is_the_same_intent()
    {
        var (organization, application, interview, send) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        new SendInterviewInvitation(organization, application, interview, send, null).IdempotencyKey
            .Should().Be(new SendInterviewInvitation(organization, application, interview, send, Guid.CreateVersion7()).IdempotencyKey, "who sent it is not the intent");
        new SendInterviewInvitation(organization, application, interview, Guid.CreateVersion7(), null).IdempotencyKey
            .Should().NotBe(new SendInterviewInvitation(organization, application, interview, send, null).IdempotencyKey, "a new send is a new intent");

        new CreateInterviewEvents(organization, application, interview, 1).IdempotencyKey.Should().Be(new CreateInterviewEvents(organization, application, interview, 1).IdempotencyKey);
        new[]
        {
            new CreateInterviewEvents(organization, application, interview, 1).IdempotencyKey,
            new CreateInterviewEvents(organization, application, interview, 2).IdempotencyKey,
            new UpdateInterviewEvents(organization, application, interview, 1).IdempotencyKey,
            new CancelInterviewEvents(organization, application, interview, 1).IdempotencyKey,
        }.Should().OnlyHaveUniqueItems("each revision and each kind of change is its own intent");
    }

    private static JobApplication Application(Guid? organization = null)
    {
        var org = organization ?? Guid.CreateVersion7();
        var job = new Job { OrganizationId = org, Title = "Support engineer", Slug = "support-engineer" };
        var candidate = Candidate.Create(org, "Grace", "Adeyemi", "grace@example.com", null, DateTimeOffset.UtcNow);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, DateTimeOffset.UtcNow, null,
            new ApplicationIntake(ApplicationSource.CareersSite, []));
        return application;
    }

    // A request whose window ends at windowEnd (and started a week before, in the past if need be).
    private static InterviewRequest Request(DateTimeOffset windowEnd) =>
        InterviewRequest.Create(Application(), 30, windowEnd.AddDays(-7), windowEnd, "UTC", null, windowEnd.AddDays(-8));
}
