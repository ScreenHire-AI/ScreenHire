using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// User A, signed in to organization A, reaches nothing of organization B's through the API: not in lists and not by
/// direct id. Row-level security behind the same rule is in <see cref="RowLevelSecurityTests"/>.
/// </summary>
public sealed class TenantIsolationTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Job_list_shows_only_the_users_own_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (a, organizationA) = await SignedInWithOrganizationAsync(cancellationToken);
        var (b, organizationB) = await SignedInWithOrganizationAsync(cancellationToken);
        await fixture.AddJobsAsync(organizationA, [TestData.NewJob(organizationA, "a-only")], cancellationToken);
        await fixture.AddJobsAsync(organizationB, [TestData.NewJob(organizationB, "b-only")], cancellationToken);

        var jobsA = await a.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken);
        var jobsB = await b.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken);

        jobsA.EnumerateArray().Select(j => j.GetProperty("slug").GetString()).Should().Equal("a-only");
        jobsB.EnumerateArray().Select(j => j.GetProperty("slug").GetString()).Should().Equal("b-only");
        a.Dispose();
        b.Dispose();
    }

    [Fact]
    public async Task Another_organizations_job_is_not_found_by_its_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (a, organizationA) = await SignedInWithOrganizationAsync(cancellationToken);
        var other = await fixture.CreateOrganizationAsync(cancellationToken: cancellationToken);
        var own = TestData.NewJob(organizationA, "own");
        var theirs = TestData.NewJob(other, "theirs");
        await fixture.AddJobsAsync(organizationA, [own], cancellationToken);
        await fixture.AddJobsAsync(other.Id, [theirs], cancellationToken);

        using var ownResponse = await a.Client.GetAsync($"/v1/jobs/{own.Id}", cancellationToken);
        using var theirResponse = await a.Client.GetAsync($"/v1/jobs/{theirs.Id}", cancellationToken);
        using var missingResponse = await a.Client.GetAsync($"/v1/jobs/{Guid.NewGuid()}", cancellationToken);

        ownResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("slug").GetString().Should().Be("own");
        theirResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "another tenant's row looks exactly like a missing one");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        a.Dispose();
    }

    [Fact]
    public async Task Brightwaters_jobs_are_invisible_to_another_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid brightwaterJob;
        using (var owner = new ApiSession(fixture))
        {
            await owner.SignInWithDevSsoAsync("brightwater-owner", cancellationToken);
            brightwaterJob = (await owner.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken))[0].GetProperty("id").GetGuid();
        }

        var (outsider, _) = await SignedInWithOrganizationAsync(cancellationToken);

        (await outsider.Client.GetAsync($"/v1/jobs/{brightwaterJob}", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken)).GetArrayLength().Should().Be(0);
        outsider.Dispose();
    }

    [Fact]
    public async Task Visiting_another_organizations_careers_page_does_not_change_whose_jobs_a_user_sees()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (a, organizationA) = await SignedInWithOrganizationAsync(cancellationToken);
        await fixture.AddJobsAsync(organizationA, [TestData.NewJob(organizationA, "a-draft")], cancellationToken);
        var other = await fixture.CreateOrganizationAsync(org =>
        {
            var job = TestData.NewJob(org, "b-open");
            job.Publish(DateTimeOffset.UtcNow);
            return [job, TestData.NewJob(org, "b-draft")];
        }, cancellationToken);

        var board = await a.Client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{other.Slug}/jobs", cancellationToken);
        var own = await a.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken);

        board.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("slug").GetString()).Should().Equal("b-open");
        own.EnumerateArray().Select(j => j.GetProperty("slug").GetString()).Should().Equal("a-draft");
        a.Dispose();
    }

    [Fact]
    public async Task User_without_an_organization_sees_no_jobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);

        (await session.Client.GetFromJsonAsync<JsonElement>("/v1/jobs", cancellationToken)).GetArrayLength().Should().Be(0);
    }

    private async Task<(ApiSession Session, Guid OrganizationId)> SignedInWithOrganizationAsync(CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail(), cancellationToken);
        return (session, await session.OnboardAsync(cancellationToken));
    }
}
