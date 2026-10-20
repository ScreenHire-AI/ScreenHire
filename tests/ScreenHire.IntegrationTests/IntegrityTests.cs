using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The integrity engine end to end (docs/architecture/integrity-engine.md): a careers submission becomes two assessments
/// readable through the API, with reason codes, evidence, a summary and a next step; reviewers dismiss and add notes,
/// and every action is in the audit log; raw network data expires; the tables are tenant-scoped and append-only. The
/// applicants are the synthetic ones the golden files use, submitted over HTTP the way the careers server does, with
/// their address in <c>X-Forwarded-For</c> (the API trusts it from loopback).
/// </summary>
public sealed class IntegrityTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bot_submission_is_assessed_with_reasons_evidence_a_summary_and_a_next_step()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);

        var integrity = await AssessedAsync(tenant, applicationId);

        integrity.Text("status").Should().Be("Assessed");
        var authenticity = integrity.At("authenticity");
        authenticity.GetProperty("level").GetString().Should().Be("VerificationRecommended");
        authenticity.GetProperty("reasonCodes").EnumerateArray().Select(c => c.GetString()).Should().Contain(
            [SignalCatalogue.Honeypot, SignalCatalogue.Webdriver, SignalCatalogue.DatacenterIp, SignalCatalogue.DisposableEmail, SignalCatalogue.SubmitVelocity]);
        authenticity.GetProperty("summary").GetString().Should().StartWith("6 observations from network, identity and automation checks");
        authenticity.GetProperty("nextStep").GetString().Should().Contain("not a reason to reject");
        authenticity.GetProperty("rulesetVersion").GetString().Should().Be(IntegrityRuleset.Version);
        var datacenter = authenticity.GetProperty("signals").EnumerateArray().Single(s => s.GetProperty("code").GetString() == SignalCatalogue.DatacenterIp);
        datacenter.GetProperty("countsTowardLevel").GetBoolean().Should().BeTrue();
        datacenter.GetProperty("evidence").EnumerateArray().Should().Contain(e =>
            e.GetProperty("kind").GetString() == "Fact" && e.GetProperty("value").GetString() == "Example Cloud Hosting (AS64497)");

        var ai = integrity.At("aiAssistance");
        ai.GetProperty("level").GetString().Should().Be("NotIndicated");
        ai.GetProperty("policy").GetString().Should().Be("AllowedWithDisclosure");

        // Evidence, not verdicts: no score, no percentage, and never the raw address.
        var raw = integrity.Body.GetRawText();
        raw.Should().NotContainEquivalentOf("score").And.NotContain("%").And.NotContain(SyntheticApplicants.Bot.IpAddress);

        // The level reaches the list's chips and the timeline the workflow engine reads.
        var list = await tenant.GetAsync($"/v1/jobs/{jobId}/applications");
        list.At("0.integrity.authenticity").GetString().Should().Be("VerificationRecommended");
        var timeline = await tenant.GetAsync($"/v1/applications/{applicationId}/timeline");
        timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().Contain("IntegrityAssessed");
    }

    [Fact]
    public async Task A_genuine_applicant_has_no_concerns_and_their_raw_network_data_is_stored_to_expire()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman);

        var integrity = await SettledAsync(tenant, applicationId);

        integrity.Text("authenticity.level").Should().Be("NoConcerns");
        integrity.At("authenticity.reasonCodes").GetArrayLength().Should().Be(0);
        integrity.Text("authenticity.nextStep").Should().Be("No action needed.");

        await using var db = await TenantDbAsync(tenant.OrganizationId);
        var observation = await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == applicationId, Ct);
        observation.IpAddress.Should().Be(SyntheticApplicants.GenuineHuman.IpAddress);
        observation.UserAgent.Should().Be(SyntheticApplicants.GenuineHuman.UserAgent);
        observation.DeviceHash.Should().HaveLength(64).And.NotContain("robin", "the fingerprint is stored only as an organization-salted hash");
        observation.ExpiresAt.Should().BeCloseTo(observation.ObservedAt.AddDays(30), TimeSpan.FromSeconds(1));
        (await db.IntegrityAnalyzerRuns.Where(r => r.ApplicationId == applicationId).Select(r => r.Analyzer).ToListAsync(Ct))
            .Should().BeEquivalentTo(IntegritySupport.Analyzers);
    }

    [Fact]
    public async Task An_application_farm_shares_details_within_the_organization()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        await ApplyAsync(slug, jobId, SyntheticApplicants.ApplicationFarm[0]);
        var second = await ApplyAsync(slug, jobId, SyntheticApplicants.ApplicationFarm[1]);

        var integrity = await AssessedAsync(tenant, second);

        integrity.Text("authenticity.level").Should().Be("VerificationRecommended");
        var duplicate = integrity.At("authenticity.signals").EnumerateArray().Single(s => s.GetProperty("code").GetString() == SignalCatalogue.DuplicateInOrganization);
        duplicate.GetProperty("weight").GetString().Should().Be("High");
        duplicate.GetProperty("evidence").EnumerateArray().Select(e => e.GetProperty("label").GetString())
            .Should().Contain(["Same device", "Same resume file", "Same phone number", "Same email address (ignoring a plus tag)"]);
    }

    [Fact]
    public async Task Reviewers_dismiss_with_a_reason_and_add_notes_and_every_action_is_logged()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var bot = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        var genuine = await ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman);
        var integrity = await AssessedAsync(tenant, bot);
        var assessmentId = integrity.Id("authenticity.id");
        var dismiss = $"/v1/applications/{bot}/integrity/assessments/{assessmentId}/dismiss";

        (await tenant.PostAsync(dismiss, new JsonObject { ["reason"] = "Other" })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("note");
        var dismissed = (await tenant.PostAsync(dismiss, new JsonObject { ["reason"] = "VerifiedOutsideScreenHire", ["note"] = "Video call on Monday." }))
            .Expect(HttpStatusCode.OK);
        (await tenant.PostAsync(dismiss, new JsonObject { ["reason"] = "FalsePositive" })).Expect(HttpStatusCode.Conflict);
        var noted = (await tenant.PostAsync($"/v1/applications/{bot}/integrity/notes", new JsonObject { ["note"] = "Asked for references." }))
            .Expect(HttpStatusCode.OK);
        (await tenant.PostAsync($"/v1/applications/{bot}/integrity/request-verification")).Expect(HttpStatusCode.Conflict);

        dismissed.Text("authenticity.dismissal.reason").Should().Be("VerifiedOutsideScreenHire");
        dismissed.Id("authenticity.dismissal.actorUserId").Should().Be(tenant.UserId);
        dismissed.Text("authenticity.level").Should().Be("VerificationRecommended", "dismissing records a decision; it never rewrites the evidence");
        var log = noted.At("reviewLog").EnumerateArray().ToList();
        log.Select(e => e.GetProperty("type").GetString()).Should().Equal("Dismissed", "NoteAdded");
        log[0].GetProperty("assessmentKind").GetString().Should().Be("Authenticity");
        log[1].GetProperty("note").GetString().Should().Be("Asked for references.");
        noted.At("requestVerification.available").GetBoolean().Should().BeFalse();

        // Nothing to dismiss at the lowest level.
        var clear = await AssessedAsync(tenant, genuine);
        (await tenant.PostAsync(
            $"/v1/applications/{genuine}/integrity/assessments/{clear.Id("authenticity.id")}/dismiss", new JsonObject { ["reason"] = "FalsePositive" }))
            .Expect(HttpStatusCode.Conflict);

        // Two reviewers at once can both pass the endpoint's check; the database still keeps one dismissal.
        await using var db = await TenantDbAsync(tenant.OrganizationId);
        var assessment = await db.IntegrityAssessments.AsNoTracking().SingleAsync(a => a.Id == assessmentId, Ct);
        db.IntegrityReviewActions.Add(IntegrityReviewAction.Dismiss(assessment, DismissReason.FalsePositive, null, tenant.UserId, DateTimeOffset.UtcNow));
        var second = () => db.SaveChangesAsync(Ct);
        (await second.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Another_organization_sees_and_changes_nothing()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        var integrity = await AssessedAsync(tenant, applicationId);

        (await other.GetAsync($"/v1/applications/{applicationId}/integrity")).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync(
            $"/v1/applications/{applicationId}/integrity/assessments/{integrity.Id("authenticity.id")}/dismiss",
            new JsonObject { ["reason"] = "FalsePositive" })).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/applications/{applicationId}/integrity/notes", new JsonObject { ["note"] = "x" })).Expect(HttpStatusCode.NotFound);

        await using var theirs = await TenantDbAsync(other.OrganizationId);
        (await theirs.IntegritySignals.AnyAsync(s => s.ApplicationId == applicationId, Ct)).Should().BeFalse();
        (await theirs.IntegrityAssessments.AnyAsync(a => a.ApplicationId == applicationId, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicates_are_never_matched_across_organizations()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        var (firstSlug, firstJob) = await OpenJobAsync(first);
        var (secondSlug, secondJob) = await OpenJobAsync(second);
        await ApplyAsync(firstSlug, firstJob, SyntheticApplicants.ApplicationFarm[0]);
        var other = await ApplyAsync(secondSlug, secondJob, SyntheticApplicants.ApplicationFarm[1]);

        var integrity = await AssessedAsync(second, other);

        integrity.At("authenticity.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().NotContain(SignalCatalogue.DuplicateInOrganization, "the same device, file and phone at another employer is not a match");
    }

    [Fact]
    public async Task Row_level_security_itself_hides_and_refuses_another_organizations_integrity_rows()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        await SettledAsync(tenant, applicationId);
        (await tenant.PostAsync($"/v1/applications/{applicationId}/integrity/notes", new JsonObject { ["note"] = "Checked the references." }))
            .Expect(HttpStatusCode.OK);
        string[] tables =
        [
            "integrity_analyzer_runs", "integrity_signals", "integrity_assessments", "integrity_review_actions", "submission_observations",
            "application_documents", "consistency_checks",
        ];

        // Raw SQL as the application role, past the EF query filter: only the policies stand in the way.
        foreach (var table in tables)
        {
            (await CountAsync(tenant.OrganizationId, $"SELECT count(*) FROM {table} WHERE application_id = @id", applicationId)).Should().BeGreaterThan(0, table);
            (await CountAsync(other.OrganizationId, $"SELECT count(*) FROM {table} WHERE application_id = @id", applicationId)).Should().Be(0, table);
        }

        var forged = () => CountAsync(
            other.OrganizationId,
            "INSERT INTO integrity_review_actions (id, organization_id, application_id, type, note, actor_user_id, occurred_at) " +
            "VALUES (gen_random_uuid(), @organization, @id, 'NoteAdded', 'x', gen_random_uuid(), now()) RETURNING 1",
            applicationId, tenant.OrganizationId);
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task With_the_assistive_technology_switch_no_timing_is_stored_and_the_honeypot_does_not_count()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot, accommodation: true);

        var integrity = await AssessedAsync(tenant, applicationId);

        await using var db = await TenantDbAsync(tenant.OrganizationId);
        (await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == applicationId, Ct)).FormStartedAt.Should().BeNull();
        var codes = integrity.At("authenticity.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        codes.Should().NotContain(SignalCatalogue.SubmitVelocity).And.NotContain(SignalCatalogue.Honeypot);
        integrity.Body.GetRawText().Should().NotContainEquivalentOf("accommodation").And.NotContainEquivalentOf("assistive");
    }

    [Fact]
    public async Task The_integrity_api_offers_no_reject_action()
    {
        using var client = fixture.CreateHttpClient("api");

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        var routes = document.GetProperty("paths").EnumerateObject().Where(p => p.Name.Contains("/integrity", StringComparison.Ordinal)).ToList();
        routes.Should().NotBeEmpty();
        routes.Should().AllSatisfy(r => r.Name.Should().NotContainEquivalentOf("reject"));
        routes.SelectMany(r => r.Value.EnumerateObject()).Select(o => o.Value.GetProperty("operationId").GetString())
            .Should().NotContain(id => id!.Contains("Reject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_recruiter_added_application_is_assessed_without_any_network_data()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (_, jobId) = await OpenJobAsync(tenant);
        var applicationId = (await tenant.CreateApplicationAsync(jobId, "dana.frost@example.com", "Dana Frost")).Id();

        var integrity = await AssessedAsync(tenant, applicationId);

        integrity.Text("authenticity.level").Should().Be("NoConcerns");
        await using var db = await TenantDbAsync(tenant.OrganizationId);
        (await db.SubmissionObservations.AnyAsync(o => o.ApplicationId == applicationId, Ct)).Should().BeFalse("a recruiter's own address is never recorded for a candidate");
        (await db.IntegrityAnalyzerRuns.SingleAsync(r => r.ApplicationId == applicationId && r.Analyzer == "network", Ct))
            .Note.Should().Be("No network address was recorded for this application.");
    }

    [Fact]
    public async Task Raw_network_data_is_deleted_once_its_retention_ends_and_the_signals_stay()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        await AssessedAsync(tenant, applicationId);

        // The sender stays up until the purge is seen: disposing it drops its private schema, and with it any envelope
        // not yet on the shared queue.
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        // What the daily sweep sends for this organization, 31 days on.
        await sender.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new PurgeOrganizationIntegrityData(tenant.OrganizationId, DateTimeOffset.UtcNow.AddDays(31)));

        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await TenantDbAsync(tenant.OrganizationId);
                return !await db.SubmissionObservations.AnyAsync(o => o.ApplicationId == applicationId, Ct);
            },
            "the worker to purge the expired observation",
            Ct);
        await using var after = await TenantDbAsync(tenant.OrganizationId);
        (await after.IntegritySignals.CountAsync(s => s.ApplicationId == applicationId, Ct)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Running_the_pipeline_again_changes_nothing_and_only_a_new_analyzer_version_runs()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        await SettledAsync(tenant, applicationId);
        await using var db = await TenantDbAsync(tenant.OrganizationId);
        var ran = await db.IntegrityAnalyzerRuns.AsNoTracking().Where(r => r.ApplicationId == applicationId)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion }).ToListAsync(Ct);
        var before = await RecordAsync(db, applicationId);

        // A redelivery: the analyzer versions that already ran. Each stand-in fails the test if the pipeline calls it.
        IIntegrityAnalyzer[] same = [.. ran.Select(r => new StandInAnalyzer(r.Analyzer, r.AnalyzerVersion))];
        var redelivered = await new IntegrityPipeline(db, same, TimeProvider.System).RunAsync(applicationId, Ct);
        await db.SaveChangesAsync(Ct);

        redelivered.Should().BeEmpty("nothing changed, so there is no new assessment to announce");
        (await RecordAsync(db, applicationId)).Should().Be(before);

        // A new analyzer runs alone. Reporting nothing, it leaves the assessments as they were.
        var quiet = new StandInAnalyzer("review-probe", 1, AnalyzerResult.Nothing("Nothing to report."));
        var afterQuiet = await new IntegrityPipeline(db, [.. same, quiet], TimeProvider.System).RunAsync(applicationId, Ct);
        await db.SaveChangesAsync(Ct);

        quiet.Calls.Should().Be(1);
        afterQuiet.Should().BeEmpty();
        (await RecordAsync(db, applicationId)).Should().Be(before with { Runs = before.Runs + 1 });

        // Its next version runs once more and, with a new signal, reassesses and announces it.
        var observed = new ObservedSignal(
            SignalCatalogue.Inconsistency, SignalConfidence.Medium, [SignalEvidence.Fact("Probe", "A test observation.")],
            new Dictionary<string, string> { ["detail"] = "A test observation." });
        var bumped = new StandInAnalyzer("review-probe", 2, new AnalyzerResult([observed]));
        var afterBump = await new IntegrityPipeline(db, [.. same, quiet, bumped], TimeProvider.System).RunAsync(applicationId, Ct);
        await db.SaveChangesAsync(Ct);

        quiet.Calls.Should().Be(1);
        bumped.Calls.Should().Be(1);
        afterBump.OfType<IntegrityAssessed>().Should().ContainSingle();
        var after = await RecordAsync(db, applicationId);
        after.Runs.Should().Be(before.Runs + 2);
        after.Signals.Should().Be(before.Signals + 1);
        after.Assessments.Should().BeGreaterThan(before.Assessments);
        after.Timeline.Should().Be(before.Timeline + 1);
    }

    [Fact]
    public async Task The_application_role_cannot_change_the_record_or_touch_the_signal_network()
    {
        using var tenant = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await OpenJobAsync(tenant);
        var applicationId = await ApplyAsync(slug, jobId, SyntheticApplicants.Bot);
        await SettledAsync(tenant, applicationId);
        await using var db = await TenantDbAsync(tenant.OrganizationId);

        var update = () => db.IntegrityAssessments.Where(a => a.ApplicationId == applicationId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Level, "NoConcerns"), Ct);
        var delete = () => db.IntegritySignals.Where(s => s.ApplicationId == applicationId).ExecuteDeleteAsync(Ct);
        var network = () => db.SignalNetworkEntries.CountAsync(Ct);
        // What a file said is a record too: extraction writes it once and nothing rewrites or removes it.
        var rewrite = () => db.ApplicationDocuments.Where(d => d.ApplicationId == applicationId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Text, "edited"), Ct);
        var forget = () => db.ConsistencyChecks.Where(c => c.ApplicationId == applicationId).ExecuteDeleteAsync(Ct);

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await network.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await rewrite.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await forget.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    // A published remote job with the default workflow, in the tenant's organization, and the organization's slug.
    private Task<(string Slug, Guid JobId)> OpenJobAsync(Tenant tenant) => IntegritySupport.OpenJobAsync(fixture, tenant);

    private Task<Guid> ApplyAsync(string slug, Guid jobId, SyntheticApplicant applicant, bool accommodation = false) =>
        IntegritySupport.ApplyAsync(fixture, slug, jobId, applicant, accommodation);

    private static Task<ApiResult> AssessedAsync(Tenant tenant, Guid applicationId) => IntegritySupport.AssessedAsync(tenant, applicationId);

    // Every analyzer has run, content forensics included, so row counts no longer change underneath a test.
    private Task<ApiResult> SettledAsync(Tenant tenant, Guid applicationId) => IntegritySupport.SettledAsync(fixture, tenant, applicationId);

    // One scalar statement as the application role with the organization as tenant, in a transaction like the API's.
    private async Task<long> CountAsync(Guid organizationId, string sql, Guid applicationId, Guid? organization = null)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction))
        {
            setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", applicationId);
        if (organization is { } value)
        {
            command.Parameters.AddWithValue("organization", value);
        }

        var result = Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
        await transaction.RollbackAsync(Ct);
        return result;
    }

    private Task<ScreenHireDbContext> TenantDbAsync(Guid organizationId) => IntegritySupport.TenantDbAsync(fixture, organizationId);

    // What the pipeline has written for one application.
    private static async Task<PipelineRecord> RecordAsync(ScreenHireDbContext db, Guid applicationId) => new(
        await db.IntegrityAnalyzerRuns.CountAsync(r => r.ApplicationId == applicationId, Ct),
        await db.IntegritySignals.CountAsync(s => s.ApplicationId == applicationId, Ct),
        await db.IntegrityAssessments.CountAsync(a => a.ApplicationId == applicationId, Ct),
        await db.ApplicationEvents.CountAsync(e => e.ApplicationId == applicationId && e.Type == ApplicationEventType.IntegrityAssessed, Ct));

    private sealed record PipelineRecord(int Runs, int Signals, int Assessments, int Timeline);

    // An analyzer with a given name and version. Without a result it must never run.
    private sealed class StandInAnalyzer(string name, int version, AnalyzerResult? result = null) : IIntegrityAnalyzer
    {
        public string Name => name;

        public int Version => version;

        public int Calls { get; private set; }

        public Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return result is null
                ? Task.FromException<AnalyzerResult>(new InvalidOperationException($"{name} version {version} already ran and must not run again."))
                : Task.FromResult(result);
        }
    }
}
