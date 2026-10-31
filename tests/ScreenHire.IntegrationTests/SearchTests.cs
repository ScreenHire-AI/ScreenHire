namespace ScreenHire.IntegrationTests;

/// <summary>
/// The dashboard's search (<c>GET /v1/search</c>): candidates by name or email and jobs by title, within the reader's
/// organization and the jobs they see (ADR-0015), for a signed-in person only.
/// </summary>
public sealed class SearchTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Search_finds_candidates_by_name_or_email_and_jobs_by_title()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Support Engineer"));
        await recruiter.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"));
        var email = ApiSession.NewEmail("quinlan");
        var application = await recruiter.CreateApplicationAsync(job.Id(), email, "Morgan Quinlan");
        await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("other"), "Sam Patel");

        var byName = (await recruiter.GetAsync("/v1/search?q=quinl")).Expect(HttpStatusCode.OK);
        byName.Count("candidates").Should().Be(1);
        byName.Text("candidates.0.applicationId").Should().Be(application.Text("id"));
        byName.Text("candidates.0.candidateName").Should().Be("Morgan Quinlan");
        byName.Text("candidates.0.email").Should().Be(email);
        byName.Text("candidates.0.jobTitle").Should().Be("Support Engineer");
        byName.Text("candidates.0.stage.name").Should().Be("Applied");
        byName.Count("jobs").Should().Be(0);

        var byEmail = (await recruiter.GetAsync($"/v1/search?q={Uri.EscapeDataString(email)}")).Expect(HttpStatusCode.OK);
        byEmail.Text("candidates.0.candidateName").Should().Be("Morgan Quinlan");

        var byTitle = (await recruiter.GetAsync("/v1/search?q=support")).Expect(HttpStatusCode.OK);
        byTitle.Count("jobs").Should().Be(1);
        byTitle.Text("jobs.0.title").Should().Be("Support Engineer");
        byTitle.Text("jobs.0.status").Should().Be("Draft");
    }

    [Fact]
    public async Task Names_that_start_with_the_search_come_first_and_the_list_is_capped()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Support Engineer"));
        // The oldest, so only the prefix rule can put it first among the newer matches.
        await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("first"), "Ray Alvarez");
        for (var i = 0; i < 9; i++)
        {
            await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("match"), $"Casey Ray{i}");
        }

        var found = (await recruiter.GetAsync("/v1/search?q=ray")).Expect(HttpStatusCode.OK);
        found.Count("candidates").Should().Be(8);
        found.Text("candidates.0.candidateName").Should().Be("Ray Alvarez");
    }

    [Fact]
    public async Task Search_never_finds_another_organization_or_a_job_outside_the_readers_hiring_teams()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var theirs = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var hidden = await owner.CreateJobAsync(Samples.RemoteJob("Platform Payroll"));
        await owner.CreateApplicationAsync(theirs.Id(), ApiSession.NewEmail("seen"), "Robin Visible");
        await owner.CreateApplicationAsync(hidden.Id(), ApiSession.NewEmail("unseen"), "Robin Hidden");
        using var member = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(theirs.Id(), await owner.MemberIdAsync(member.UserId));
        using var stranger = await fixture.SignedInWithOrganizationAsync();

        var narrow = (await member.GetAsync("/v1/search?q=robin")).Expect(HttpStatusCode.OK);
        narrow.Body.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("candidateName").GetString())
            .Should().Equal("Robin Visible");
        var jobs = (await member.GetAsync("/v1/search?q=platform")).Expect(HttpStatusCode.OK);
        jobs.Body.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("title").GetString()).Should().Equal("Platform Engineer");

        var outside = (await stranger.GetAsync("/v1/search?q=robin")).Expect(HttpStatusCode.OK);
        outside.Count("candidates").Should().Be(0);
        outside.Count("jobs").Should().Be(0);
    }

    [Fact]
    public async Task Search_needs_two_to_a_hundred_characters_and_a_signed_in_person()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await owner.GetAsync("/v1/search?q=a")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("q");
        (await owner.GetAsync("/v1/search")).Expect(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"/v1/search?q={new string('a', 101)}")).Expect(HttpStatusCode.BadRequest);

        using var anonymous = fixture.CreateHttpClient("api");
        (await anonymous.GetAsync("/v1/search?q=robin", TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var key = await owner.CreateKeyAsync("ATS sync", ["Read"]);
        using var integrator = new KeyClient(fixture, key.Text("token"));
        (await integrator.GetAsync("/v1/search?q=robin")).Status
            .Should().Be(HttpStatusCode.Unauthorized, "search is the dashboard's, not the hiring API's");
    }
}
