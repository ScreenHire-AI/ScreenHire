using System.Text.Json;
using static ScreenHire.IntegrationTests.Scheduling.SchedulingOptionSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The subscribe link of ScreenHire's calendar (docs/architecture/interview-scheduling.md, "The subscribe link"): a member's
/// booked interviews as an iCalendar feed that a calendar app reads without signing in. The token is the secret, so a forged,
/// reset, turned-off or another organization's link finds nothing, and the feed never names the candidate.
/// </summary>
public sealed class CalendarFeedTests(AppHostFixture fixture)
{
    private const string FeedRoute = "/v1/account/interview-hours/feed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_feed_lists_a_booked_interview_by_job_and_never_names_the_candidate()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var feed = await TurnOnAsync(world.Owner);
        var (interviewId, token) = await world.ScheduledAsync();
        var chosen = FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        using var response = await world.Careers.GetAsync(feed, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        response.Headers.CacheControl!.NoStore.Should().BeTrue("a calendar app must always ask again");
        text.Should().Contain("BEGIN:VCALENDAR").And.Contain($"interview-{interviewId:N}").And.Contain("Platform Engineer")
            .And.Contain($"DTSTART:{chosen.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        text.Should().NotContain("Casey").And.NotContain("Morgan").And.NotContain(world.CandidateEmail, "a link that leaks shows no one's name or address");

        // A cancelled interview leaves the feed.
        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK);
        var afterCancel = await world.Careers.GetStringAsync(feed, Ct);
        afterCancel.Should().NotContain($"interview-{interviewId:N}");
    }

    [Fact]
    public async Task Resetting_or_turning_off_the_link_ends_the_old_one_and_a_forged_token_finds_nothing()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var first = await TurnOnAsync(world.Owner);
        (await StatusAsync(world, first)).Should().Be(HttpStatusCode.OK);

        var second = await TurnOnAsync(world.Owner);

        second.Should().NotBe(first, "a new link has a new secret");
        (await StatusAsync(world, first)).Should().Be(HttpStatusCode.NotFound, "the reset ended the old link");
        (await StatusAsync(world, second)).Should().Be(HttpStatusCode.OK);
        var inside = second.LastIndexOf('/') + 10;
        var tampered = second[..inside] + (second[inside] == 'A' ? 'B' : 'A') + second[(inside + 1)..];
        (await StatusAsync(world, tampered)).Should().Be(HttpStatusCode.NotFound, "a token with a changed character is not one we issued");
        (await StatusAsync(world, "/v1/public/calendar-feeds/not-a-token.ics")).Should().Be(HttpStatusCode.NotFound);

        var off = (await world.Owner.DeleteAsync(FeedRoute)).Expect(HttpStatusCode.OK);
        off.At("feedUrl").ValueKind.Should().Be(JsonValueKind.Null);
        (await StatusAsync(world, second)).Should().Be(HttpStatusCode.NotFound, "the link is off");

        // Turned on again, it is a third link and the earlier two stay dead.
        var third = await TurnOnAsync(world.Owner);
        third.Should().NotBe(first).And.NotBe(second);
        (await StatusAsync(world, third)).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(world, second)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_members_feed_holds_only_their_own_organizations_interviews()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var ourFeed = await TurnOnAsync(world.Owner);
        var theirFeed = await TurnOnAsync(theirs);
        var (interviewId, token) = await world.ScheduledAsync();
        (await world.BookAsync(token, FarSlot(InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK))))).Expect(HttpStatusCode.NoContent);

        (await world.Careers.GetStringAsync(ourFeed, Ct)).Should().Contain($"interview-{interviewId:N}");
        var other = await world.Careers.GetStringAsync(theirFeed, Ct);

        other.Should().Contain("BEGIN:VCALENDAR").And.NotContain($"interview-{interviewId:N}").And.NotContain("Platform Engineer");
    }

    // Turns the link on (or resets it) and returns its path: the calendar app reads the feed from the API's own address here.
    private static async Task<string> TurnOnAsync(Tenant member)
    {
        var settings = (await member.PostAsync(FeedRoute)).Expect(HttpStatusCode.OK);
        return new Uri(settings.Text("feedUrl")).AbsolutePath;
    }

    private static async Task<HttpStatusCode> StatusAsync(InterviewWorld world, string path)
    {
        using var response = await world.Careers.GetAsync(path, Ct);
        return response.StatusCode;
    }
}
