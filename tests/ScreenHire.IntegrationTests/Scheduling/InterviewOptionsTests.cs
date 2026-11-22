using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Scheduling;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using static ScreenHire.IntegrationTests.Scheduling.SchedulingOptionSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The three ways to pick a time through the API, the worker and Mailpit (docs/architecture/interview-scheduling.md, "Three ways
/// to pick a time"): pick from our times (here from interview blocks, with interviewers who have no calendar), tell us when
/// you're free (the candidate sends 3 to 5 times, the team books one or asks for more) and we'll suggest a time (the candidate
/// accepts one or asks for another). The slot grid, the hours and the booking checks themselves are the unit tests'.
/// </summary>
public sealed class InterviewOptionsTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Interview_blocks_offer_times_to_interviewers_with_no_calendar_and_a_booking_puts_them_on_ScreenHires_calendar()
    {
        using var world = await InterviewWorld.CreateAsync(fixture, connectCalendars: false);

        var created = (await world.ScheduleAsync("OurTimes", more: body =>
        {
            body["useBlocks"] = true;
            body["hours"] = WeekdayHours("13:00", "15:00");
        })).Expect(HttpStatusCode.Created);
        var interviewId = created.Id();

        created.Text("option").Should().Be("OurTimes");
        created.At("useBlocks").GetBoolean().Should().BeTrue();
        var invitation = await world.WaitForEmailAsync(world.CandidateEmail, OurTimesSubject);
        var token = InterviewWorld.TokenIn(invitation.Text);
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("option").Should().Be("OurTimes");
        page.At("slotsUnavailable").GetBoolean().Should().BeFalse("blocks need no calendar");
        var slots = InterviewWorld.SlotsOf(page);
        slots.Should().NotBeEmpty();
        slots.Should().OnlyContain(
            s => s.UtcDateTime.TimeOfDay >= TimeSpan.FromHours(13) && s.UtcDateTime.TimeOfDay <= TimeSpan.FromHours(14.5),
            "a 30 minute interview fits inside the 13:00 to 15:00 block");

        var chosen = FarSlot(slots);
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        await world.WaitForSyncAsync(interviewId, "Synced");
        (await world.AttendeesAsync(interviewId)).Should().OnlyContain(
            a => a.Provider == CalendarProvider.ScreenHire && a.SyncStatus == InterviewSyncStatus.Synced, "no provider holds their events");
        (await world.InterviewAsync(interviewId)).GetProperty("startsAt").GetDateTimeOffset().Should().Be(chosen);
        await world.WaitForEmailAsync(world.CandidateEmail, "is set");
    }

    [Fact]
    public async Task Tell_us_when_you_are_free_refuses_too_few_and_too_many_times_and_the_candidate_cannot_book_their_own_time()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.OfferedAsync("TheirTimes", TheirTimesSubject);

        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("option").Should().Be("TheirTimes");
        var suggestable = page.TimesOf("suggestable");
        suggestable.Should().HaveCountGreaterThan(6, "every time inside the window and hours is one to choose from");
        InterviewWorld.SlotsOf(page).Should().BeEmpty("the candidate picks from a grid, not from offered slots");

        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(suggestable.Take(2)) }))
            .Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_few_times");
        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(suggestable.Take(6)) }))
            .Expect(HttpStatusCode.Conflict).Text("type").Should().Be("too_many_times");
        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times([suggestable[0], suggestable[1], suggestable[2].AddMinutes(7)]) }))
            .Expect(HttpStatusCode.Conflict).Text("type").Should().Be("not_offered");
        (await world.BookAsync(token, suggestable[0])).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("not_offered");
        (await world.RescheduleAsync(token, suggestable[0])).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("not_booked");
        (await world.InterviewAsync(interviewId)).GetProperty("status").GetString().Should().Be("Open", "nothing the candidate tried changed it");
    }

    [Fact]
    public async Task The_team_books_one_of_the_times_the_candidate_suggested_and_the_booking_runs_like_any_other()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var job = await AutomationSamples.JobWithAutomationsAsync(world.Owner, "Suggested times job", TagWhenBooked());
        var email = ApiSession.NewEmail("candidate");
        var application = (await world.Owner.CreateApplicationAsync(job, email)).Id();
        var created = (await world.ScheduleAsync("TheirTimes", applicationId: application)).Expect(HttpStatusCode.Created);
        var interviewId = created.Id();
        var token = InterviewWorld.TokenIn((await world.WaitForEmailAsync(email, TheirTimesSubject)).Text);
        var suggestable = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).TimesOf("suggestable");
        var sent = new[] { suggestable[0], suggestable[8], suggestable[16] };

        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(sent) })).Expect(HttpStatusCode.NoContent);

        var waiting = await world.InterviewAsync(interviewId, application);
        waiting.GetProperty("candidateTimes").EnumerateArray().Select(t => t.GetDateTimeOffset()).Should().Equal(sent.Order());
        waiting.GetProperty("waitsForTeam").GetBoolean().Should().BeTrue();
        var afterSend = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        afterSend.TimesOf("suggestedTimes").Should().Equal(sent.Order());
        afterSend.At("waitingForTeam").GetBoolean().Should().BeTrue();
        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(sent) })).Expect(HttpStatusCode.Conflict)
            .Text("type").Should().Be("not_offered", "the times were sent already");

        // A time the candidate did not send is refused; one they sent is booked, for the whole organization.
        var other = suggestable[3];
        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/book", new JsonObject { ["start"] = other })).Expect(HttpStatusCode.Conflict);
        var booked = (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/book", new JsonObject { ["start"] = sent[1] })).Expect(HttpStatusCode.OK);
        booked.Text("status").Should().Be("Booked");
        booked.At("startsAt").GetDateTimeOffset().Should().Be(sent[1]);

        await world.WaitForEmailAsync(email, "is set");
        await world.WaitForSyncAsync(interviewId, "Synced", application);
        await world.WaitForTimelineAsync("InterviewBooked", application);
        var tagged = await WaitForTagAsync(world, application, "interview-booked");
        tagged.Should().BeTrue("the InterviewScheduled trigger fired for a time the team booked");
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("state").Should().Be("Booked");
    }

    [Fact]
    public async Task Asking_for_more_times_clears_the_candidates_times_and_emails_them_the_teams_message_on_the_same_link()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (interviewId, token) = await world.OfferedAsync("TheirTimes", TheirTimesSubject);
        var suggestable = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).TimesOf("suggestable");

        // Nothing to ask about before the candidate has sent times.
        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/more-times", new JsonObject { ["note"] = "Mornings suit us." })).Expect(HttpStatusCode.Conflict);

        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(suggestable.Take(3)) })).Expect(HttpStatusCode.NoContent);
        var asked = (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/more-times", new JsonObject { ["note"] = "Mornings suit us." })).Expect(HttpStatusCode.OK);

        asked.At("candidateTimes").GetArrayLength().Should().Be(0);
        asked.At("waitsForTeam").GetBoolean().Should().BeFalse("the next step is the candidate's again");
        asked.Text("teamNote").Should().Be("Mornings suit us.");
        var email = await world.WaitForEmailAsync(world.CandidateEmail, MoreTimesSubject);
        email.Text.Should().Contain("Mornings suit us.");
        // The email's link is a fresh signature of the same interview, and the one the candidate already has still works.
        (await world.PublicGetAsync(InterviewWorld.TokenIn(email.Text))).Expect(HttpStatusCode.OK).Text("option").Should().Be("TheirTimes");
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.TimesOf("suggestedTimes").Should().BeEmpty();
        page.TimesOf("suggestable").Should().NotBeEmpty("they can send times again");
        page.Text("teamNote").Should().Be("Mornings suit us.");
        (await world.PublicPostAsync(token, "/suggest", new JsonObject { ["times"] = Times(suggestable.Skip(5).Take(3)) })).Expect(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_candidate_accepts_one_of_the_teams_proposed_times_through_the_booking_route()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var proposed = new[] { DaysOut(3), DaysOut(4) };
        var (interviewId, token) = await world.OfferedAsync("OurSuggestion", OurSuggestionSubject, body => body["proposedTimes"] = Times(proposed));

        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("option").Should().Be("OurSuggestion");
        page.TimesOf("proposedTimes").Should().Equal(proposed);
        InterviewWorld.SlotsOf(page).Should().BeEmpty();

        (await world.BookAsync(token, proposed[1].AddMinutes(30))).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("slot_taken");
        (await world.BookAsync(token, proposed[1])).Expect(HttpStatusCode.NoContent);

        var booked = await world.InterviewAsync(interviewId);
        booked.GetProperty("status").GetString().Should().Be("Booked");
        booked.GetProperty("startsAt").GetDateTimeOffset().Should().Be(proposed[1]);
        await world.WaitForSyncAsync(interviewId, "Synced");
        await world.WaitForEmailAsync(world.CandidateEmail, "is set");
    }

    [Fact]
    public async Task Another_time_please_waits_for_the_team_tells_the_interviewers_and_hides_the_proposed_times()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var proposed = new[] { DaysOut(3), DaysOut(4) };
        var (interviewId, token) = await world.OfferedAsync("OurSuggestion", OurSuggestionSubject, body => body["proposedTimes"] = Times(proposed));

        (await world.PublicPostAsync(token, "/another-time", new JsonObject { ["note"] = new string('x', 501) })).Expect(HttpStatusCode.BadRequest);
        (await world.PublicPostAsync(token, "/another-time", new JsonObject { ["note"] = "Mondays are hard for me." })).Expect(HttpStatusCode.NoContent);

        var waiting = await world.InterviewAsync(interviewId);
        waiting.GetProperty("waitsForTeam").GetBoolean().Should().BeTrue();
        waiting.GetProperty("candidateNote").GetString().Should().Be("Mondays are hard for me.");
        waiting.GetProperty("anotherTimeAskedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.At("anotherTimeAsked").GetBoolean().Should().BeTrue();
        page.At("waitingForTeam").GetBoolean().Should().BeTrue();
        page.TimesOf("proposedTimes").Should().BeEmpty("the candidate has said none of them works");
        (await world.BookAsync(token, proposed[0])).Expect(HttpStatusCode.Conflict).Text("type").Should().NotBeNullOrEmpty();
        (await world.PublicPostAsync(token, "/another-time", new JsonObject())).Expect(HttpStatusCode.Conflict).Text("type").Should().Be("not_offered");

        // Both interviewers hear about it in the bell.
        foreach (var member in new[] { world.Owner, world.Second })
        {
            await WolverineStore.WaitUntilAsync(
                async () => (await member.GetAsync("/v1/notifications")).Expect(HttpStatusCode.OK).At("items").EnumerateArray()
                    .Any(n => n.GetProperty("kind").GetString() == "interview-changed" && n.GetProperty("payload").GetProperty("change").GetString() == "AnotherTimeAsked"),
                "the interviewer's bell to say the candidate asked for another time",
                Ct);
        }
    }

    private static JsonArray Times(IEnumerable<DateTimeOffset> times) => new([.. times.Select(t => (JsonNode)JsonValue.Create(t)!)]);

    private static JsonObject TagWhenBooked() => new()
    {
        ["id"] = "tag-when-booked",
        ["name"] = "Tag when booked",
        ["trigger"] = new JsonObject { ["type"] = "InterviewScheduled" },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "interview-booked" }),
    };

    private static async Task<bool> WaitForTagAsync(InterviewWorld world, Guid application, string tag)
    {
        await WolverineStore.WaitUntilAsync(
            async () => (await world.Owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK).At("tags").EnumerateArray()
                .Any(t => t.GetString() == tag),
            $"the application to carry the tag {tag}",
            Ct);
        return true;
    }
}
