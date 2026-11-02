using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Verification;
using ScreenHire.IntegrationTests.Messaging;
using static ScreenHire.IntegrationTests.CandidateTestSupport;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A candidate's history (<c>GET /v1/candidates/{id}/history</c>): the timeline events of every application they made,
/// newest first, each with its application and job. Verification output and what a reviewer scored stay out of it, other
/// organizations' candidates and jobs outside a member's access are not found, and an API key is refused.
/// </summary>
public sealed class CandidateHistoryTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_lists_the_events_of_both_applications_newest_first_with_their_job_titles_and_without_verification_or_scores()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = await fixture.ApplyAsync(slug, firstJob, person, Ct);
        var second = (await owner.CreateApplicationAsync(secondJob, person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, first);
        // The assessment lands on the first application's timeline, so its absence from the history means something.
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/applications/{first}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                .Any(e => e.GetProperty("type").GetString() == "VerificationAssessed"),
            "the verification assessment to reach the timeline",
            Ct);

        // One at a time, so each event is later than the one before it.
        (await owner.PostAsync($"/v1/applications/{first}/notes", new JsonObject { ["note"] = "Strong first call." })).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{second}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{second}/scorecards", Submission("interview", "Yes", InterviewRatings(4, 5, 5)))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{first}/tags", new JsonObject { ["tag"] = "Referral" })).Expect(HttpStatusCode.OK);

        var history = (await owner.GetAsync($"/v1/candidates/{candidateId}/history")).Expect(HttpStatusCode.OK);

        var entries = history.Body.EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("entry").GetProperty("occurredAt").GetDateTimeOffset()).Should().BeInDescendingOrder();
        entries.Select(e => e.GetProperty("entry").GetProperty("type").GetString()).Should().NotContain("VerificationAssessed");
        entries.Where(e => e.GetProperty("entry").GetProperty("type").GetString() == "Submitted")
            .Select(e => (e.GetProperty("applicationId").GetGuid(), e.GetProperty("jobId").GetGuid(), e.GetProperty("jobTitle").GetString()))
            .Should().BeEquivalentTo([(first, firstJob, "Platform Engineer"), (second, secondJob, "Data Analyst")]);

        // The four events this test made, in the order they happened, newest first.
        var mine = entries.Where(e => e.GetProperty("entry").GetProperty("type").GetString() is "TagAdded" or "ScorecardSubmitted" or "NoteAdded"
            || (e.GetProperty("entry").GetProperty("type").GetString() == "StageChanged" && e.GetProperty("entry").GetProperty("toStageId").GetString() == "interview")).ToList();
        mine.Select(e => e.GetProperty("entry").GetProperty("type").GetString()).Should().Equal("TagAdded", "ScorecardSubmitted", "StageChanged", "NoteAdded");
        mine.Select(e => e.GetProperty("jobTitle").GetString()).Should().Equal("Platform Engineer", "Data Analyst", "Data Analyst", "Platform Engineer");
        mine.Select(e => e.GetProperty("applicationId").GetGuid()).Should().Equal(first, second, second, first);
        mine.Select(e => e.GetProperty("jobId").GetGuid()).Should().Equal(firstJob, secondJob, secondJob, firstJob);

        // The scorecard is a person's judgement (ADR-0013): the application's own timeline has it, the history says only that one was given.
        var timeline = (await owner.GetAsync($"/v1/applications/{second}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "ScorecardSubmitted");
        timeline.GetProperty("scorecardId").ValueKind.Should().Be(JsonValueKind.String);
        timeline.GetProperty("recommendation").GetString().Should().Be("Yes");
        var scorecard = mine[1].GetProperty("entry");
        scorecard.GetProperty("scorecardId").ValueKind.Should().Be(JsonValueKind.Null);
        scorecard.GetProperty("recommendation").ValueKind.Should().Be(JsonValueKind.Null);
        history.Body.GetRawText().Should().NotContain(timeline.GetProperty("scorecardId").GetString()!);
    }

    [Fact]
    public async Task Another_organizations_candidate_and_an_unknown_one_have_no_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var stranger = await fixture.SignedInWithOrganizationAsync();
        var application = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), ApiSession.NewEmail("person"))).Id();
        var candidateId = await CandidateOfAsync(owner, application);

        (await owner.GetAsync($"/v1/candidates/{candidateId}/history")).Expect(HttpStatusCode.OK).Count().Should().BeGreaterThan(0);
        (await stranger.GetAsync($"/v1/candidates/{candidateId}/history")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/candidates/{Guid.NewGuid()}/history")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_reads_the_history_of_those_jobs_and_a_candidate_with_none_of_them_is_404()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var visibleJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var hiddenJob = await OpenJobTitledAsync(owner, "Payroll Specialist");
        var both = ApiSession.NewEmail("both");
        (await owner.CreateApplicationAsync(visibleJob, both)).Id();
        var hiddenApplication = (await owner.CreateApplicationAsync(hiddenJob, both)).Id();
        var hiddenOnly = await owner.CreateApplicationAsync(hiddenJob, ApiSession.NewEmail("hidden"));
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(visibleJob, await owner.MemberIdAsync(viewer.UserId));
        var candidateId = await CandidateOfAsync(owner, hiddenApplication);

        var seen = (await viewer.GetAsync($"/v1/candidates/{candidateId}/history")).Expect(HttpStatusCode.OK);
        var ownerSees = (await owner.GetAsync($"/v1/candidates/{candidateId}/history")).Expect(HttpStatusCode.OK);

        seen.Body.EnumerateArray().Select(e => e.GetProperty("jobTitle").GetString()).Distinct().Should().Equal("Platform Engineer");
        ownerSees.Body.EnumerateArray().Select(e => e.GetProperty("jobTitle").GetString()).Distinct().Should().BeEquivalentTo("Platform Engineer", "Payroll Specialist");
        (await viewer.GetAsync($"/v1/candidates/{hiddenOnly.At("candidate").GetProperty("id").GetGuid()}/history")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_organization_API_key_cannot_read_a_candidates_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var application = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), ApiSession.NewEmail("person"))).Id();
        var candidateId = await CandidateOfAsync(owner, application);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Reporting", ["Read"])).Text("token"));

        (await key.GetAsync($"/v1/candidates/{candidateId}/history")).Status.Should().Be(HttpStatusCode.Unauthorized, "only the hiring API accepts keys");
        (await key.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
    }
}
