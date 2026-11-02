using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Verification;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Domain.Verification;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using static ScreenHire.IntegrationTests.CandidateTestSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The duplicate check run again over an earlier application (verification-engine.md §10.2): when a later application
/// from another candidate shares a detail with it, the earlier one carries the same evidence, as a new revision of its
/// duplicate run, and its review log says so in words that name neither the other candidate nor their job. Every test
/// settles the earlier application before the next one arrives: a re-check of an application that has not run its
/// duplicate check yet does nothing, because that first run sees everything there is.
/// </summary>
public sealed class VerificationRecheckTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_later_member_of_a_farm_gives_the_earlier_ones_a_new_revision_and_one_current_duplicate_signal()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var otherJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var first = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, first);
        var second = await fixture.ApplyAsync(slug, otherJob, SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, second);
        await WaitForReviewEntryAsync(owner, first, "EvidenceChanged");
        var third = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[2].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, third);

        var firstLog = await WaitForReviewEntryAsync(owner, first, "EvidenceChanged", count: 2, alsoHolds: v => OneDuplicateSignal(v));
        var secondLog = await WaitForReviewEntryAsync(owner, second, "EvidenceChanged", alsoHolds: v => OneDuplicateSignal(v));
        var thirdLog = await VerificationSupport.AssessedAsync(owner, third);

        ReviewEntries(firstLog, "EvidenceChanged").Select(e => e.GetProperty("note").GetString()).Should().SatisfyRespectively(
            n => n.Should().StartWith("New evidence: ").And.Contain("1 other application"),
            n => n.Should().StartWith("New evidence: ").And.Contain("2 other applications"));
        ReviewEntries(secondLog, "EvidenceChanged").Select(e => e.GetProperty("note").GetString()).Should().ContainSingle()
            .Which.Should().StartWith("New evidence: ").And.Contain("2 other applications", "its own first run saw one, the third member made it two");
        ReviewEntries(thirdLog, "EvidenceChanged").Should().BeEmpty("the last member's own run saw everyone");
        foreach (var log in new[] { firstLog, secondLog })
        {
            AuthenticityCodes(log).Count(c => c == SignalCatalogue.DuplicateInOrganization).Should().Be(1, "only the latest revision speaks for the analyzer");
            ReviewEntries(log, "EvidenceChanged").Should().AllSatisfy(e => e.GetProperty("actorUserId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null));
            log.At("reviewLog").GetRawText().Should().NotContain("Data Analyst").And.NotContainEquivalentOf("talentdesk");
        }

        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == first && r.Analyzer == "duplicates").Select(r => r.Revision).ToListAsync(Ct))
            .Should().BeEquivalentTo([1, 2, 3]);
        (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == second && r.Analyzer == "duplicates").Select(r => r.Revision).ToListAsync(Ct))
            .Should().BeEquivalentTo([1, 2]);
        (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == third && r.Analyzer == "duplicates").Select(r => r.Revision).ToListAsync(Ct))
            .Should().BeEquivalentTo([1]);
        (await db.VerificationSignals.Where(s => s.ApplicationId == first && s.Source == "duplicates").Select(s => s.SourceRevision).ToListAsync(Ct))
            .Should().BeEquivalentTo([2, 3], "revision 1 found nobody, so only revisions 2 and 3 stored a signal");
    }

    [Fact]
    public async Task Checking_the_same_pair_again_changes_nothing_in_process_or_through_the_queue()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var first = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, first);
        var second = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, second);
        await WaitForReviewEntryAsync(owner, first, "EvidenceChanged");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var before = await RecordAsync(db, first);
        before.Should().Be(new Record(Runs: 2, Signals: 1, Entries: 1), "revision 1 found nobody and revision 2 found the second member");

        // In process: the analyzer finds what its latest run found, so the pipeline writes nothing and announces nothing.
        var pipeline = new VerificationPipeline(db, [new DuplicateAnalyzer(new DatabaseApplicantDirectory(db))], TimeProvider.System);
        var announced = await pipeline.RerunAsync(first, "duplicates", Ct);
        await db.SaveChangesAsync(Ct);
        announced.Should().BeEmpty();
        (await RecordAsync(db, first)).Should().Be(before);

        // Through the queue: the same pair again (the durable queue drops it), then a different cause for the same
        // application, which the handler does run. Once that one is handled, nothing has moved.
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new RecheckApplicationDuplicates(owner.OrganizationId, first, second));
        var otherCause = Guid.NewGuid();
        await bus.PublishAsync(new RecheckApplicationDuplicates(owner.OrganizationId, first, otherCause));
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(otherCause.ToString(), Ct, ".RecheckApplicationDuplicates") > 0,
            "the worker to handle the second re-check",
            Ct);

        (await RecordAsync(db, first)).Should().Be(before);
        (await store.GetDeadLettersAsync(first.ToString(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_re_check_sent_under_another_organization_changes_nothing_and_is_not_dead_lettered()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var first = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, first);
        var second = await fixture.ApplyAsync(slug, job, SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, owner, second);
        await WaitForReviewEntryAsync(owner, first, "EvidenceChanged");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);
        var before = await RecordAsync(db, first);

        // Another organization's tenant context with this organization's application: row-level security shows it nothing.
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var cause = Guid.NewGuid();
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new RecheckApplicationDuplicates(other.OrganizationId, first, cause));
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(cause.ToString(), Ct, ".RecheckApplicationDuplicates") > 0,
            "the worker to handle the forged re-check",
            Ct);

        (await RecordAsync(db, first)).Should().Be(before);
        (await store.GetDeadLettersAsync(cause.ToString(), Ct)).Should().BeEmpty("an application the organization cannot see is finished, not failed");
    }

    [Fact]
    public async Task A_farm_in_one_organization_gives_another_organizations_application_no_evidence_and_no_review_entry()
    {
        using var quiet = await fixture.SignedInWithOrganizationAsync();
        using var busy = await fixture.SignedInWithOrganizationAsync();
        var quietSlug = await SlugAsync(quiet);
        var busySlug = await SlugAsync(busy);
        var quietJob = await OpenJobTitledAsync(quiet, "Platform Engineer");
        var busyJob = await OpenJobTitledAsync(busy, "Platform Engineer");
        var alone = await fixture.ApplyAsync(quietSlug, quietJob, SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, quiet, alone);
        var busyFirst = await fixture.ApplyAsync(busySlug, busyJob, SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, busy, busyFirst);
        var busySecond = await fixture.ApplyAsync(busySlug, busyJob, SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember(), Ct);
        await VerificationSupport.SettledAsync(fixture, busy, busySecond);

        // The re-checks in the busy organization have happened, so any that crossed over would have too.
        await WaitForReviewEntryAsync(busy, busyFirst, "EvidenceChanged");

        var verification = await VerificationSupport.SettledAsync(fixture, quiet, alone);
        AuthenticityCodes(verification).Should().NotContain(SignalCatalogue.DuplicateInOrganization);
        ReviewEntries(verification, "EvidenceChanged").Should().BeEmpty();
        await using var db = await VerificationSupport.TenantDbAsync(fixture, quiet.OrganizationId);
        (await db.VerificationAnalyzerRuns.Where(r => r.ApplicationId == alone && r.Analyzer == "duplicates").Select(r => r.Revision).ToListAsync(Ct))
            .Should().Equal(1);
    }

    [Fact]
    public async Task Messages_about_an_application_that_is_gone_are_finished_and_never_dead_lettered()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var gone = Guid.NewGuid();
        var file = Guid.NewGuid();
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new AnalyzeApplicationVerification(owner.OrganizationId, gone));
        await bus.PublishAsync(new ExtractApplicationDocument(owner.OrganizationId, gone, file));
        await bus.PublishAsync(new CheckApplicationConsistency(owner.OrganizationId, gone));
        await bus.PublishAsync(new RecheckApplicationDuplicates(owner.OrganizationId, gone, Guid.NewGuid()));
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);

        foreach (var type in new[] { ".AnalyzeApplicationVerification", ".ExtractApplicationDocument", ".CheckApplicationConsistency", ".RecheckApplicationDuplicates" })
        {
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(gone.ToString(), Ct, type) > 0,
                $"the worker to finish the {type.TrimStart('.')} for an application that is gone",
                Ct);
        }

        (await store.GetDeadLettersAsync(gone.ToString(), Ct)).Should().BeEmpty();
    }

    private static bool OneDuplicateSignal(ApiResult verification) =>
        AuthenticityCodes(verification).Count(c => c == SignalCatalogue.DuplicateInOrganization) == 1;

    // What the pipeline has stored for the duplicate check of one application.
    private static async Task<Record> RecordAsync(Infrastructure.Persistence.ScreenHireDbContext db, Guid applicationId) => new(
        await db.VerificationAnalyzerRuns.CountAsync(r => r.ApplicationId == applicationId && r.Analyzer == "duplicates", Ct),
        await db.VerificationSignals.CountAsync(s => s.ApplicationId == applicationId && s.Source == "duplicates", Ct),
        await db.VerificationReviewActions.CountAsync(a => a.ApplicationId == applicationId && a.Type == VerificationReviewActionType.EvidenceChanged, Ct));

    private sealed record Record(int Runs, int Signals, int Entries);
}
