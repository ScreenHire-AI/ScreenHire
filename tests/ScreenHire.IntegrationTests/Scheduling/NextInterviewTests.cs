using System.Text.Json;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Scheduling;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// The applications list's <c>nextInterviewAt</c>, which the Jobs overview counts as Interviews this week
/// (docs/architecture/notifications.md, "The Jobs overview"): the booked interview that has not started yet, and nothing for an
/// interview that is past, not booked or cancelled.
/// </summary>
public sealed class NextInterviewTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_gives_an_application_its_booked_interview_only_while_it_is_still_ahead()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organizationId = owner.OrganizationId;
        var ahead = await fixture.AddApplicationAsync(organizationId, Ct);
        var past = await fixture.AddApplicationAsync(organizationId, Ct);
        var open = await fixture.AddApplicationAsync(organizationId, Ct);
        var cancelled = await fixture.AddApplicationAsync(organizationId, Ct);
        var none = await fixture.AddApplicationAsync(organizationId, Ct);
        var now = SchedulingTestSupport.Now();
        var start = now.AddDays(2);

        InterviewRequest Booked(JobApplication application)
        {
            var interview = InterviewRequest.Create(application, 30, now, now.AddDays(14), "UTC", null, now);
            interview.Book(start, now);
            return interview;
        }

        var pastInterview = Booked(past);
        var cancelledInterview = Booked(cancelled);
        await using (var db = await fixture.ContextAsync(organizationId, Ct))
        {
            db.InterviewRequests.AddRange(
                Booked(ahead), pastInterview, cancelledInterview, InterviewRequest.Create(open, 30, now, now.AddDays(14), "UTC", null, now));
            await db.SaveChangesAsync(Ct);
        }

        // An interview that already took place, and one called off: the states the scheduling flow leaves behind.
        await fixture.OwnerSqlAsync(
            "UPDATE interview_requests SET starts_at = now() - interval '1 day', ends_at = now() - interval '1 day' + interval '30 minutes' WHERE id = @id",
            ("id", pastInterview.Id));
        await fixture.OwnerSqlAsync("UPDATE interview_requests SET status = 'Cancelled' WHERE id = @id", ("id", cancelledInterview.Id));

        var list = (await owner.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetGuid(), a => a.GetProperty("nextInterviewAt"));

        list[ahead.Id].GetDateTimeOffset().Should().Be(start);
        list[past.Id].ValueKind.Should().Be(JsonValueKind.Null, "it has started");
        list[open.Id].ValueKind.Should().Be(JsonValueKind.Null, "an open request has no time yet");
        list[cancelled.Id].ValueKind.Should().Be(JsonValueKind.Null, "it was called off");
        list[none.Id].ValueKind.Should().Be(JsonValueKind.Null);
    }
}
