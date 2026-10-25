using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Applications;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Organization email templates and email to candidates through the running API, worker and Mailpit: owners and admins
/// change the wording, a person sends a template to one candidate or a selection, every email lands in Mailpit and in the
/// application's activity, and the rejection email stays with the Reject action. Each test uses its own organization.
/// </summary>
public sealed class EmailTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_rewords_a_built_in_template_adds_one_and_restores_and_deletes_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var list = (await owner.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK);
        list.At("sendingEnabled").GetBoolean().Should().BeTrue();
        list.Body.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetString())
            .Should().Equal("application-received", "stage-change", "questionnaire-request", "application-rejected");
        list.Text("templates.2.name").Should().Be("Follow-up questions");
        list.At("templates.2.questionnaireOnly").GetBoolean().Should().BeTrue();
        list.Text("templates.3.name").Should().Be("Not progressing");
        list.At("templates.3.rejectOnly").GetBoolean().Should().BeTrue();
        list.Body.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("name").GetString())
            .Should().Contain(["candidateName", "jobTitle", "organizationName", "stageName", "reason", "questionnaireLink"]);

        var reworded = (await owner.PutAsync("/v1/email-templates/stage-change", Wording("News about {{ jobTitle }}", "Hi {{ candidateName }}, you are in {{ stageName }}.")))
            .Expect(HttpStatusCode.OK);
        reworded.At("edited").GetBoolean().Should().BeTrue();
        reworded.Text("name").Should().Be("Moved to the next step", "a built-in keeps ScreenHire's name");

        var created = (await owner.PostAsync("/v1/email-templates", Wording("Interview at {{ organizationName }}", "Hi {{ candidateName }}, let us talk.", "Interview invitation")))
            .Expect(HttpStatusCode.Created);
        var id = created.Text("id");
        Guid.TryParseExact(id, "D", out _).Should().BeTrue();
        (await owner.GetAsync($"/v1/email-templates/{id}")).Expect(HttpStatusCode.OK).Text("name").Should().Be("Interview invitation");
        (await owner.PutAsync($"/v1/email-templates/{id}", Wording("Interview", "Hi.", "Interview call"))).Expect(HttpStatusCode.OK)
            .Text("name").Should().Be("Interview call");
        (await owner.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK).Count("templates").Should().Be(5);

        (await owner.DeleteAsync("/v1/email-templates/stage-change")).Expect(HttpStatusCode.NoContent);
        var restored = (await owner.GetAsync("/v1/email-templates/stage-change")).Expect(HttpStatusCode.OK);
        restored.At("edited").GetBoolean().Should().BeFalse();
        restored.Text("subject").Should().Contain("An update on your application");

        (await owner.DeleteAsync($"/v1/email-templates/{id}")).Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/v1/email-templates/{id}")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Wording_outside_the_variables_and_safe_tags_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var unknown = (await owner.PostAsync("/v1/email-templates", Wording("Hi {{ candidateEmail }}", "Body", "Bad"))).Expect(HttpStatusCode.BadRequest);
        unknown.ProblemKeys().Should().Equal("subject");
        (await owner.PostAsync("/v1/email-templates", Wording("Hi", "{% for i in (1..9) %}x{% endfor %}", "Loop"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("body");
        (await owner.PutAsync("/v1/email-templates/application-rejected", Wording("Your application", "Hi {{ candidateName }}, no.")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("body");
        (await owner.PutAsync("/v1/email-templates/stage-change", Wording("Hi", "The reason: {{ reason }}")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("body");
        (await owner.PutAsync("/v1/email-templates/stage-change", Wording("Hi", "Hello", "Renamed"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("name");
    }

    [Fact]
    public async Task The_preview_and_the_test_send_render_unsaved_wording_with_the_sample_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var me = (await owner.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;

        var preview = (await owner.PostAsync("/v1/email-templates/preview", new JsonObject
        {
            ["subject"] = "Hello {{ candidateName }}",
            ["body"] = "Hi {{ candidateName }},\n\n<b>Welcome</b> to {{ organizationName }}.",
        })).Expect(HttpStatusCode.OK);
        preview.Text("subject").Should().Be("Hello Jordan Lee");
        preview.Text("text").Should().Contain("<b>Welcome</b>");
        preview.Text("html").Should().Contain("&lt;b&gt;Welcome&lt;/b&gt;").And.NotContain("<b>");

        (await owner.PostAsync("/v1/email-templates/preview", new JsonObject { ["subject"] = "{{ nope }}", ["body"] = "Hi" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("subject");

        var marker = Guid.NewGuid().ToString("N")[..8];
        (await owner.PostAsync("/v1/email-templates/test", new JsonObject { ["subject"] = $"Check {marker}", ["body"] = "Hi {{ candidateName }}" }))
            .Expect(HttpStatusCode.OK).Text("to").Should().Be(me);
        var received = await WaitForSubjectAsync(me, marker);
        received.Subject.Should().Be($"[Test] Check {marker}");
        received.Text.Should().Contain("Hi Jordan Lee");
    }

    [Fact]
    public async Task Emailing_a_selection_sends_each_candidate_the_organizations_wording_and_records_it_in_activity()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        (await owner.PutAsync("/v1/email-templates/application-received", Wording($"Thanks {marker}, {{{{ candidateName }}}}", "Hi {{ candidateName }}, we have it.")))
            .Expect(HttpStatusCode.OK);
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Bulk email"))).Id();
        var emails = Enumerable.Range(0, 3).Select(i => ApiSession.NewEmail($"bulk{i}")).ToList();
        var applications = new List<Guid>();
        foreach (var (email, index) in emails.Select((email, index) => (email, index)))
        {
            applications.Add((await owner.CreateApplicationAsync(job, email, $"Candidate {index}")).Id());
        }

        using var other = await fixture.SignedInWithOrganizationAsync();
        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var otherEvents = await EventsConnection.ConnectAsync(other.Session, Ct);
        var queued = (await owner.PostAsync("/v1/emails", Send("application-received", applications))).Expect(HttpStatusCode.Accepted);
        queued.At("queued").GetInt32().Should().Be(3);
        queued.At("skipped").GetInt32().Should().Be(0);

        for (var i = 0; i < 3; i++)
        {
            var sent = await fixture.Mailpit.WaitForMessagesAsync(emails[i], 1, Ct);
            sent.Should().ContainSingle();
            sent[0].Subject.Should().Be($"Thanks {marker}, Candidate {i}");
        }

        await events.WaitForAsync("ApplicationEmailed", "applicationId", applications[0], Ct);
        otherEvents.ReceivedAnythingAbout(applications[0]).Should().BeFalse("another organization hears nothing about our emails");
        var entry = (await owner.GetAsync($"/v1/applications/{applications[0]}/timeline")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Single(e => e.GetProperty("type").GetString() == "EmailSent");
        entry.GetProperty("emailTemplateId").GetString().Should().Be("application-received");
        entry.GetProperty("emailSubject").GetString().Should().Be($"Thanks {marker}, Candidate 0");
        entry.GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);
    }

    // Delivery is at least once: the same send in a new envelope, as a recovered outbox would deliver it, finds its
    // timeline entry (the send's id) and sends nothing twice.
    [Fact]
    public async Task A_send_delivered_again_sends_nothing_twice()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Redelivered email"))).Id();
        var email = ApiSession.NewEmail("redelivered");
        var application = (await owner.CreateApplicationAsync(job, email)).Id();
        (await owner.PostAsync("/v1/emails", Send("application-received", [application]))).Expect(HttpStatusCode.Accepted);
        (await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct)).Should().ContainSingle();
        // The email reaches Mailpit just before the worker commits its timeline entry.
        var sendId = Guid.Empty;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var entries = EmailEntries(await owner.GetAsync($"/v1/applications/{application}/timeline"));
                sendId = entries.Count == 1 ? entries[0].GetProperty("id").GetGuid() : Guid.Empty;
                return sendId != Guid.Empty;
            },
            "the send's timeline entry",
            Ct);

        // Both envelopes handled, the first and the second: the worker marks an envelope handled in a later batch, so a
        // count taken before publishing can still rise for the first one alone.
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new SendApplicationEmail(owner.OrganizationId, application, "application-received", sendId, owner.UserId));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(sendId.ToString(), Ct, nameof(SendApplicationEmail)) >= 2, "the worker to handle the send again", Ct);
        }

        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().ContainSingle("the second delivery found the timeline entry and sent nothing");
        EmailEntries(await owner.GetAsync($"/v1/applications/{application}/timeline")).Should().ContainSingle();
    }

    // Workflow-engine.md §2: the rejection email goes only with Reject, and stage-change never tells an ended application
    // it moved on.
    [Fact]
    public async Task The_rejection_email_is_never_sent_by_hand_and_stage_change_skips_ended_applications()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Rejection rule"))).Id();
        var email = ApiSession.NewEmail("ended");
        var application = (await owner.CreateApplicationAsync(job, email)).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);

        (await owner.PostAsync("/v1/emails", Send("application-rejected", [application]))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("templateId");
        (await owner.GetAsync($"/v1/emails/preview?applicationId={application}&templateId=application-rejected")).Expect(HttpStatusCode.BadRequest);

        // The follow-up questions email carries a link made for one request, so only SendQuestionnaire sends it.
        (await owner.PostAsync("/v1/emails", Send("questionnaire-request", [application]))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("templateId");
        (await owner.GetAsync($"/v1/emails/preview?applicationId={application}&templateId=questionnaire-request")).Expect(HttpStatusCode.BadRequest);

        var queued = (await owner.PostAsync("/v1/emails", Send("stage-change", [application]))).Expect(HttpStatusCode.Accepted);
        queued.At("queued").GetInt32().Should().Be(0);
        queued.At("skipped").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_preview_for_a_candidate_uses_their_application()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Preview job"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("preview"), "Robin Ellis")).Id();

        var preview = (await owner.GetAsync($"/v1/emails/preview?applicationId={application}&templateId=stage-change")).Expect(HttpStatusCode.OK);

        preview.Text("subject").Should().Be("An update on your application for Preview job");
        preview.Text("text").Should().Contain("Hi Robin Ellis").And.Contain("the next step: Applied");
    }

    [Fact]
    public async Task Recruiters_send_and_read_templates_but_only_owners_and_admins_change_them_and_viewers_send_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Roles"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("roles"))).Id();

        (await recruiter.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK);
        (await recruiter.PutAsync("/v1/email-templates/stage-change", Wording("Hi", "Hello"))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/email-templates", Wording("Hi", "Hello", "Mine"))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/email-templates/test", new JsonObject { ["subject"] = "Hi", ["body"] = "Hello" })).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/emails", Send("stage-change", [application]))).Expect(HttpStatusCode.Accepted);
        (await viewer.PostAsync("/v1/emails", Send("stage-change", [application]))).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_organizations_templates_and_applications_are_out_of_reach()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var theirTemplate = (await theirs.PostAsync("/v1/email-templates", Wording("Theirs", "Hi", "Theirs"))).Expect(HttpStatusCode.Created).Text("id");
        var theirJob = (await theirs.CreateJobAsync(Samples.RemoteJob("Theirs"))).Id();
        var theirApplication = (await theirs.CreateApplicationAsync(theirJob, ApiSession.NewEmail("theirs"))).Id();

        (await ours.GetAsync($"/v1/email-templates/{theirTemplate}")).Expect(HttpStatusCode.NotFound);
        (await ours.PutAsync($"/v1/email-templates/{theirTemplate}", Wording("Mine", "Hi", "Mine"))).Expect(HttpStatusCode.NotFound);
        (await ours.DeleteAsync($"/v1/email-templates/{theirTemplate}")).Expect(HttpStatusCode.NotFound);
        (await ours.PostAsync("/v1/emails", Send(theirTemplate, [theirApplication]))).Expect(HttpStatusCode.BadRequest);
        (await ours.PostAsync("/v1/emails", Send("stage-change", [theirApplication]))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("applicationIds");
        (await ours.GetAsync($"/v1/emails/preview?applicationId={theirApplication}&templateId=stage-change")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK).Count("templates").Should().Be(4);
    }

    // An automation may send any of the organization's templates, which then cannot be deleted from under it; a workflow
    // that names a template the organization does not have is refused at save.
    [Fact]
    public async Task An_automation_sends_an_organization_template_that_cannot_be_deleted_while_it_is_named()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var template = (await owner.PostAsync("/v1/email-templates", Wording($"Screening {marker}", "Hi {{ candidateName }}, welcome to {{ stageName }}.", "Screening welcome")))
            .Expect(HttpStatusCode.Created).Text("id");

        var missing = (await owner.PostAsync("/v1/workflows/validate", new JsonObject
        {
            ["workflow"] = Samples.DefinitionWith(AutomationSamples.EmailOnStage("screening", Guid.CreateVersion7().ToString()))["workflow"]!.DeepClone(),
        })).Expect(HttpStatusCode.OK);
        missing.At("valid").GetBoolean().Should().BeFalse();
        missing.Text("errors.0.path").Should().Be("automations[0].actions[0].emailTemplate");

        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Custom automation email", AutomationSamples.EmailOnStage("screening", template));
        var email = ApiSession.NewEmail("automated");
        var application = (await owner.CreateApplicationAsync(job, email, "Ari Lane")).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var sent = await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct);
        sent[0].Subject.Should().Be($"Screening {marker}");
        sent[0].Text.Should().Contain("welcome to Screening");
        await AutomationSamples.WaitForRunAsync(owner, application, "email-on-stage", "Completed");
        // The run completes when it queues the email; the send handler mails it, then commits the timeline entry, so
        // Mailpit can have the email a moment before the entry is there.
        JsonElement entry = default;
        await WolverineStore.WaitUntilAsync(async () =>
        {
            var timeline = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body;
            var sentEntries = timeline.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "EmailSent").ToList();
            sentEntries.Should().HaveCountLessThan(2);
            if (sentEntries.Count == 0)
            {
                return false;
            }

            entry = sentEntries[0].Clone();
            return true;
        }, "the automation's email to reach the timeline", Ct);
        entry.GetProperty("automationRunId").ValueKind.Should().Be(JsonValueKind.String);
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);

        var refused = (await owner.DeleteAsync($"/v1/email-templates/{template}")).Expect(HttpStatusCode.Conflict);
        refused.Text("detail").Should().Contain("Custom automation email");
    }

    private static List<JsonElement> EmailEntries(ApiResult timeline) =>
        [.. timeline.Expect(HttpStatusCode.OK).Body.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "EmailSent")];

    private static JsonObject Wording(string subject, string body, string? name = null)
    {
        var wording = new JsonObject { ["subject"] = subject, ["body"] = body };
        if (name is not null)
        {
            wording["name"] = name;
        }

        return wording;
    }

    private static JsonObject Send(string templateId, IEnumerable<Guid> applicationIds) => new()
    {
        ["templateId"] = templateId,
        ["applicationIds"] = new JsonArray([.. applicationIds.Select(id => JsonValue.Create(id))]),
    };

    private async Task<MailpitMessage> WaitForSubjectAsync(string address, string marker)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var match = (await fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject.Contains(marker, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Mailpit received no email with {marker} in its subject.");
            }

            await Task.Delay(200, Ct);
        }
    }
}
