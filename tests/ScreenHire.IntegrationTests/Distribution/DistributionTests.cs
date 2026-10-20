using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.GoogleIndexing;
using ScreenHire.Domain.Distribution;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Distribution;

/// <summary>
/// One-click publish and teardown across every board, through the running API and worker (docs/architecture/
/// job-distribution.md). The test stack turns on the sandbox board with a short delay (<see cref="AppHostFixture.SandboxDelay"/>)
/// and the local Indexing API fake, so every board runs with no third party.
/// </summary>
public sealed class DistributionTests(AppHostFixture fixture)
{
    private static readonly string[] FeedBoards = ["xml-feed", "careerjet", "talent", "adzuna", "jooble"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publishing_puts_the_job_on_every_board_and_closing_takes_it_down_without_claiming_unconfirmed_removals()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Distribution engineer"))).Id();

        var published = await recruiter.PublishJobAsync(jobId);

        var postings = Postings(published);
        postings.Keys.Should().BeEquivalentTo(["careers", "google", "job-board-api", "sandbox", .. FeedBoards]);
        postings["careers"].Should().Be("Live", "we serve the careers page ourselves");
        postings["sandbox"].Should().Be("Queued", "the worker calls push boards");
        postings["google"].Should().Be("Queued", "Indexing API calls wait for quota");
        foreach (var board in FeedBoards.Append("job-board-api"))
        {
            postings[board].Should().Be("Submitted", $"{board} reads what we serve and never confirms");
        }

        foreach (var board in FeedBoards)
        {
            (await FeedAsync(slug, board)).Should().Contain(jobId.ToString(), $"the {board} feed lists the published job");
        }

        (await recruiter.GetAsync($"/v1/public/boards/{slug}/jobs/{jobId}")).Expect(HttpStatusCode.OK).Text("title").Should().Be("Distribution engineer");
        (await NotificationsAsync(recruiter.OrganizationId)).Should().ContainSingle(n => n.Type == IndexingNotificationType.Updated);

        await WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        (await SandboxListingsAsync(recruiter, slug)).Should().ContainSingle(l => l == jobId);

        var closeRequestedAt = DateTimeOffset.UtcNow;
        var closed = await recruiter.CloseJobAsync(jobId);

        var afterClose = Postings(closed);
        afterClose["careers"].Should().Be("Closed", "the careers page serves 410 at once");
        afterClose["sandbox"].Should().Be("Closing", "the sandbox has not confirmed the removal yet");
        afterClose["google"].Should().Be("Closing", "Google never confirms a removal");
        foreach (var board in FeedBoards.Append("job-board-api"))
        {
            afterClose[board].Should().Be("Closing", $"{board} only drops the job on its next refresh");
        }

        var sandboxExpectedBy = PostingOf(closed, "sandbox").GetProperty("expectedBy").GetDateTimeOffset();
        sandboxExpectedBy.Should().BeAfter(closeRequestedAt, "the response says when the sandbox should be done");

        var gone = await recruiter.GetAsync($"/v1/public/organizations/{slug}/jobs/{published.Text("slug")}");
        gone.Status.Should().Be(HttpStatusCode.Gone);
        gone.At("validThrough").GetDateTimeOffset().Should().BeOnOrBefore(DateTimeOffset.UtcNow, "Google reads a past validThrough as expired");
        foreach (var board in FeedBoards)
        {
            (await FeedAsync(slug, board)).Should().NotContain(jobId.ToString(), $"the {board} feed drops a closed job at once");
        }

        (await recruiter.GetAsync($"/v1/public/boards/{slug}/jobs/{jobId}")).Status.Should().Be(HttpStatusCode.NotFound);
        var notifications = await NotificationsAsync(recruiter.OrganizationId);
        notifications.Should().ContainSingle(n => n.Type == IndexingNotificationType.Deleted && n.SupersededAt == null, "URL_DELETED is queued");
        notifications.Where(n => n.Type == IndexingNotificationType.Updated)
            .Should().OnlyContain(n => n.SentAt != null || n.SupersededAt != null, "an update still waiting is never sent after the delete");

        var sandbox = await WaitForPostingAsync(recruiter, jobId, "sandbox", "Closed");
        var confirmedAt = sandbox.GetProperty("closedAt").GetDateTimeOffset();
        (confirmedAt - closeRequestedAt).Should().BeGreaterThanOrEqualTo(AppHostFixture.SandboxDelay, "the sandbox confirms only after its removal delay");
        (await SandboxListingsAsync(recruiter, slug)).Should().NotContain(jobId);
        Postings((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK))["talent"]
            .Should().Be("Closing", "nothing moves a board that never confirms to Closed");
    }

    [Fact]
    public async Task A_board_with_issues_keeps_the_posting_in_draft_and_out_of_its_feed()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var job = Samples.RemoteJob("Short description");
        job["description"] = "<p>Short.</p>";
        var jobId = (await recruiter.CreateJobAsync(job)).Id();

        var published = await recruiter.PublishJobAsync(jobId);

        var adzuna = PostingOf(published, "adzuna");
        adzuna.GetProperty("state").GetString().Should().Be("Draft");
        adzuna.GetProperty("issues").EnumerateArray().Select(i => i.GetString()).Should().ContainSingle(i => i!.Contains("100 characters"));
        PostingOf(published, "talent").GetProperty("state").GetString().Should().Be("Draft");
        PostingOf(published, "careerjet").GetProperty("state").GetString().Should().Be("Submitted");
        (await FeedAsync(slug, "adzuna")).Should().NotContain(jobId.ToString());
        (await FeedAsync(slug, "careerjet")).Should().Contain(jobId.ToString());
    }

    [Fact]
    public async Task One_board_can_be_taken_down_while_the_job_stays_open_but_not_the_careers_page()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Per-board teardown"))).Id();
        await recruiter.PublishJobAsync(jobId);

        var careerjet = (await recruiter.PostAsync($"/v1/jobs/{jobId}/postings/careerjet/close")).Expect(HttpStatusCode.OK);

        careerjet.Text("state").Should().Be("Closing");
        (await FeedAsync(slug, "careerjet")).Should().NotContain(jobId.ToString());
        (await FeedAsync(slug, "jooble")).Should().Contain(jobId.ToString(), "only Careerjet was taken down");
        (await recruiter.GetAsync($"/v1/jobs/{jobId}")).Text("status").Should().Be("Open");
        (await recruiter.PostAsync($"/v1/jobs/{jobId}/postings/careers/close")).Status.Should().Be(HttpStatusCode.Conflict);

        // Republishing updates the boards the job is on and leaves the one taken down alone; naming it posts it again.
        Postings(await recruiter.PublishJobAsync(jobId))["careerjet"].Should().Be("Closing");
        var reposted = (await recruiter.PostAsync($"/v1/jobs/{jobId}/publish", new JsonObject { ["boards"] = new JsonArray("careerjet") }))
            .Expect(HttpStatusCode.OK);
        Postings(reposted)["careerjet"].Should().Be("Submitted");
        (await FeedAsync(slug, "careerjet")).Should().Contain(jobId.ToString());
    }

    [Fact]
    public async Task Publishing_changed_content_sends_the_new_version_to_every_board()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var body = PublishableJob("Versioned role");
        var jobId = (await recruiter.CreateJobAsync(body)).Id();
        await recruiter.PublishJobAsync(jobId);
        // The worker updates the postings as boards answer the first publish; republishing while it does is a 409 by
        // design ("refresh and try again"), so let the first publish settle before testing the second.
        await WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        body["title"] = "Versioned role, renamed";
        (await recruiter.PutAsync($"/v1/jobs/{jobId}", body)).Expect(HttpStatusCode.OK);

        (await FeedAsync(slug, "xml-feed")).Should().Contain("<![CDATA[Versioned role]]>", "feeds show the published version, not the live edit");
        var republished = await recruiter.PublishJobAsync(jobId);

        republished.At("postings").EnumerateArray().Select(p => p.GetProperty("jobVersion").GetInt32()).Should().AllBeEquivalentTo(2);
        (await FeedAsync(slug, "xml-feed")).Should().Contain("<![CDATA[Versioned role, renamed]]>");
        await WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
    }

    [Fact]
    public async Task Feeds_carry_cache_headers_and_answer_an_unchanged_etag_with_304()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        await recruiter.PublishJobAsync((await recruiter.CreateJobAsync(PublishableJob("Cached role"))).Id());
        using var client = fixture.CreateHttpClient("api");
        var path = $"/v1/public/organizations/{slug}/feeds/careerjet.xml";

        using var first = await client.GetAsync(path, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        first.Headers.CacheControl!.Public.Should().BeTrue();
        first.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        first.Content.Headers.LastModified.Should().NotBeNull();
        var etag = first.Headers.ETag!;
        XDocument.Parse(await first.Content.ReadAsStringAsync(Ct)).Root!.Name.LocalName.Should().Be("jobs");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(etag);
        using var second = await client.SendAsync(request, Ct);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Feeds_postings_and_the_job_board_api_never_show_another_organizations_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var otherSlug = await SlugOfAsync(other.OrganizationId);
        var jobId = (await owner.CreateJobAsync(PublishableJob("Owner only"))).Id();
        await owner.PublishJobAsync(jobId);

        (await other.GetAsync($"/v1/jobs/{jobId}/postings")).Status.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/jobs/{jobId}/postings/careerjet/close")).Status.Should().Be(HttpStatusCode.NotFound);
        (await FeedAsync(otherSlug, "careerjet")).Should().NotContain(jobId.ToString());
        (await other.GetAsync($"/v1/public/boards/{otherSlug}/jobs/{jobId}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/jobs/{jobId}/postings")).Expect(HttpStatusCode.OK).Count().Should().Be(9);
    }

    [Fact]
    public async Task Boards_lists_only_configured_boards_and_publishing_to_an_unknown_one_is_refused()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Board list"))).Id();

        var boards = (await recruiter.GetAsync("/v1/boards")).Expect(HttpStatusCode.OK);

        boards.Body.EnumerateArray().Select(b => b.GetProperty("id").GetString()).Should()
            .BeEquivalentTo(["careers", "google", "job-board-api", "sandbox", .. FeedBoards]);
        var careerjet = boards.Body.EnumerateArray().Single(b => b.GetProperty("id").GetString() == "careerjet");
        careerjet.GetProperty("confirmsRemoval").GetBoolean().Should().BeFalse();
        careerjet.GetProperty("removalEtaSeconds").GetInt32().Should().Be(86_400);
        var refused = await recruiter.PostAsync($"/v1/jobs/{jobId}/publish", new JsonObject { ["boards"] = new JsonArray("indeed") });
        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("boards");
        (await recruiter.GetAsync($"/v1/jobs/{jobId}")).Text("status").Should().Be("Draft", "a refused publish changes nothing");
    }

    [Fact]
    public async Task Publish_and_close_raise_posting_events_and_one_sandbox_command_each()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Commands"))).Id();
        var published = await recruiter.PublishJobAsync(jobId);
        var sandboxId = PostingOf(published, "sandbox").GetProperty("id").GetString()!;
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);

        await WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        await recruiter.PublishJobAsync(jobId);

        (await store.CountIncomingAsync(sandboxId, Ct, ".SyncJobPosting")).Should().Be(1, "republishing unchanged content sends nothing");
        await recruiter.CloseJobAsync(jobId);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(sandboxId, Ct, ".SyncJobPosting") == 2, "the close to reach the sandbox", Ct);
        (await store.GetDeadLettersAsync(sandboxId, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_indexing_dispatcher_sends_queued_notifications_and_google_never_reaches_closed()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Indexing"))).Id();
        await recruiter.PublishJobAsync(jobId);
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();

        // The worker's cron sends the same message every minute; sending it here runs the dispatcher now. Twice, so the
        // notification is handed out twice, as when a send has not run before the next minute: it is sent once, and
        // the second send finds it sent instead of failing.
        await bus.PublishAsync(new DispatchIndexingNotifications());
        await bus.PublishAsync(new DispatchIndexingNotifications());
        var google = await WaitForPostingAsync(recruiter, jobId, "google", "Submitted");
        (await NotificationsAsync(recruiter.OrganizationId)).Should().ContainSingle(n => n.Type == IndexingNotificationType.Updated && n.SentAt != null);
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        (await store.GetDeadLettersAsync(recruiter.OrganizationId.ToString(), Ct)).Should().BeEmpty();

        google.GetProperty("confirmsLive").GetBoolean().Should().BeFalse("Google accepted URL_UPDATED but reports nothing about indexing");
        (await recruiter.CloseJobAsync(jobId)).At("postings").EnumerateArray()
            .Single(p => p.GetProperty("boardId").GetString() == "google").GetProperty("state").GetString().Should().Be("Closing");
        await bus.PublishAsync(new DispatchIndexingNotifications());
        await WolverineStore.WaitUntilAsync(
            async () => (await NotificationsAsync(recruiter.OrganizationId)).Any(n => n.Type == IndexingNotificationType.Deleted && n.SentAt != null),
            "URL_DELETED to be sent", Ct);
        Postings((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK))["google"]
            .Should().Be("Closing", "an accepted URL_DELETED is a request, not a confirmed removal");
    }

    // [Deduplicated] on SyncJobPosting: the worker records a claim for each command's idempotency key, and a duplicate of a
    // command already handled changes nothing. The posting's own state checks would also stop this duplicate, so the
    // claim is what shows the attribute is wired.
    [Fact]
    public async Task A_duplicate_board_command_is_claimed_once_and_changes_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Duplicate command"))).Id();
        var postingId = PostingOf(await recruiter.PublishJobAsync(jobId), "sandbox").GetProperty("id").GetGuid();
        await WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        var original = new SyncJobPosting(recruiter.OrganizationId, postingId, jobId, "sandbox", 1, PostingAction.Publish, Sequence: 1);

        (await CountClaimsAsync(original.IdempotencyKey)).Should().Be(1, "the worker claimed the command's key");

        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await using (var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(original);
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountQueuedAsync("worker", postingId.ToString(), Ct, ".SyncJobPosting") == 0
                    && await store.CountIncomingAsync(postingId.ToString(), Ct, ".SyncJobPosting") == 2,
                "the duplicate to reach the worker", Ct);
        }

        (await CountClaimsAsync(original.IdempotencyKey)).Should().Be(1);
        (await store.GetDeadLettersAsync(postingId.ToString(), Ct)).Should().BeEmpty();
        var sandbox = PostingOf((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK), "sandbox");
        sandbox.GetProperty("state").GetString().Should().Be("Live");
        (await SandboxListingsAsync(recruiter, await SlugOfAsync(recruiter.OrganizationId))).Should().ContainSingle(l => l == jobId);
    }

    private async Task<long> CountClaimsAsync(string key)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select count(*) from wolverine.wolverine_deduplication_hashed where deduplication_id = @key", connection);
        command.Parameters.AddWithValue("key", key);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>A remote job whose description clears every board's minimum length.</summary>
    private static JsonObject PublishableJob(string title)
    {
        var job = Samples.RemoteJob(title);
        job["description"] = "<p>Build and run the systems our customers rely on every day, with a small team that owns its work from design to production.</p>";
        return job;
    }

    private static Dictionary<string, string> Postings(ApiResult job) =>
        job.At("postings").EnumerateArray().ToDictionary(p => p.GetProperty("boardId").GetString()!, p => p.GetProperty("state").GetString()!);

    private static System.Text.Json.JsonElement PostingOf(ApiResult job, string boardId) =>
        job.At("postings").EnumerateArray().Single(p => p.GetProperty("boardId").GetString() == boardId);

    private static async Task<System.Text.Json.JsonElement> WaitForPostingAsync(Tenant recruiter, Guid jobId, string boardId, string state)
    {
        System.Text.Json.JsonElement posting = default;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            var postings = (await recruiter.GetAsync($"/v1/jobs/{jobId}/postings")).Expect(HttpStatusCode.OK);
            posting = postings.Body.EnumerateArray().Single(p => p.GetProperty("boardId").GetString() == boardId);
            return posting.GetProperty("state").GetString() == state;
        }, $"the {boardId} posting to be {state}", Ct);
        return posting;
    }

    private async Task<string> FeedAsync(string slug, string boardId)
    {
        using var client = fixture.CreateHttpClient("api");
        using var response = await client.GetAsync($"/v1/public/organizations/{slug}/feeds/{boardId}.xml", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<List<Guid>> SandboxListingsAsync(Tenant recruiter, string slug) =>
        [.. (await recruiter.GetAsync($"/v1/public/sandbox-board/{slug}/listings")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Select(l => l.GetProperty("jobId").GetGuid())];

    private async Task<List<IndexingNotification>> NotificationsAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        return await db.IndexingNotifications.AsNoTracking().OrderBy(n => n.EnqueuedAt).ToListAsync(Ct);
    }

    private async Task<string> SlugOfAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        return await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Slug).SingleAsync(Ct);
    }
}
