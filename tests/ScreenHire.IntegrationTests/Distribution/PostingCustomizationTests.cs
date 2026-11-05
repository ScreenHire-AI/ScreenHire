using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Distribution;

/// <summary>
/// A board's own title and description (docs/architecture/job-distribution.md, "Per-board customization"), through the
/// running API: saving sends the text to that board only, a failing board check saves nothing, a board that cannot take
/// text of its own refuses, the job's later edits show beside the board's text, and the change history and the live
/// event follow each save.
/// </summary>
public sealed class PostingCustomizationTests(AppHostFixture fixture)
{
    private const string AdzunaPhrase = "Written only for readers on the Adzuna listing";

    private const string AdzunaDescription =
        $"<p>{AdzunaPhrase}: you will build and run the systems our customers rely on, with a small team that owns its work from design to production.</p>";

    private const string CareerjetPhrase = "Written only for readers on the Careerjet listing";

    private const string CareerjetDescription = $"<p>{CareerjetPhrase}, where the team is described in a few plain words.</p>";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customizing_a_board_that_refused_the_job_text_sends_it_there_and_changes_nothing_else()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = await CreateShortJobAsync(recruiter);
        var published = await recruiter.PublishJobAsync(jobId);
        var jobSlug = published.Text("slug");
        PostingOf(published, "adzuna").GetProperty("state").GetString().Should().Be("Draft");
        PostingOf(published, "adzuna").GetProperty("issues").EnumerateArray().Should().ContainSingle(i => i.GetString()!.Contains("100 characters"));
        (await FeedAsync(slug, "adzuna")).Should().NotContain(jobId.ToString());
        var careerjetBefore = await FeedAsync(slug, "careerjet");

        var saved = (await recruiter.PutAsync(Path(jobId, "adzuna"), Request(null, AdzunaDescription))).Expect(HttpStatusCode.OK);

        saved.Text("boardId").Should().Be("adzuna");
        saved.Text("state").Should().Be("Submitted", "Adzuna reads a feed we serve, and its check now passes");
        saved.At("issues").GetArrayLength().Should().Be(0);
        saved.At("jobVersion").GetInt32().Should().Be(1, "no job version is made");
        saved.At("customizedDescription").GetBoolean().Should().BeTrue();
        saved.At("customizedTitle").GetBoolean().Should().BeFalse();
        saved.At("jobChangedSinceCustomized").GetBoolean().Should().BeFalse();
        var adzuna = await FeedAsync(slug, "adzuna");
        adzuna.Should().Contain(jobId.ToString()).And.Contain(AdzunaPhrase, "the feed shows the board's own description");
        adzuna.Should().NotContain("<p>Short.</p>");

        (await FeedAsync(slug, "careerjet")).Should().Be(careerjetBefore, "another board's feed is untouched");
        (await FeedAsync(slug, "careerjet")).Should().NotContain(AdzunaPhrase);
        (await recruiter.GetAsync($"/v1/public/boards/{slug}/jobs/{jobId}")).Expect(HttpStatusCode.OK).Body.GetRawText()
            .Should().NotContain(AdzunaPhrase, "the public job-board API shows the job");
        (await recruiter.GetAsync($"/v1/public/organizations/{slug}/jobs/{jobSlug}")).Expect(HttpStatusCode.OK).Body.GetRawText()
            .Should().NotContain(AdzunaPhrase, "the careers page shows the job");

        var job = (await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        job.Text("description").Should().Be("<p>Short.</p>", "the job's own description is unchanged");
        job.At("publishedVersion").GetInt32().Should().Be(1);
        job.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse();
        (await recruiter.GetAsync($"/v1/jobs/{jobId}/versions")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        PostingOf(job, "adzuna").GetProperty("customizedDescription").GetBoolean().Should().BeTrue();
        PostingOf(job, "careerjet").GetProperty("customizedDescription").GetBoolean().Should().BeFalse();

        var customization = (await recruiter.GetAsync(Path(jobId, "adzuna"))).Expect(HttpStatusCode.OK);
        customization.Text("boardName").Should().Be("Adzuna");
        customization.Text("description.override").Should().Contain(AdzunaPhrase);
        customization.Text("description.job").Should().Be("<p>Short.</p>");
        customization.At("title.override").ValueKind.Should().Be(JsonValueKind.Null);
        customization.Text("title.job").Should().Be("Short description");
        customization.Count("changes").Should().Be(0);
    }

    [Fact]
    public async Task A_description_the_board_still_refuses_is_a_400_that_saves_and_sends_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = await CreateShortJobAsync(recruiter);
        await recruiter.PublishJobAsync(jobId);

        var refused = (await recruiter.PutAsync(Path(jobId, "adzuna"), Request("A better title", "<p>Still too short.</p>")))
            .Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Contain("board");
        refused.At("errors.board").EnumerateArray().Should().ContainSingle(e => e.GetString()!.Contains("100 characters"));
        var postings = (await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        PostingOf(postings, "adzuna").GetProperty("state").GetString().Should().Be("Draft");
        PostingOf(postings, "adzuna").GetProperty("customizedTitle").GetBoolean().Should().BeFalse("the title was not saved either");
        var customization = (await recruiter.GetAsync(Path(jobId, "adzuna"))).Expect(HttpStatusCode.OK);
        customization.At("description.override").ValueKind.Should().Be(JsonValueKind.Null);
        customization.At("title.override").ValueKind.Should().Be(JsonValueKind.Null);
        (await FeedAsync(slug, "adzuna")).Should().NotContain(jobId.ToString());
    }

    [Fact]
    public async Task The_preview_shows_what_a_board_would_receive_and_what_its_check_says_without_saving()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = await CreateShortJobAsync(recruiter);
        await recruiter.PublishJobAsync(jobId);

        var failing = (await recruiter.PostAsync($"{Path(jobId, "adzuna")}/preview", Request("Preview title", "<p>Still too short.</p>")))
            .Expect(HttpStatusCode.OK);
        var passing = (await recruiter.PostAsync($"{Path(jobId, "adzuna")}/preview", Request(null, AdzunaDescription))).Expect(HttpStatusCode.OK);

        failing.Text("title").Should().Be("Preview title");
        failing.Text("description").Should().Contain("Still too short.");
        failing.At("issues").EnumerateArray().Should().ContainSingle(i => i.GetString()!.Contains("100 characters"));
        passing.Text("title").Should().Be("Short description", "a field left out follows the job");
        passing.Text("description").Should().Contain(AdzunaPhrase);
        passing.At("issues").GetArrayLength().Should().Be(0);
        var customization = (await recruiter.GetAsync(Path(jobId, "adzuna"))).Expect(HttpStatusCode.OK);
        customization.At("description.override").ValueKind.Should().Be(JsonValueKind.Null, "a preview saves nothing");
        PostingOf((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK), "adzuna")
            .GetProperty("state").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task Going_back_to_the_jobs_text_is_checked_by_the_board_like_any_other_save()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = await CreateShortJobAsync(recruiter);
        await recruiter.PublishJobAsync(jobId);
        (await recruiter.PutAsync(Path(jobId, "adzuna"), Request(null, AdzunaDescription))).Expect(HttpStatusCode.OK);

        var refused = (await recruiter.PutAsync(Path(jobId, "adzuna"), Request(null, null))).Expect(HttpStatusCode.BadRequest);

        refused.At("errors.board").EnumerateArray().Should().ContainSingle(e => e.GetString()!.Contains("100 characters"), "the job's own text is too short for Adzuna");
        (await recruiter.GetAsync(Path(jobId, "adzuna"))).Expect(HttpStatusCode.OK).Text("description.override").Should().Contain(AdzunaPhrase);
        (await FeedAsync(slug, "adzuna")).Should().Contain(AdzunaPhrase, "the board keeps showing what it showed");
    }

    [Fact]
    public async Task A_board_can_have_its_own_title_and_description_and_go_back_to_the_jobs()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);
        var untouched = (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK);
        untouched.At("title.override").ValueKind.Should().Be(JsonValueKind.Null);
        untouched.At("description.override").ValueKind.Should().Be(JsonValueKind.Null);
        untouched.Text("title.job").Should().Be("Platform engineer");
        untouched.Text("description.job").Should().Be(PublishableDescription);

        var saved = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("Platform engineer (remote)", CareerjetDescription))).Expect(HttpStatusCode.OK);

        saved.At("customizedTitle").GetBoolean().Should().BeTrue();
        saved.At("customizedDescription").GetBoolean().Should().BeTrue();
        var careerjet = await FeedAsync(slug, "careerjet");
        careerjet.Should().Contain("Platform engineer (remote)").And.Contain(CareerjetPhrase);
        careerjet.Should().NotContain(PublishableText);
        var other = await FeedAsync(slug, "xml-feed");
        other.Should().Contain("<![CDATA[Platform engineer]]>").And.Contain(PublishableText).And.NotContain(CareerjetPhrase);

        var cleared = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(null, null))).Expect(HttpStatusCode.OK);

        cleared.At("customizedTitle").GetBoolean().Should().BeFalse();
        cleared.At("customizedDescription").GetBoolean().Should().BeFalse();
        var back = await FeedAsync(slug, "careerjet");
        back.Should().Contain(PublishableText).And.NotContain(CareerjetPhrase).And.NotContain("Platform engineer (remote)");
        (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK).At("title.override").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Text_that_reads_as_the_jobs_own_or_is_blank_follows_the_job()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);

        var same = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("Platform engineer", PublishableDescription))).Expect(HttpStatusCode.OK);
        var blank = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("   ", "  "))).Expect(HttpStatusCode.OK);

        same.At("customizedTitle").GetBoolean().Should().BeFalse("it is the job's own title");
        same.At("customizedDescription").GetBoolean().Should().BeFalse("it is the job's own description");
        blank.At("customizedTitle").GetBoolean().Should().BeFalse();
        blank.At("customizedDescription").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_title_longer_than_200_characters_is_refused()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);

        var refused = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(new string('t', 201), null))).Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Contain("title");
    }

    [Fact]
    public async Task The_careers_page_and_google_show_the_job_as_it_is_and_refuse_text_of_their_own()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);
        var boards = (await recruiter.GetAsync("/v1/boards")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("canCustomize").GetBoolean());
        boards["careers"].Should().BeFalse();
        boards["google"].Should().BeFalse();
        boards.Where(b => b.Key is not ("careers" or "google")).Should().OnlyContain(b => b.Value, "every other board takes text of its own");

        foreach (var board in new[] { "careers", "google" })
        {
            (await recruiter.GetAsync(Path(jobId, board))).Expect(HttpStatusCode.Conflict);
            (await recruiter.PutAsync(Path(jobId, board), Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Conflict);
            (await recruiter.PostAsync($"{Path(jobId, board)}/preview", Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Conflict);
        }

        var postings = (await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        PostingOf(postings, "careers").GetProperty("customizedTitle").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_job_that_is_not_open_cannot_be_customized()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var draft = (await recruiter.CreateJobAsync(PublishableJob("Never published"))).Id();
        var closed = (await recruiter.CreateJobAsync(PublishableJob("Closed again"))).Id();
        await recruiter.PublishJobAsync(closed);
        await recruiter.CloseJobAsync(closed);

        foreach (var jobId in new[] { draft, closed })
        {
            (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.Conflict);
            (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Conflict);
            (await recruiter.PostAsync($"{Path(jobId, "careerjet")}/preview", Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Conflict);
        }

        (await recruiter.GetAsync($"/v1/jobs/{draft}/postings")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "nothing was created for the draft");
    }

    [Fact]
    public async Task A_board_the_job_was_taken_down_from_stays_down_when_its_text_is_saved()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);
        (await recruiter.PostAsync($"/v1/jobs/{jobId}/postings/careerjet/close")).Expect(HttpStatusCode.OK);

        var refused = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.Conflict);

        refused.Text("detail").Should().Contain("Post it again first");
        PostingOf((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK), "careerjet").GetProperty("state").GetString()
            .Should().BeOneOf("Closing", "Closed");
        (await FeedAsync(slug, "careerjet")).Should().NotContain(jobId.ToString());
        (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK).At("description.override").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unknown_job_or_board_is_a_404()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);

        foreach (var path in new[] { Path(jobId, "no-such-board"), Path(jobId, "indeed"), Path(Guid.NewGuid(), "careerjet") })
        {
            (await recruiter.GetAsync(path)).Expect(HttpStatusCode.NotFound);
            (await recruiter.PutAsync(path, Request("A title", CareerjetDescription))).Expect(HttpStatusCode.NotFound);
            (await recruiter.PostAsync($"{path}/preview", Request("A title", CareerjetDescription))).Expect(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Another_organizations_job_is_a_404_and_stays_as_it_was()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(owner.OrganizationId);
        var jobId = (await owner.CreateJobAsync(PublishableJob("Owner only"))).Id();
        await owner.PublishJobAsync(jobId);

        (await other.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.NotFound);
        (await other.PutAsync(Path(jobId, "careerjet"), Request("Theirs", CareerjetDescription))).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"{Path(jobId, "careerjet")}/preview", Request("Theirs", CareerjetDescription))).Expect(HttpStatusCode.NotFound);

        (await FeedAsync(slug, "careerjet")).Should().NotContain(CareerjetPhrase);
        (await owner.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK).At("title.override").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_member_who_cannot_edit_jobs_can_read_a_customization_but_not_preview_or_save_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var slug = await SlugOfAsync(owner.OrganizationId);
        var jobId = (await owner.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await owner.PublishJobAsync(jobId);

        (await viewer.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK);
        (await viewer.PutAsync(Path(jobId, "careerjet"), Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{Path(jobId, "careerjet")}/preview", Request("A title", CareerjetDescription))).Expect(HttpStatusCode.Forbidden);

        (await FeedAsync(slug, "careerjet")).Should().NotContain(CareerjetPhrase);
        (await owner.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_board_keeps_its_own_text_when_the_job_is_edited_and_published_again_and_the_job_change_is_shown_beside_it()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugOfAsync(recruiter.OrganizationId);
        var body = PublishableJob("Platform engineer");
        var jobId = (await recruiter.CreateJobAsync(body)).Id();
        await recruiter.PublishJobAsync(jobId);
        (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("Platform engineer (remote)", CareerjetDescription))).Expect(HttpStatusCode.OK);

        const string editedText = "Rewritten for the whole team after the reorganization, still long enough for every board to accept it as it stands.";
        body["description"] = $"<p>{editedText}</p>";
        (await recruiter.PutAsync($"/v1/jobs/{jobId}", body)).Expect(HttpStatusCode.OK);

        var beforePublish = (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK);
        beforePublish.Count("changes").Should().Be(0, "an edit nobody published is not what boards show");
        PostingOf((await recruiter.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK), "careerjet")
            .GetProperty("jobChangedSinceCustomized").GetBoolean().Should().BeFalse();

        var republished = await recruiter.PublishJobAsync(jobId);

        republished.At("publishedVersion").GetInt32().Should().Be(2);
        var careerjet = PostingOf(republished, "careerjet");
        careerjet.GetProperty("jobVersion").GetInt32().Should().Be(2);
        careerjet.GetProperty("jobChangedSinceCustomized").GetBoolean().Should().BeTrue("the job's description changed under the board's own");
        careerjet.GetProperty("customizedDescription").GetBoolean().Should().BeTrue();
        PostingOf(republished, "xml-feed").GetProperty("jobChangedSinceCustomized").GetBoolean().Should().BeFalse("a board that follows the job has nothing to compare");
        var careerjetFeed = await FeedAsync(slug, "careerjet");
        careerjetFeed.Should().Contain("Platform engineer (remote)").And.Contain(CareerjetPhrase).And.NotContain(editedText);
        (await FeedAsync(slug, "xml-feed")).Should().Contain(editedText, "the other boards follow the job to its new version");

        var customization = (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK);
        customization.Text("description.job").Should().Contain(editedText);
        customization.Text("description.override").Should().Contain(CareerjetPhrase);
        customization.Count("changes").Should().Be(1, "only the description changed; the title is the job's, unchanged");
        customization.Text("changes.0.field").Should().Be("Description");
        customization.Text("changes.0.before").Should().Contain("Build and run the systems");
        customization.Text("changes.0.after").Should().Contain(editedText);

        var resaved = (await recruiter.PutAsync(Path(jobId, "careerjet"), Request("Platform engineer (remote)", CareerjetDescription))).Expect(HttpStatusCode.OK);

        resaved.At("jobChangedSinceCustomized").GetBoolean().Should().BeFalse("saving again accepts the job's new text");
        (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK).Count("changes").Should().Be(0);
    }

    [Fact]
    public async Task A_customization_is_recorded_in_the_jobs_history_and_a_boards_own_state_changes_are_not()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        await recruiter.PublishJobAsync(jobId);
        var board = (await recruiter.GetAsync(Path(jobId, "careerjet"))).Expect(HttpStatusCode.OK).Text("boardName");
        var started = (await History(recruiter, jobId)).Count("entries");

        (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.OK);

        var customized = await History(recruiter, jobId);
        customized.Count("entries").Should().Be(started + 1);
        customized.Text("entries.0.by").Should().Be("Member");
        customized.Id("entries.0.userId").Should().Be(recruiter.UserId);
        customized.Count("entries.0.lines").Should().Be(1);
        customized.Text("entries.0.lines.0.text").Should().Be($"customized the description for {board}");
        customized.Text("entries.0.lines.0.details.0.label").Should().Be("Description");
        customized.At("entries.0.lines.0.details.0.before").ValueKind.Should().Be(JsonValueKind.Null);
        customized.Text("entries.0.lines.0.details.0.after").Should().Contain(CareerjetPhrase).And.NotContain("<p>", "the history shows the words, not the markup");

        // The same text again moves the posting through its states (queued, then submitted) but changes nothing it shows.
        (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.OK);
        // The worker's answers for the sandbox board are state changes of a posting too.
        await DistributionTests.WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        var churned = await History(recruiter, jobId);
        churned.Count("entries").Should().Be(started + 1, "a board's state changes make no history line");

        (await recruiter.PutAsync(Path(jobId, "careerjet"), Request(null, null))).Expect(HttpStatusCode.OK);

        var cleared = await History(recruiter, jobId);
        cleared.Count("entries").Should().Be(started + 2);
        cleared.Text("entries.0.lines.0.text").Should().Be($"went back to the job's description for {board}");
        cleared.Text("entries.0.lines.0.details.0.before").Should().Contain(CareerjetPhrase);
        cleared.At("entries.0.lines.0.details.0.after").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Every_save_tells_the_organizations_open_dashboards_and_no_other()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);
        var jobId = (await ours.CreateJobAsync(PublishableJob("Platform engineer"))).Id();
        // Careerjet is a feed board, so no worker answer raises an event of its own for this job.
        (await ours.PostAsync($"/v1/jobs/{jobId}/publish", new JsonObject { ["boards"] = new JsonArray("careerjet") })).Expect(HttpStatusCode.OK);
        var published = DateTimeOffset.UtcNow;
        await WaitForPostingsChangedAsync(ourClient, jobId, after: DateTimeOffset.MinValue);

        (await ours.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.OK);
        var saved = await WaitForPostingsChangedAsync(ourClient, jobId, after: published);
        saved.GetProperty("organizationId").GetGuid().Should().Be(ours.OrganizationId);

        // A save that changes nothing the board shows still tells the other open dashboards to refetch.
        var resavedAfter = DateTimeOffset.UtcNow;
        (await ours.PutAsync(Path(jobId, "careerjet"), Request(null, CareerjetDescription))).Expect(HttpStatusCode.OK);
        await WaitForPostingsChangedAsync(ourClient, jobId, after: resavedAfter);

        theirClient.ReceivedAnythingAbout(jobId).Should().BeFalse("another organization's job events never reach us");
    }

    private const string PublishableText = "Build and run the systems our customers rely on every day, with a small team that owns its work from design to production.";

    private const string PublishableDescription = $"<p>{PublishableText}</p>";

    private static string Path(Guid jobId, string boardId) => $"/v1/jobs/{jobId}/postings/{boardId}/customization";

    private static JsonObject Request(string? title, string? description) => new() { ["title"] = title, ["description"] = description };

    /// <summary>A remote job whose description clears every board's minimum length.</summary>
    private static JsonObject PublishableJob(string title)
    {
        var job = Samples.RemoteJob(title);
        job["description"] = PublishableDescription;
        return job;
    }

    private static async Task<Guid> CreateShortJobAsync(Tenant recruiter)
    {
        var job = Samples.RemoteJob("Short description");
        job["description"] = "<p>Short.</p>";
        return (await recruiter.CreateJobAsync(job)).Id();
    }

    private static async Task<ApiResult> History(Tenant recruiter, Guid jobId) =>
        (await recruiter.GetAsync($"/v1/jobs/{jobId}/history")).Expect(HttpStatusCode.OK);

    private static JsonElement PostingOf(ApiResult job, string boardId) =>
        job.At("postings").EnumerateArray().Single(p => p.GetProperty("boardId").GetString() == boardId);

    private static async Task<JsonElement> WaitForPostingsChangedAsync(EventsConnection client, Guid jobId, DateTimeOffset after)
    {
        JsonElement found = default;
        await WolverineStore.WaitUntilAsync(() =>
        {
            foreach (var received in client.Received)
            {
                if (received.Type == "JobPostingsChanged"
                    && received.Data.GetProperty("jobId").GetGuid() == jobId
                    && received.Data.GetProperty("changedAt").GetDateTimeOffset() > after)
                {
                    found = received.Data;
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }, $"a JobPostingsChanged event for {jobId} after {after:O}", Ct);
        return found;
    }

    private async Task<string> FeedAsync(string slug, string boardId)
    {
        using var client = fixture.CreateHttpClient("api");
        using var response = await client.GetAsync($"/v1/public/organizations/{slug}/feeds/{boardId}.xml", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<string> SlugOfAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        return await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Slug).SingleAsync(Ct);
    }
}
