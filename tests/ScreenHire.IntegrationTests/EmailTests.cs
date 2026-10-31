using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Applications;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.Infrastructure.Tenancy;
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
            .Should().Equal("application-received", "stage-change", "questionnaire-request", "application-rejected", "talent-pool-invitation");
        list.Text("templates.2.name").Should().Be("Follow-up questions");
        list.At("templates.2.questionnaireOnly").GetBoolean().Should().BeTrue();
        list.Text("templates.3.name").Should().Be("Not progressing");
        list.At("templates.3.rejectOnly").GetBoolean().Should().BeTrue();
        list.Text("templates.4.name").Should().Be("Invitation to apply");
        list.At("templates.4.invitationOnly").GetBoolean().Should().BeTrue();
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
        (await owner.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK).Count("templates").Should().Be(6);

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
            ["body"] = "<h2>Hi {{ candidateName }},</h2><p><b>Welcome</b> to {{ organizationName }}.</p><ul><li>Bring ID</li></ul>"
                + "<img src=\"https://tracker.example.com/p.gif\">",
        })).Expect(HttpStatusCode.OK);
        preview.Text("subject").Should().Be("Hello Jordan Lee");
        preview.Text("html").Should().Contain("<h2>Hi Jordan Lee,</h2><p><strong>Welcome</strong> to").And.Contain("<ul><li>Bring ID</li></ul>")
            .And.NotContain("<img");
        preview.Text("text").Should().Contain("Hi Jordan Lee,").And.Contain("- Bring ID").And.NotContain("<");

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
                .PublishAsync(new SendApplicationEmail(owner.OrganizationId, application, sendId, owner.UserId));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(sendId.ToString(), Ct, nameof(SendApplicationEmail)) >= 2, "the worker to handle the send again", Ct);
        }

        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().ContainSingle("the second delivery found the timeline entry and sent nothing");
        EmailEntries(await owner.GetAsync($"/v1/applications/{application}/timeline")).Should().ContainSingle();
    }

    // The worker checks again when it sends: a kept copy that started from an email only an action sends (which the API
    // never stores) goes nowhere, leaves no timeline entry, and is not kept.
    [Fact]
    public async Task The_worker_drops_a_copy_of_an_email_only_an_action_sends()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Worker guard"))).Id();
        var email = ApiSession.NewEmail("guarded");
        var application = (await owner.CreateApplicationAsync(job, email)).Id();
        var sendId = Guid.CreateVersion7();
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            var row = await db.Applications.SingleAsync(a => a.Id == application, Ct);
            db.ApplicationEmails.Add(row.PrepareEmail(sendId, "application-rejected", email, "Not this time", "No.", "<p>No.</p>", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new SendApplicationEmail(owner.OrganizationId, application, sendId, owner.UserId));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(sendId.ToString(), Ct, nameof(SendApplicationEmail)) >= 1, "the worker to handle the send", Ct);
        }

        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().BeEmpty();
        EmailEntries(await owner.GetAsync($"/v1/applications/{application}/timeline")).Should().BeEmpty();
        await using var check = await fixture.CreateDbContextAsync(tenant, Ct);
        (await check.ApplicationEmails.AnyAsync(e => e.Id == sendId, Ct)).Should().BeFalse("the copy of an email that never went is not kept");
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
        // Wording a person writes by hand cannot use the rejection email's reason, nor start from that template.
        (await owner.PostAsync("/v1/emails", Wording("Your application", "{{ reason }}", applications: [application])))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("body");
        (await owner.PostAsync("/v1/emails", Wording("Your application", "Not this time.", applications: [application], templateId: "application-rejected")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateId");
        (await owner.PostAsync("/v1/emails/preview", Draft(application, "Your application", "{{ reason }}"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("body");

        // The follow-up questions email carries a link made for one request, so only SendQuestionnaire sends it.
        (await owner.PostAsync("/v1/emails", Send("questionnaire-request", [application]))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("templateId");
        (await owner.PostAsync("/v1/emails/preview", Draft(application, "More questions", "{{ questionnaireLink }}"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("body");

        var queued = (await owner.PostAsync("/v1/emails", Send("stage-change", [application]))).Expect(HttpStatusCode.Accepted);
        queued.At("queued").GetInt32().Should().Be(0);
        queued.At("skipped").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_preview_renders_unsent_wording_with_the_candidates_own_details()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Preview job"))).Id();
        var application = (await CreateNamedApplicationAsync(owner, job, ApiSession.NewEmail("preview"), "Robin", "Ellis")).Id();

        var preview = (await owner.PostAsync("/v1/emails/preview", Draft(application, "Next steps for {{ jobTitle }}", "Hi {{ firstName }} {{ lastName }}, you are in {{ stageName }}.")))
            .Expect(HttpStatusCode.OK);

        preview.Text("subject").Should().Be("Next steps for Preview job");
        preview.Text("text").Should().Contain("Hi Robin Ellis, you are in Applied.");
        preview.Text("html").Should().Contain("<p>Hi Robin Ellis, you are in Applied.</p>");
        (await owner.PostAsync("/v1/emails/preview", Draft(application, "Hi", "{{ nope }}"))).Expect(HttpStatusCode.BadRequest);
    }

    // The email composer (design system section 11): a template changed for one send goes to each candidate rendered with their own
    // details, the template stays as it was, and each candidate's copy is kept as it went, for Messages.
    [Fact]
    public async Task A_template_changed_for_one_send_reaches_each_candidate_as_written_and_is_kept_as_it_went()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var template = (await owner.PostAsync("/v1/email-templates", Wording("Interview for {{ jobTitle }}", "Hi {{ firstName }}, let us talk.", "Interview invitation")))
            .Expect(HttpStatusCode.Created).Text("id");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Changed email"))).Id();
        var casey = ApiSession.NewEmail("changed-casey");
        var riley = ApiSession.NewEmail("changed-riley");
        var applications = new[]
        {
            (await CreateNamedApplicationAsync(owner, job, casey, "Casey", "Morgan")).Id(),
            (await owner.CreateApplicationAsync(job, riley, "Riley Chen")).Id(),
        };

        var queued = (await owner.PostAsync("/v1/emails", Wording(
                $"Interview {marker} for {{{{ jobTitle }}}}", "Hi {{ firstName }}, your portfolio stood out.\n\nLet us talk.", applications, template)))
            .Expect(HttpStatusCode.Accepted);
        queued.At("queued").GetInt32().Should().Be(2);

        var toCasey = await WaitForSubjectAsync(casey, marker);
        toCasey.Subject.Should().Be($"Interview {marker} for Changed email");
        toCasey.Text.Should().Contain("Hi Casey, your portfolio stood out.");
        // A candidate from before the name was split (one name, kept whole) is greeted by all of it.
        (await WaitForSubjectAsync(riley, marker)).Text.Should().Contain("Hi Riley Chen, your portfolio stood out.");

        var unchanged = (await owner.GetAsync($"/v1/email-templates/{template}")).Expect(HttpStatusCode.OK);
        unchanged.Text("subject").Should().Be("Interview for {{ jobTitle }}");
        unchanged.Text("body").Should().Be("<p>Hi {{ firstName }}, let us talk.</p>", "the template is stored as rich text");

        var entry = await SentEntryAsync(owner, applications[0]);
        entry.GetProperty("emailTemplateId").GetString().Should().Be(template);
        var emailId = entry.GetProperty("emailId").GetGuid();
        var copy = (await owner.GetAsync($"/v1/emails/{emailId}")).Expect(HttpStatusCode.OK);
        copy.Text("subject").Should().Be($"Interview {marker} for Changed email");
        copy.Text("text").Should().Contain("Hi Casey, your portfolio stood out.");
        copy.Text("html").Should().Contain("<p>Hi Casey, your portfolio stood out.</p>");
        copy.Text("to").Should().Be(casey);
        copy.Text("templateId").Should().Be(template);

        var rileysCopy = (await owner.GetAsync($"/v1/emails/{(await SentEntryAsync(owner, applications[1])).GetProperty("emailId").GetGuid()}"))
            .Expect(HttpStatusCode.OK);
        rileysCopy.Text("text").Should().Contain("Hi Riley Chen,", "each candidate's own copy is kept");
    }

    // Messages lists every email to the candidate, about any of their applications, newest first, and a blank email
    // has no template. Viewers read the copies; API keys do not, because the email routes are for people only.
    [Fact]
    public async Task A_candidates_emails_across_their_applications_are_listed_newest_first_for_people_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var marker = Guid.NewGuid().ToString("N")[..8];
        var address = ApiSession.NewEmail("messages");
        var first = (await CreateNamedApplicationAsync(owner, (await owner.CreateJobAsync(Samples.RemoteJob("First job"))).Id(), address, "Sam", "Taylor")).Id();
        var second = (await CreateNamedApplicationAsync(owner, (await owner.CreateJobAsync(Samples.RemoteJob("Second job"))).Id(), address, "Sam", "Taylor")).Id();
        var candidate = (await owner.GetAsync($"/v1/applications/{first}")).Expect(HttpStatusCode.OK).Text("candidate.id");

        (await owner.PostAsync("/v1/emails", Wording($"First {marker}", "Hi {{ firstName }}.", [first]))).Expect(HttpStatusCode.Accepted);
        await WaitForSubjectAsync(address, $"First {marker}");
        await SentEntryAsync(owner, first);
        (await owner.PostAsync("/v1/emails", Wording($"Second {marker}", "Hello again, {{ firstName }}.", [second]))).Expect(HttpStatusCode.Accepted);
        await WaitForSubjectAsync(address, $"Second {marker}");
        await SentEntryAsync(owner, second);

        var listed = (await viewer.GetAsync($"/v1/candidates/{candidate}/emails")).Expect(HttpStatusCode.OK);
        listed.Count().Should().Be(2);
        listed.Text("0.subject").Should().Be($"Second {marker}");
        listed.Text("0.jobTitle").Should().Be("Second job");
        listed.Text("1.subject").Should().Be($"First {marker}");
        listed.At("1.templateId").ValueKind.Should().Be(JsonValueKind.Null, "a blank email has no template");
        listed.At("1.kept").GetBoolean().Should().BeTrue();
        listed.Text("1.actorUserId").Should().Be(owner.UserId.ToString());
        var emailId = listed.Text("0.id");
        (await viewer.GetAsync($"/v1/emails/{emailId}")).Expect(HttpStatusCode.OK).Text("text").Should().Contain("Hello again, Sam.");

        var token = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Sync", ["scopes"] = new JsonArray("Read", "Write") }))
            .Expect(HttpStatusCode.Created).Text("token");
        using var key = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = fixture.GetEndpoint("api") };
        key.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using (var byKey = await key.GetAsync($"/v1/emails/{emailId}", Ct))
        {
            byKey.IsSuccessStatusCode.Should().BeFalse("an API key reads no email");
        }

        using (var listByKey = await key.GetAsync($"/v1/candidates/{candidate}/emails", Ct))
        {
            listByKey.IsSuccessStatusCode.Should().BeFalse("an API key reads no email");
        }
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
        (await ours.PostAsync("/v1/emails/preview", Draft(theirApplication, "Hi", "Hello"))).Expect(HttpStatusCode.NotFound);
        (await theirs.PostAsync("/v1/emails", Send("stage-change", [theirApplication]))).Expect(HttpStatusCode.Accepted);
        var theirEmail = (await SentEntryAsync(theirs, theirApplication)).GetProperty("emailId").GetGuid();
        var theirCandidate = (await theirs.GetAsync($"/v1/applications/{theirApplication}")).Expect(HttpStatusCode.OK).Text("candidate.id");
        (await ours.GetAsync($"/v1/emails/{theirEmail}")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync($"/v1/candidates/{theirCandidate}/emails")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync("/v1/email-templates")).Expect(HttpStatusCode.OK).Count("templates").Should().Be(5);
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

    // A send of wording a person wrote, started from `templateId` or blank.
    private static JsonObject Wording(string subject, string body, IEnumerable<Guid> applications, string? templateId = null) => new()
    {
        ["subject"] = subject,
        ["body"] = body,
        ["templateId"] = templateId,
        ["applicationIds"] = new JsonArray([.. applications.Select(id => JsonValue.Create(id))]),
    };

    private static JsonObject Draft(Guid applicationId, string subject, string body) =>
        new() { ["applicationId"] = applicationId, ["subject"] = subject, ["body"] = body };

    private static async Task<ApiResult> CreateNamedApplicationAsync(Tenant owner, Guid jobId, string email, string firstName, string lastName) =>
        (await owner.PostAsync($"/v1/jobs/{jobId}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = firstName, ["lastName"] = lastName, ["email"] = email },
        })).Expect(HttpStatusCode.Created);

    // The application's EmailSent entry, once the worker has committed it: Mailpit gets the email just before.
    private static async Task<JsonElement> SentEntryAsync(Tenant owner, Guid applicationId)
    {
        var entry = default(JsonElement);
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var entries = EmailEntries(await owner.GetAsync($"/v1/applications/{applicationId}/timeline"));
                if (entries.Count == 0)
                {
                    return false;
                }

                entry = entries[^1].Clone();
                return true;
            },
            "the email's timeline entry",
            Ct);
        return entry;
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
