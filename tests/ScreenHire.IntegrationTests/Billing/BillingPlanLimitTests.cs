using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// The plan's active job limit at <c>POST /v1/jobs/{id}/publish</c> (ADR-0029). Only opening a draft counts, against the
/// jobs open across the whole organization; an open job stays open and readable whatever happens to the plan. The test
/// stack pins Starter at 1 open job.
/// </summary>
public sealed class BillingPlanLimitTests(AppHostFixture fixture)
{
    private const string PlanLimit = "urn:screenhire:problem:plan-limit";
    private const string SubscriptionEnded = "urn:screenhire:problem:subscription-ended";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> NewJobAsync(Tenant tenant, string title) => (await tenant.CreateJobAsync(Samples.RemoteJob(title))).Id();

    private static async Task<ApiResult> TryPublishAsync(Tenant tenant, Guid jobId) => await tenant.PostAsync($"/v1/jobs/{jobId}/publish");

    private static async Task<string> StatusOfAsync(Tenant tenant, Guid jobId) => (await tenant.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK).Text("status");

    [Fact]
    public async Task A_trial_organization_publishes_and_the_page_counts_the_open_job_against_the_growth_limit()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await NewJobAsync(owner, "Data Engineer");

        await owner.PublishJobAsync(job);

        var billing = await owner.BillingAsync();
        billing.Text("state").Should().Be("Trialing");
        billing.At("openJobs").GetInt32().Should().Be(1);
        billing.At("activeJobLimit").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task A_draft_over_the_plans_limit_is_a_402_plan_limit_in_plain_words_and_the_open_job_is_untouched()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var first = await NewJobAsync(owner, "Platform Engineer");
        var second = await NewJobAsync(owner, "Payroll Specialist");
        await owner.PublishJobAsync(first);

        var refused = (await TryPublishAsync(owner, second)).Expect(HttpStatusCode.PaymentRequired);

        refused.Text("type").Should().Be(PlanLimit);
        refused.Text("title").Should().Be("Plan limit reached");
        refused.Text("detail").Should().Contain("allows 1 open job").And.Contain("1 is open").And.Contain("stay open");
        (await StatusOfAsync(owner, first)).Should().Be("Open");
        (await StatusOfAsync(owner, second)).Should().Be("Draft", "a refused publish changes nothing");
        (await owner.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(2);
        var billing = await owner.BillingAsync();
        billing.At("openJobs").GetInt32().Should().Be(1);
        billing.At("activeJobLimit").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Republishing_an_open_job_never_counts_against_the_limit()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var job = await NewJobAsync(owner, "Platform Engineer");
        await owner.PublishJobAsync(job);

        var again = await owner.PublishJobAsync(job);

        again.Text("status").Should().Be("Open");
    }

    [Fact]
    public async Task Closing_a_job_frees_its_place_for_the_next_draft()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var first = await NewJobAsync(owner, "Platform Engineer");
        var second = await NewJobAsync(owner, "Payroll Specialist");
        await owner.PublishJobAsync(first);
        (await TryPublishAsync(owner, second)).Expect(HttpStatusCode.PaymentRequired);

        await owner.CloseJobAsync(first);
        var published = await owner.PublishJobAsync(second);

        published.Text("status").Should().Be("Open");
        (await StatusOfAsync(owner, first)).Should().Be("Closed");
    }

    [Fact]
    public async Task A_recruiter_who_only_sees_their_own_jobs_is_held_to_the_count_of_the_whole_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var ownersJob = await NewJobAsync(owner, "Platform Engineer");
        await owner.PublishJobAsync(ownersJob);
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var theirs = await NewJobAsync(recruiter, "Payroll Specialist");
        (await recruiter.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(1, "the owner's job is hidden from them");

        var refused = (await TryPublishAsync(recruiter, theirs)).Expect(HttpStatusCode.PaymentRequired);

        refused.Text("type").Should().Be(PlanLimit);
        refused.Text("detail").Should().Contain("1 is open", "the hidden job counts");
        (await StatusOfAsync(recruiter, theirs)).Should().Be("Draft");
    }

    [Fact]
    public async Task After_the_subscription_ends_a_draft_cannot_open_but_every_read_works_and_open_jobs_stay_open()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var open = await NewJobAsync(owner, "Platform Engineer");
        var draft = await NewJobAsync(owner, "Payroll Specialist");
        await owner.PublishJobAsync(open);
        var slug = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
        var jobSlug = (await owner.GetAsync($"/v1/jobs/{open}")).Text("slug");
        await fixture.CancelSubscriptionAsync(owner);

        var refused = (await TryPublishAsync(owner, draft)).Expect(HttpStatusCode.PaymentRequired);

        refused.Text("type").Should().Be(SubscriptionEnded);
        refused.Text("detail").Should().Contain("subscription has ended").And.Contain("open jobs stay open");
        (await StatusOfAsync(owner, draft)).Should().Be("Draft");
        (await owner.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(2);
        (await StatusOfAsync(owner, open)).Should().Be("Open");
        (await owner.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK);
        (await owner.BillingAsync()).At("openJobs").GetInt32().Should().Be(1);

        using var candidate = fixture.CreateHttpClient("api");
        var board = await candidate.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}/jobs", Ct);
        board.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("slug").GetString()).Should().Contain(jobSlug, "an open job stays on the careers page");
        using var page = await candidate.GetAsync($"/v1/public/organizations/{slug}/jobs/{jobSlug}", Ct);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        await owner.CloseJobAsync(open);
        (await StatusOfAsync(owner, open)).Should().Be("Closed", "closing is never refused");
    }

    [Fact]
    public async Task Republishing_an_open_job_after_the_subscription_ends_is_still_allowed()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var job = await NewJobAsync(owner, "Platform Engineer");
        await owner.PublishJobAsync(job);
        await fixture.CancelSubscriptionAsync(owner);

        var again = await owner.PublishJobAsync(job);

        again.Text("status").Should().Be("Open");
    }

    [Fact]
    public async Task Subscribing_again_after_the_subscription_ended_lets_a_draft_open()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var draft = await NewJobAsync(owner, "Payroll Specialist");
        await fixture.CancelSubscriptionAsync(owner);
        (await TryPublishAsync(owner, draft)).Expect(HttpStatusCode.PaymentRequired);

        await fixture.SubscribeAsync(owner, "growth");

        (await owner.PublishJobAsync(draft)).Text("status").Should().Be("Open");
    }

    [Fact]
    public async Task The_unlimited_plan_opens_more_jobs_than_starter_allows()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "unlimited");

        foreach (var title in new[] { "Platform Engineer", "Payroll Specialist", "Data Engineer" })
        {
            await owner.PublishJobAsync(await NewJobAsync(owner, title));
        }

        var billing = await owner.BillingAsync();
        billing.At("openJobs").GetInt32().Should().Be(3);
        billing.At("activeJobLimit").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task One_organization_at_its_limit_does_not_hold_another_back()
    {
        using var full = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(full, "starter");
        await full.PublishJobAsync(await NewJobAsync(full, "Platform Engineer"));
        (await TryPublishAsync(full, await NewJobAsync(full, "Payroll Specialist"))).Expect(HttpStatusCode.PaymentRequired);
        await fixture.SubscribeAsync(other, "starter");
        await fixture.CancelSubscriptionAsync(other);
        using var trial = await fixture.SignedInWithOrganizationAsync();

        var published = await trial.PublishJobAsync(await NewJobAsync(trial, "Data Engineer"));

        published.Text("status").Should().Be("Open");
        (await TryPublishAsync(other, await NewJobAsync(other, "Support Lead"))).Expect(HttpStatusCode.PaymentRequired).Text("type").Should().Be(SubscriptionEnded);
        (await full.BillingAsync()).At("openJobs").GetInt32().Should().Be(1);
        (await trial.BillingAsync()).At("openJobs").GetInt32().Should().Be(1);
    }
}
