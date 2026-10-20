using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Form telemetry (integrity-engine.md §10.2): the application form issues a session, ingest stores strict snapshots
/// of counts only, submission binds them before the pipeline runs, behavioural signals stay context on their own, the
/// assistive-technology switch deletes them, the device check follows the job's basis, and retention purges them. Each
/// test makes its own organization with a job copied from the "Software engineer" system template, whose
/// <c>notice_period</c> question is free text.
/// </summary>
public sealed class TelemetryTests(AppHostFixture fixture)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_form_issues_a_session_and_the_device_check_basis_where_the_job_is()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("uk", ["GB"]), ("germany", ["DE"]));
        using var client = fixture.CreateHttpClient("api");

        var uk = await FormAsync(client, organization, jobs[0]);
        var germany = await FormAsync(client, organization, jobs[1]);

        uk.At("telemetry.sessionToken").GetString().Should().NotBeNullOrEmpty();
        uk.At("telemetry.deviceCheck").GetString().Should().Be("Notice");
        germany.At("telemetry.deviceCheck").GetString().Should().Be("Consent", "the API's appsettings ask for consent in the EU");
        (await FormAsync(client, organization, jobs[0])).At("telemetry.sessionToken").GetString()
            .Should().NotBe(uk.At("telemetry.sessionToken").GetString(), "every form gets a session of its own");
    }

    [Fact]
    public async Task Ingest_takes_counts_and_refuses_unknown_members_fields_markers_tokens_and_oversized_bodies()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("engineer", ["GB"]), ("other", ["GB"]));
        var job = jobs[0];
        using var client = fixture.CreateHttpClient("api");
        var token = await TokenAsync(client, organization, job);
        var otherJobsToken = await TokenAsync(client, organization, jobs[1]);

        (await PostAsync(client, organization, job, Snapshot(token))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var extraMember = Snapshot(token);
        extraMember["answer"] = "Three months, but I can start sooner.";
        var extraFieldMember = Snapshot(token);
        extraFieldMember["fields"]![0]!["value"] = "Three months, but I can start sooner.";
        var extraPageMember = Snapshot(token);
        extraPageMember["page"]!["keys"] = new JsonArray("T", "h");
        foreach (var refused in new[]
        {
            extraMember,
            extraFieldMember,
            extraPageMember,
            Snapshot(token, fieldId: "not_a_question"),
            Snapshot(token, marker: "my-marker"),
            Snapshot(token, typed: -1),
            Snapshot("CfDJ8forged-token"),
            Snapshot(otherJobsToken),
        })
        {
            (await PostAsync(client, organization, job, refused)).StatusCode.Should().Be(HttpStatusCode.BadRequest, refused.ToJsonString());
        }

        var oversized = Snapshot(token);
        oversized["sessionToken"] = new string('x', 40 * 1024);
        (await PostAsync(client, organization, job, oversized)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        await using var db = await TenantDbAsync(organization.Id);
        var stored = await db.TelemetrySnapshots.Where(s => s.JobId == job.Id).ToListAsync(Ct);
        stored.Should().ContainSingle("only the valid snapshot is stored");
        stored[0].ApplicationId.Should().BeNull();
        stored[0].ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(1), TimeSpan.FromMinutes(5), "an unsent form's telemetry lives a day");
    }

    [Fact]
    public async Task Submission_binds_the_session_and_behavioural_signals_alone_are_not_shown()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("engineer", ["GB"]));
        var job = jobs[0];
        using var client = fixture.CreateHttpClient("api");
        var token = await TokenAsync(client, organization, job);
        var detailsPage = Guid.CreateVersion7();
        var questionsPage = Guid.CreateVersion7();

        // The details page twice (a later snapshot replaces the earlier), the questions page once with a pasted answer.
        await PostAsync(client, organization, job, Snapshot(token, detailsPage, 1, fieldId: "fullName", typed: 5));
        await PostAsync(client, organization, job, Snapshot(token, detailsPage, 2, fieldId: "fullName", typed: 12));
        await PostAsync(client, organization, job, Snapshot(token, detailsPage, 1, fieldId: "fullName", typed: 99)); // late and stale
        await PostAsync(client, organization, job, Snapshot(token, questionsPage, 1, fieldId: "notice_period", pasted: 77, pastes: 1));
        var final = Snapshot(token, Guid.CreateVersion7(), 1, fieldId: "aiUseDetails", typed: 20);

        var applicationId = await ApplyAsync(client, organization, job, "casey.morgan@example.com", token, final);
        var assessments = await AssessedAsync(organization.Id, applicationId);

        await using var db = await TenantDbAsync(organization.Id);
        var snapshots = await db.TelemetrySnapshots.Where(s => s.JobId == job.Id).ToListAsync(Ct);
        snapshots.Should().HaveCount(3).And.AllSatisfy(s =>
        {
            s.ApplicationId.Should().Be(applicationId);
            s.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
        });
        snapshots.Single(s => s.PageViewId == detailsPage).Fields.Single().TypedCharacters.Should().Be(12);

        var signals = await db.IntegritySignals.Where(s => s.ApplicationId == applicationId && s.Category == SignalCategory.Behavioural).ToListAsync(Ct);
        var paste = signals.Should().ContainSingle().Which;
        paste.Code.Should().Be(SignalCatalogue.PasteDominant);
        paste.AccessibilitySensitive.Should().BeTrue();
        paste.Explanation.Should().Contain("What is your notice period?");
        var ai = assessments.Single(a => a.Kind == IntegrityAssessmentKind.AiAssistance);
        ai.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated));
        ai.AppliedRules.Should().Contain(IntegrityRuleset.TelemetryOnlyAlongsideAnotherCategory);
        ai.ReasonCodes.Should().BeEmpty("telemetry alone is never a reason");
        ai.SignalIds.Should().NotContain(paste.Id, "telemetry alone is not shown on the assessment; the signal is kept for when another category joins it");

        // A beacon that arrives after the application was sent changes nothing.
        (await PostAsync(client, organization, job, Snapshot(token, Guid.CreateVersion7(), 1, fieldId: "fullName", pasted: 500)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await using var after = await TenantDbAsync(organization.Id);
        (await after.TelemetrySnapshots.CountAsync(s => s.JobId == job.Id, Ct)).Should().Be(3);
    }

    [Fact]
    public async Task With_the_assistive_technology_switch_the_sessions_telemetry_is_deleted_and_never_read()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("engineer", ["GB"]));
        var job = jobs[0];
        using var client = fixture.CreateHttpClient("api");
        var token = await TokenAsync(client, organization, job);
        await PostAsync(client, organization, job, Snapshot(token, fieldId: "notice_period", pasted: 77, pastes: 1));

        var applicationId = await ApplyAsync(client, organization, job, "ari.moss@example.com", token, Snapshot(token, fieldId: "fullName", typed: 8), accommodation: true);
        await AssessedAsync(organization.Id, applicationId);

        await using var db = await TenantDbAsync(organization.Id);
        (await db.TelemetrySnapshots.CountAsync(s => s.JobId == job.Id, Ct)).Should().Be(0);
        (await db.IntegritySignals.CountAsync(s => s.ApplicationId == applicationId && s.Category == SignalCategory.Behavioural, Ct)).Should().Be(0);
        var run = await db.IntegrityAnalyzerRuns.SingleAsync(r => r.ApplicationId == applicationId && r.Analyzer == "behavioural", Ct);
        run.Note.Should().Be(BehaviouralAnalyzer.NoTelemetryNote, "the run reads the same as one with no telemetry at all");
    }

    [Fact]
    public async Task With_the_assistive_technology_switch_no_device_check_or_markers_are_kept_whatever_is_sent()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("uk", ["GB"]));
        using var client = fixture.CreateHttpClient("api");

        var accommodated = await ApplyAsync(
            client, organization, jobs[0], "ari.moss@example.com", accommodation: true, fingerprint: Fingerprint('d'), markers: ["webdriver"]);
        var measured = await ApplyAsync(
            client, organization, jobs[0], "kim.lowe@example.com", fingerprint: Fingerprint('e'), markers: ["webdriver", "my-own-marker"]);

        await using var db = await TenantDbAsync(organization.Id);
        var off = await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == accommodated, Ct);
        off.DeviceHash.Should().BeNull();
        off.AutomationMarkers.Should().BeEmpty();
        var on = await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == measured, Ct);
        on.DeviceHash.Should().NotBeNull();
        on.AutomationMarkers.Should().Equal("webdriver"); // only the module's fixed list
    }

    [Fact]
    public async Task Where_the_job_needs_consent_a_device_fingerprint_is_kept_only_with_the_tick()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("germany", ["DE"]), ("uk", ["GB"]));
        using var client = fixture.CreateHttpClient("api");

        var withoutTick = await ApplyAsync(client, organization, jobs[0], "noor.haddad@example.com", fingerprint: Fingerprint('a'));
        var withTick = await ApplyAsync(client, organization, jobs[0], "lee.carter@example.com", fingerprint: Fingerprint('b'), deviceCheckConsent: true);
        var noticeOnly = await ApplyAsync(client, organization, jobs[1], "kim.lowe@example.com", fingerprint: Fingerprint('c'));

        await using var db = await TenantDbAsync(organization.Id);
        async Task<string?> DeviceHashAsync(Guid id) => (await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == id, Ct)).DeviceHash;
        (await DeviceHashAsync(withoutTick)).Should().BeNull();
        (await DeviceHashAsync(withTick)).Should().NotBeNull().And.NotBe(Fingerprint('b'), "it is stored keyed-hashed, never as sent");
        (await DeviceHashAsync(noticeOnly)).Should().NotBeNull();
    }

    [Fact]
    public async Task Telemetry_is_purged_once_its_retention_ends()
    {
        var (organization, jobs) = await OrganizationWithJobsAsync(("engineer", ["GB"]));
        var job = jobs[0];
        using var client = fixture.CreateHttpClient("api");
        await PostAsync(client, organization, job, Snapshot(await TokenAsync(client, organization, job)));

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        // What the daily sweep sends for this organization, two days on: the unsent form's snapshot has expired.
        await sender.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new PurgeOrganizationIntegrityData(organization.Id, DateTimeOffset.UtcNow.AddDays(2)));

        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await TenantDbAsync(organization.Id);
                return !await db.TelemetrySnapshots.AnyAsync(s => s.JobId == job.Id, Ct);
            },
            "the worker to purge the expired telemetry",
            Ct);
    }

    private static JsonObject Snapshot(
        string token,
        Guid? pageViewId = null,
        int sequence = 1,
        string fieldId = "notice_period",
        int typed = 9,
        int pasted = 0,
        int pastes = 0,
        string marker = "webdriver") => new()
    {
        ["sessionToken"] = token,
        ["pageViewId"] = (pageViewId ?? Guid.CreateVersion7()).ToString(),
        ["sequence"] = sequence,
        ["fields"] = new JsonArray(new JsonObject
        {
            ["fieldId"] = fieldId,
            ["pasteCount"] = pastes,
            ["pastedCharacters"] = pasted,
            ["typedCharacters"] = typed,
            ["focusCount"] = 1,
            ["blurCount"] = 1,
            ["activeMilliseconds"] = 4000,
            ["autofilled"] = false,
        }),
        ["page"] = new JsonObject { ["visibilityChanges"] = 0, ["automationMarkers"] = new JsonArray(marker) },
    };

    private static string Fingerprint(char digit) => new(digit, 64);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Organization organization, Job job, JsonObject snapshot) =>
        await client.PostAsJsonAsync($"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/telemetry", snapshot, Ct);

    private static async Task<JsonElement> FormAsync(HttpClient client, Organization organization, Job job) =>
        await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{organization.Slug}/jobs/{job.Slug}/application-form", Ct);

    private static async Task<string> TokenAsync(HttpClient client, Organization organization, Job job) =>
        (await FormAsync(client, organization, job)).At("telemetry.sessionToken").GetString()!;

    // Applies as the careers server does: the resume upload, then the submission with the session and the last page's
    // snapshot.
    private static async Task<Guid> ApplyAsync(
        HttpClient client,
        Organization organization,
        Job job,
        string email,
        string? token = null,
        JsonObject? final = null,
        bool accommodation = false,
        string? fingerprint = null,
        bool deviceCheckConsent = false,
        string[]? markers = null)
    {
        var prefix = $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}";
        var ticketResponse = await client.PostAsJsonAsync($"{prefix}/questions/resume/uploads", new { fileName = "cv.pdf", sizeBytes = Pdf.Length }, Ct);
        ticketResponse.StatusCode.Should().Be(HttpStatusCode.OK, await ticketResponse.Content.ReadAsStringAsync(Ct));
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        using (var storage = new HttpClient())
        using (var put = new HttpRequestMessage(HttpMethod.Put, ticket.GetProperty("uploadUrl").GetString()) { Content = new ByteArrayContent(Pdf) })
        {
            foreach (var header in ticket.GetProperty("headers").EnumerateObject())
            {
                put.Headers.Add(header.Name, header.Value.GetString());
            }

            (await storage.SendAsync(put, Ct)).IsSuccessStatusCode.Should().BeTrue();
        }

        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        (await client.PostAsync($"{prefix}/questions/resume/uploads/{uploadId}/verify", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Casey Morgan", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-10-20.3",
            ["answers"] = new JsonArray(
                new JsonObject { ["questionId"] = "resume", ["value"] = uploadId.ToString() },
                new JsonObject { ["questionId"] = "work_authorization", ["value"] = "yes" },
                new JsonObject { ["questionId"] = "experience", ["value"] = "six_to_nine" },
                new JsonObject { ["questionId"] = "notice_period", ["value"] = "Three months, but I can start sooner if the team needs it." }),
            ["aiUseDeclaration"] = "DidNotUse",
            ["accommodationRequested"] = accommodation,
            ["deviceFingerprint"] = fingerprint,
            ["automationMarkers"] = new JsonArray([.. (markers ?? []).Select(m => JsonValue.Create(m))]),
            ["deviceCheckConsent"] = deviceCheckConsent,
            ["telemetrySessionToken"] = token,
            ["telemetry"] = final,
        };
        using var response = await client.PostAsJsonAsync($"{prefix}/applications", body, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    // Both current assessments, once the worker has made them.
    private async Task<IReadOnlyList<IntegrityAssessment>> AssessedAsync(Guid organizationId, Guid applicationId)
    {
        List<IntegrityAssessment> assessments = [];
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await TenantDbAsync(organizationId);
                assessments = await db.IntegrityAssessments.AsNoTracking().Where(a => a.ApplicationId == applicationId).ToListAsync(Ct);
                return assessments.Count == 2;
            },
            "the worker to assess the application",
            Ct);
        return assessments;
    }

    // An organization with one open job per (slug, countries), each copied from the "Software engineer" template.
    private async Task<(Organization Organization, IReadOnlyList<Job> Jobs)> OrganizationWithJobsAsync(params (string Slug, string[] Countries)[] specs)
    {
        HiringTemplateVersion engineering;
        await using (var db = await TenantDbAsync(Guid.NewGuid()))
        {
            engineering = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct);
        }

        var jobs = new List<Job>();
        var organization = await fixture.CreateOrganizationAsync(
            org => [.. specs.Select(spec =>
            {
                var job = TestData.NewJob(org, spec.Slug);
                job.ApplicantLocationRequirements = [.. spec.Countries];
                job.UseTemplate(engineering);
                job.Publish(DateTimeOffset.UtcNow);
                jobs.Add(job);
                return job;
            })],
            Ct);
        return (organization, jobs);
    }

    private async Task<ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
