using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// Who made a change, on the wire: the events behind "teammates' changes as toasts" carry <c>actorUserId</c> (the member
/// who did it, null for an API key, an automation or a candidate), task events add <c>assigneeUserId</c>, and a published
/// hiring template version adds <c>previousEditorUserId</c>, the newest other member to have changed the template.
/// The dashboard filters on these ids; they reach only their own organization's dashboards.
/// </summary>
public sealed class TeammateChangesRealtimeTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_members_move_note_tag_scorecard_and_tasks_carry_their_id_and_a_task_its_assignee_and_never_reach_another_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ownerClient = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);

        // The recruiter adds the candidate, then works the application; the owner watches.
        var application = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        ActorOf(await ownerClient.WaitForAsync("ApplicationSubmitted", "applicationId", application, Ct))
            .Should().Be(recruiter.UserId, "a recruiter adding a candidate is the actor of the new application");

        (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        ActorOf(await ownerClient.WaitForAsync("ApplicationMoved", "applicationId", application, Ct)).Should().Be(recruiter.UserId);

        var note = (await recruiter.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "Strong portfolio." }))
            .Expect(HttpStatusCode.Created);
        ActorOf(await ownerClient.WaitForAsync("ApplicationNoteAdded", "eventId", note.Id(), Ct)).Should().Be(recruiter.UserId);

        (await recruiter.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "referral" })).Expect(HttpStatusCode.OK);
        ActorOf(await ownerClient.WaitForAsync("ApplicationTagsChanged", "applicationId", application, Ct)).Should().Be(recruiter.UserId);

        var scorecard = (await recruiter.PostAsync($"/v1/applications/{application}/scorecards", new JsonObject
        {
            ["stageId"] = "interview", ["answers"] = new JsonArray(), ["recommendation"] = "Yes",
        })).Expect(HttpStatusCode.Created);
        ActorOf(await ownerClient.WaitForAsync("ScorecardSubmitted", "scorecardId", scorecard.Id(), Ct)).Should().Be(recruiter.UserId);

        // A task the recruiter makes for the owner: the creator is the actor, the owner the assignee. The owner then
        // completes and reopens it, so the actor changes while the assignee stays.
        var task = (await recruiter.PostAsync($"/v1/applications/{application}/tasks", new JsonObject
        {
            ["note"] = "Book the interview.", ["assigneeUserId"] = owner.UserId.ToString(),
        })).Expect(HttpStatusCode.Created).Id();
        var created = await ownerClient.WaitForAsync("ApplicationTaskCreated", "taskId", task, Ct);
        ActorOf(created).Should().Be(recruiter.UserId);
        AssigneeOf(created).Should().Be(owner.UserId);

        (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        var completed = await ownerClient.WaitForAsync("ApplicationTaskCompleted", "taskId", task, Ct);
        ActorOf(completed).Should().Be(owner.UserId);
        AssigneeOf(completed).Should().Be(owner.UserId);

        (await recruiter.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.OK);
        var reopened = await ownerClient.WaitForAsync("ApplicationTaskReopened", "taskId", task, Ct);
        ActorOf(reopened).Should().Be(recruiter.UserId, "the person who reopened it, not the one who completed it");
        AssigneeOf(reopened).Should().Be(owner.UserId);

        theirClient.ReceivedAnythingAbout(application).Should().BeFalse("another organization hears nothing about our candidates");
        theirClient.ReceivedAnythingAbout(task).Should().BeFalse();
        theirClient.ReceivedAnythingAbout(recruiter.UserId).Should().BeFalse("nor who our members are");
    }

    [Fact]
    public async Task A_move_made_with_an_API_key_has_no_actor()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var ownerClient = await EventsConnection.ConnectAsync(owner.Session, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));

        (await key.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var moved = await ownerClient.WaitForAsync("ApplicationMoved", "applicationId", application, Ct);
        moved.GetProperty("toStageId").GetString().Should().Be("screening");
        ActorOf(moved).Should().BeNull("a key acts for no member, so the dashboard shows the move to everyone");
    }

    [Fact]
    public async Task A_persons_email_carries_their_id()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await using var ownerClient = await EventsConnection.ConnectAsync(owner.Session, Ct);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var email = ApiSession.NewEmail("candidate");
        var application = (await owner.CreateApplicationAsync(job, email)).Id();

        var sent = (await recruiter.PostAsync("/v1/emails", new JsonObject
        {
            ["templateId"] = "application-received",
            ["applicationIds"] = new JsonArray(application),
        })).Expect(HttpStatusCode.Accepted);
        sent.At("queued").GetInt32().Should().Be(1);

        // The worker raises this one, from the id the API put in the send, after it has sent the email.
        (await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct)).Should().ContainSingle();
        ActorOf(await ownerClient.WaitForAsync("ApplicationEmailed", "applicationId", application, Ct)).Should().Be(recruiter.UserId);
    }

    [Fact]
    public async Task A_published_template_version_names_the_publisher_and_the_other_member_who_edited_it_last()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ownerClient = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, Ct);

        // The owner drafts it, the recruiter publishes it: the owner's work is what the recruiter published.
        var drafted = (await owner.CreateTemplateAsync("Platform roles")).Id();
        var published = (await recruiter.PostAsync($"/v1/templates/{drafted}/versions/1/publish")).Expect(HttpStatusCode.OK).Id();
        var byRecruiter = await ownerClient.WaitForAsync("HiringTemplateVersionPublished", "versionId", published, Ct);
        ActorOf(byRecruiter).Should().Be(recruiter.UserId);
        byRecruiter.GetProperty("previousEditorUserId").GetGuid().Should().Be(owner.UserId);

        // The only person to have touched a template publishing it has no one else to tell.
        var (_, aloneVersion) = await owner.CreatePublishedTemplateAsync("Solo roles");
        var bySoleEditor = await ownerClient.WaitForAsync("HiringTemplateVersionPublished", "versionId", aloneVersion, Ct);
        ActorOf(bySoleEditor).Should().Be(owner.UserId);
        bySoleEditor.Has("previousEditorUserId").Should().BeFalse("no other member edited it");

        theirClient.ReceivedAnythingAbout(published).Should().BeFalse("another organization's template events never reach us");
        theirClient.ReceivedAnythingAbout(aloneVersion).Should().BeFalse();
    }

    private static Guid? ActorOf(JsonElement data) => data.Has("actorUserId") ? data.GetProperty("actorUserId").GetGuid() : null;

    private static Guid? AssigneeOf(JsonElement data) => data.Has("assigneeUserId") ? data.GetProperty("assigneeUserId").GetGuid() : null;
}
