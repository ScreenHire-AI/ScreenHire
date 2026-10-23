using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// The hiring events through the hub: publishing a job, adding an application, moving it and publishing a template
/// version each reach the open dashboards of their own organization, carrying ids and the facts a client needs to
/// refresh, and never reach another organization's.
/// </summary>
public sealed class HiringRealtimeTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Hiring_events_reach_their_own_organization_with_their_facts_and_never_another_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);

        var job = await ours.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await ours.PublishJobAsync(job.Id());
        var published = await ourClient.WaitForAsync("JobPublished", "jobId", job.Id(), cancellationToken);
        published.GetProperty("organizationId").GetGuid().Should().Be(ours.OrganizationId);
        published.GetProperty("version").GetInt32().Should().Be(1);
        var versions = (await ours.GetAsync($"/v1/jobs/{job.Id()}/versions")).Expect(HttpStatusCode.OK);
        published.GetProperty("jobVersionId").GetGuid().Should().Be(versions.Id("0.id"), "the event names the version it published");

        var application = await ours.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var submitted = await ourClient.WaitForAsync("ApplicationSubmitted", "applicationId", application.Id(), cancellationToken);
        submitted.GetProperty("stageId").GetString().Should().Be("applied");

        (await ours.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" }))
            .Expect(HttpStatusCode.OK);
        var moved = await ourClient.WaitForAsync("ApplicationMoved", "applicationId", application.Id(), cancellationToken);
        moved.GetProperty("fromStageId").GetString().Should().Be("applied");
        moved.GetProperty("toStageId").GetString().Should().Be("screening");

        var (templateId, versionId) = await ours.CreatePublishedTemplateAsync("Platform roles");
        var templatePublished = await ourClient.WaitForAsync("HiringTemplateVersionPublished", "versionId", versionId, cancellationToken);
        templatePublished.GetProperty("templateId").GetGuid().Should().Be(templateId);
        templatePublished.GetProperty("version").GetInt32().Should().Be(1);

        // Our last event has arrived, so an event of ours sent to them would have too.
        theirClient.ReceivedAnythingAbout(job.Id()).Should().BeFalse("another organization's job events never reach us");
        theirClient.ReceivedAnythingAbout(application.Id()).Should().BeFalse();
        theirClient.ReceivedAnythingAbout(versionId).Should().BeFalse();
    }

    [Fact]
    public async Task Notes_tags_and_completed_tasks_reach_their_own_organization_with_ids_only_and_never_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);
        var job = await Automations.AutomationSamples.JobWithAutomationsAsync(ours, "Platform Engineer", new JsonObject
        {
            ["id"] = "todo",
            ["name"] = "Make a to-do",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "CreateTask", ["note"] = "Call the references." }),
        });
        var application = (await ours.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();

        var note = (await ours.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "Strong portfolio." }))
            .Expect(HttpStatusCode.Created);
        var noted = await ourClient.WaitForAsync("ApplicationNoteAdded", "applicationId", application, cancellationToken);
        noted.GetProperty("eventId").GetGuid().Should().Be(note.Id());
        noted.TryGetProperty("note", out _).Should().BeFalse("the note's text stays behind the API");

        (await ours.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "referral" })).Expect(HttpStatusCode.OK);
        var tagged = await ourClient.WaitForAsync("ApplicationTagsChanged", "applicationId", application, cancellationToken);
        tagged.GetProperty("jobId").GetGuid().Should().Be(job);

        (await ours.PostAsync($"/v1/applications/{application}/automations/todo/run")).Expect(HttpStatusCode.Accepted);
        await Automations.AutomationSamples.WaitForRunAsync(ours, application, "todo", "Completed");
        var task = (await ours.GetAsync("/v1/tasks")).Id("0.id");
        (await ours.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        var completed = await ourClient.WaitForAsync("ApplicationTaskCompleted", "taskId", task, cancellationToken);
        completed.GetProperty("applicationId").GetGuid().Should().Be(application);

        theirClient.ReceivedAnythingAbout(application).Should().BeFalse("another organization's notes, tags and tasks never reach us");
        theirClient.ReceivedAnythingAbout(task).Should().BeFalse();
    }

    [Fact]
    public async Task A_job_workflow_change_reaches_its_own_organization_with_the_new_version_and_never_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);
        var job = await ours.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var workflow = JsonNode.Parse(job.At("workflow").GetRawText())!.AsObject();
        workflow["stages"]![1]!["name"] = "Phone screen";

        (await ours.PutAsync($"/v1/jobs/{job.Id()}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 }))
            .Expect(HttpStatusCode.OK);

        var changed = await ourClient.WaitForAsync("JobWorkflowChanged", "jobId", job.Id(), cancellationToken);
        changed.GetProperty("organizationId").GetGuid().Should().Be(ours.OrganizationId);
        changed.GetProperty("workflowVersion").GetInt32().Should().Be(2);
        theirClient.ReceivedAnythingAbout(job.Id()).Should().BeFalse("another organization's workflow changes never reach us");
    }

    [Fact]
    public async Task An_integrity_assessment_reaches_its_own_organization_with_levels_and_no_evidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);
        var job = await ours.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await ours.PublishJobAsync(job.Id());

        // A recruiter-added application is the quickest trigger: the worker assesses it like any other.
        var application = await ours.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var assessed = await ourClient.WaitForAsync("IntegrityAssessed", "applicationId", application.Id(), cancellationToken);

        assessed.GetProperty("jobId").GetGuid().Should().Be(job.Id());
        assessed.GetProperty("authenticity").GetString().Should().Be("NoConcerns");
        assessed.GetProperty("aiAssistance").GetString().Should().Be("NotIndicated");
        assessed.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["organizationId", "applicationId", "jobId", "authenticity", "aiAssistance", "rulesetVersion", "eventId", "assessedAt"],
            "the push carries ids and levels, never evidence");
        theirClient.ReceivedAnythingAbout(application.Id()).Should().BeFalse();
    }

    [Fact]
    public async Task Republishing_changed_content_pushes_a_second_JobPublished_and_an_unchanged_publish_pushes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await using var client = await EventsConnection.ConnectAsync(recruiter.Session, cancellationToken);
        var body = Samples.RemoteJob("Data Engineer");
        var job = await recruiter.CreateJobAsync(body);
        await recruiter.PublishJobAsync(job.Id());
        await client.WaitForAsync("JobPublished", "jobId", job.Id(), cancellationToken);

        await recruiter.PublishJobAsync(job.Id());
        body["title"] = "Staff Data Engineer";
        (await recruiter.PutAsync($"/v1/jobs/{job.Id()}", body)).Expect(HttpStatusCode.OK);
        await recruiter.PublishJobAsync(job.Id());
        await recruiter.CloseJobAsync(job.Id());
        await client.WaitForAsync("JobClosed", "jobId", job.Id(), cancellationToken);

        client.Received.Count(e => e.Type == "JobPublished" && e.Data.GetProperty("jobId").GetGuid() == job.Id())
            .Should().Be(2, "version 1, then version 2 for the changed title; the unchanged publish between them raised nothing");
    }
}
