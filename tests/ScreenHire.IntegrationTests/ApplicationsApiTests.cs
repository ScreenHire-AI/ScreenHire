using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Applications, their timeline and candidates (<c>/v1/jobs/{id}/applications</c>, <c>/v1/applications</c>,
/// <c>/v1/candidates</c>), against the shared AppHost. Each test has its own organization. Jobs here use the default
/// workflow: applied, screening, interview, offer, hired, rejected, withdrawn.
/// </summary>
public sealed class ApplicationsApiTests(AppHostFixture fixture)
{
    private const string CandidateEmail = "casey.morgan@example.com";

    [Fact]
    public async Task Adding_an_application_puts_it_in_the_first_stage_with_one_submitted_timeline_entry()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));

        var created = await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail);

        created.Text("stage.id").Should().Be("applied");
        created.Text("stage.name").Should().Be("Applied");
        created.Text("stage.kind").Should().Be("Applied");
        created.Text("jobId").Should().Be(job.Text("id"));
        created.Text("jobTitle").Should().Be("Platform Engineer");
        created.Text("candidate.email").Should().Be(CandidateEmail);
        created.Text("candidate.fullName").Should().Be("Casey Morgan");
        created.At("workflowVersion").GetInt32().Should().Be(1);
        created.Text("aiUseDeclaration").Should().Be("NotProvided");
        created.At("accommodationRequested").GetBoolean().Should().BeFalse();
        var timeline = (await recruiter.GetAsync($"/v1/applications/{created.Id()}/timeline")).Expect(HttpStatusCode.OK);
        timeline.Count().Should().Be(1);
        timeline.Text("0.type").Should().Be("Submitted");
        timeline.Text("0.toStageId").Should().Be("applied");
        timeline.Text("0.actorUserId").Should().Be(recruiter.UserId.ToString(), "the recruiter who added it is the actor");
        var fetched = await recruiter.GetAsync($"/v1/applications/{created.Id()}");
        fetched.Expect(HttpStatusCode.OK).Text("candidate.id").Should().Be(created.Text("candidate.id"));
        var listed = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Expect(HttpStatusCode.OK);
        listed.Count().Should().Be(1);
        listed.Text("0.candidateName").Should().Be("Casey Morgan");
        listed.Text("0.stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task The_candidates_declarations_are_stored_with_the_application()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Declarations"));
        var extra = new JsonObject { ["accommodationRequested"] = true, ["aiUseDeclaration"] = "Used", ["aiUseDetails"] = "Grammar checks only." };

        var created = await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail, extra: extra);

        created.At("accommodationRequested").GetBoolean().Should().BeTrue();
        created.Text("aiUseDeclaration").Should().Be("Used");
        created.Text("aiUseDetails").Should().Be("Grammar checks only.");
        (await recruiter.GetAsync($"/v1/applications/{created.Id()}")).Text("aiUseDeclaration").Should().Be("Used");
    }

    [Fact]
    public async Task The_same_email_cannot_apply_to_the_same_job_twice_whatever_its_case()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Only once"));
        await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail);

        var again = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", Candidate("Casey Morgan", "  Casey.Morgan@Example.com "));

        again.Expect(HttpStatusCode.Conflict);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Count().Should().Be(1);
        (await recruiter.GetAsync("/v1/candidates")).Count().Should().Be(1);
    }

    [Fact]
    public async Task Applying_to_a_second_job_reuses_the_candidate()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var first = await recruiter.CreateJobAsync(Samples.RemoteJob("First role"));
        var second = await recruiter.CreateJobAsync(Samples.RemoteJob("Second role"));
        var one = await recruiter.CreateApplicationAsync(first.Id(), CandidateEmail);

        var two = await recruiter.CreateApplicationAsync(second.Id(), CandidateEmail);

        two.Text("candidate.id").Should().Be(one.Text("candidate.id"));
        (await recruiter.GetAsync("/v1/candidates")).Count().Should().Be(1);
        var candidate = (await recruiter.GetAsync($"/v1/candidates/{one.Text("candidate.id")}")).Expect(HttpStatusCode.OK);
        candidate.Text("email").Should().Be(CandidateEmail);
        candidate.Count("applications").Should().Be(2);
        candidate.At("applications").EnumerateArray().Select(a => a.GetProperty("jobId").GetString())
            .Should().BeEquivalentTo(first.Text("id"), second.Text("id"));
    }

    [Fact]
    public async Task Moving_an_application_changes_its_stage_and_appends_a_stage_changed_entry()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Mover"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail);

        var moved = (await recruiter.PostAsync(
            $"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening", ["note"] = "Strong portfolio." }))
            .Expect(HttpStatusCode.OK);

        moved.Text("stage.id").Should().Be("screening");
        moved.Text("stage.kind").Should().Be("Screening");
        moved.At("stageEnteredAt").GetDateTimeOffset().Should().BeAfter(moved.At("submittedAt").GetDateTimeOffset());
        var timeline = (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Expect(HttpStatusCode.OK);
        timeline.Count().Should().Be(2);
        timeline.Text("0.type").Should().Be("Submitted");
        timeline.Text("1.type").Should().Be("StageChanged");
        timeline.Text("1.fromStageId").Should().Be("applied");
        timeline.Text("1.toStageId").Should().Be("screening");
        timeline.Text("1.note").Should().Be("Strong portfolio.");
        timeline.Text("1.actorUserId").Should().Be(recruiter.UserId.ToString());
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Text("0.stage.id").Should().Be("screening");
    }

    [Fact]
    public async Task Moving_to_a_stage_the_job_does_not_have_is_a_validation_error_and_changes_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Strict mover"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail);

        var result = await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "no-such-stage" });

        result.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("toStageId");
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}")).Text("stage.id").Should().Be("applied");
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Count().Should().Be(1);
    }

    [Fact]
    public async Task Moving_to_the_current_stage_succeeds_without_a_new_timeline_entry()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Idle mover"));
        var application = await recruiter.CreateApplicationAsync(job.Id(), CandidateEmail);
        await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" });

        var again = await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" });

        again.Expect(HttpStatusCode.OK).Text("stage.id").Should().Be("screening");
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}/timeline")).Count().Should().Be(2);
    }

    [Fact]
    public async Task A_closed_job_takes_no_applications()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Closed for applications"));
        await recruiter.CloseJobAsync(job.Id());

        var result = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", Candidate("Casey Morgan", CandidateEmail));

        result.Expect(HttpStatusCode.Conflict);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Count().Should().Be(0);
        (await recruiter.GetAsync("/v1/candidates")).Count().Should().Be(0, "a refused application does not create a candidate");
    }

    [Fact]
    public async Task An_application_to_an_unknown_job_is_not_found()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var result = await recruiter.PostAsync($"/v1/jobs/{Guid.NewGuid()}/applications", Candidate("Casey Morgan", CandidateEmail));

        result.Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync("/v1/candidates")).Count().Should().Be(0);
    }

    [Fact]
    public async Task Unknown_applications_and_candidates_are_not_found()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var missing = Guid.NewGuid();

        (await recruiter.GetAsync($"/v1/applications/{missing}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/applications/{missing}/timeline")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.PostAsync($"/v1/applications/{missing}/move", new JsonObject { ["toStageId"] = "screening" })).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/candidates/{missing}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/jobs/{missing}/applications")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_application_needs_a_name_and_a_valid_email()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Validation"));

        var badEmail = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", Candidate("Casey Morgan", "not-an-email"));
        var noName = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", Candidate("", CandidateEmail));

        badEmail.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("candidate.email");
        noName.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("candidate.fullName");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Count().Should().Be(0);
    }

    [Fact]
    public async Task Application_and_candidate_endpoints_need_a_signed_in_user()
    {
        using var visitor = new ApiSession(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        using var candidates = await visitor.Client.GetAsync("/v1/candidates", cancellationToken);
        using var application = await visitor.Client.GetAsync($"/v1/applications/{id}", cancellationToken);
        using var timeline = await visitor.Client.GetAsync($"/v1/applications/{id}/timeline", cancellationToken);
        using var create = await visitor.PostAsync($"/v1/jobs/{id}/applications", Candidate("Casey Morgan", CandidateEmail), cancellationToken);
        using var move = await visitor.PostAsync($"/v1/applications/{id}/move", new { toStageId = "screening" }, cancellationToken);

        new[] { candidates, application, timeline, create, move }.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
    }

    private static JsonObject Candidate(string fullName, string email) =>
        new() { ["candidate"] = new JsonObject { ["fullName"] = fullName, ["email"] = email } };
}
