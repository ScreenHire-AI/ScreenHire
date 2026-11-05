using ScreenHire.Application.Privacy;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.UnitTests.Privacy;

/// <summary>Record retention (docs/architecture/data-requests.md): the setting, its history line and which applications it selects.</summary>
public sealed class RetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddMonths(-12);
    private static readonly DateTimeOffset Old = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset Recent = Cutoff.AddDays(1);

    [Fact]
    public void Retention_is_off_by_default_and_the_cutoff_is_the_period_before_now()
    {
        var settings = new RetentionSettings { OrganizationId = Guid.NewGuid() };

        settings.Cutoff(Now).Should().BeNull();
        settings.Set(18, Now);
        settings.Cutoff(Now).Should().Be(Now.AddMonths(-18));
        settings.UpdatedAt.Should().Be(Now);
        settings.Set(null, Now.AddDays(1));
        settings.Cutoff(Now).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void A_period_outside_1_to_120_months_is_refused(int months)
    {
        var settings = new RetentionSettings { OrganizationId = Guid.NewGuid() };

        settings.Invoking(s => s.Set(months, Now)).Should().Throw<ArgumentOutOfRangeException>();
        RetentionSettings.CutoffFor(months, Now).Should().BeNull();
    }

    [Fact]
    public void The_history_says_when_it_was_turned_on_changed_and_turned_off_and_nothing_for_no_change()
    {
        var off = new RetentionSettings { OrganizationId = Guid.NewGuid() };
        var one = new RetentionSettings { OrganizationId = off.OrganizationId };
        one.Set(1, Now);
        var eighteen = new RetentionSettings { OrganizationId = off.OrganizationId };
        eighteen.Set(18, Now);

        Sentences(SettingsChanges.Retention(null, eighteen)).Should().Equal("turned on deleting ended applications 18 months after they end");
        Sentences(SettingsChanges.Retention(null, one)).Should().Equal("turned on deleting ended applications 1 month after they end");
        Sentences(SettingsChanges.Retention(eighteen, one)).Should().Equal("changed when ended applications are deleted to 1 month after they end");
        Sentences(SettingsChanges.Retention(eighteen, off)).Should().Equal("turned off deleting ended applications");
        SettingsChanges.Retention(null, off).Should().BeEmpty("saving it off when it was never on changes nothing");
        SettingsChanges.Retention(eighteen, eighteen).Should().BeEmpty();
        SettingsChanges.Retention(eighteen, null).Should().BeEmpty();
    }

    [Fact]
    public void A_hired_rejected_or_withdrawn_application_is_selected_once_it_entered_that_stage_before_the_cutoff()
    {
        var candidate = Guid.NewGuid();
        var applications = new[]
        {
            App(candidate, StageKind.Hired, Old),
            App(candidate, StageKind.Rejected, Old),
            App(candidate, StageKind.Withdrawn, Old),
            App(candidate, StageKind.Rejected, Recent),
            App(candidate, StageKind.Screening, Old),
        };

        var selected = RetentionSelection.Select(applications, new HashSet<Guid>(), Cutoff);

        selected.ApplicationIds.Should().BeEquivalentTo(applications.Take(3).Select(a => a.ApplicationId));
        selected.CandidateCount.Should().Be(1);
    }

    [Fact]
    public void A_candidate_with_a_live_application_loses_only_the_ended_one()
    {
        var candidate = Guid.NewGuid();
        var ended = App(candidate, StageKind.Rejected, Old);
        var live = App(candidate, StageKind.Screening, Recent);

        RetentionSelection.Select([ended, live], new HashSet<Guid>(), Cutoff).ApplicationIds.Should().Equal(ended.ApplicationId);
    }

    [Fact]
    public void A_saved_candidate_is_skipped_entirely_whatever_their_applications_look_like()
    {
        var saved = Guid.NewGuid();
        var other = Guid.NewGuid();
        var applications = new[] { App(saved, StageKind.Rejected, Old), App(saved, StageKind.Hired, Old), App(other, StageKind.Rejected, Old) };

        var selected = RetentionSelection.Select(applications, new HashSet<Guid> { saved }, Cutoff);

        selected.ApplicationIds.Should().Equal(applications[2].ApplicationId);
        selected.CandidateCount.Should().Be(1);
    }

    [Fact]
    public void An_application_the_team_kept_in_mind_expires_from_that_time_once_its_candidate_is_no_longer_saved()
    {
        var candidate = Guid.NewGuid();
        var old = App(candidate, StageKind.Screening, Recent) with { KeptInMindAt = Old };
        var recent = App(Guid.NewGuid(), StageKind.Screening, Recent) with { KeptInMindAt = Recent };

        RetentionSelection.Select([old, recent], new HashSet<Guid>(), Cutoff).ApplicationIds.Should().Equal(old.ApplicationId);
        RetentionSelection.Select([old, recent], new HashSet<Guid> { candidate }, Cutoff).ApplicationIds.Should().BeEmpty();
    }

    [Fact]
    public void An_application_of_a_job_that_closed_before_the_cutoff_is_selected_and_a_job_closed_with_no_time_is_not()
    {
        var closedLongAgo = App(Guid.NewGuid(), StageKind.Screening, Recent) with { JobStatus = JobStatus.Closed, JobClosedAt = Old };
        var closedRecently = App(Guid.NewGuid(), StageKind.Screening, Recent) with { JobStatus = JobStatus.Closed, JobClosedAt = Recent };
        var closedWithoutTime = App(Guid.NewGuid(), StageKind.Screening, Recent) with { JobStatus = JobStatus.Closed, JobClosedAt = null };
        var open = App(Guid.NewGuid(), StageKind.Screening, Recent);

        RetentionSelection.Select([closedLongAgo, closedRecently, closedWithoutTime, open], new HashSet<Guid>(), Cutoff).ApplicationIds
            .Should().Equal(closedLongAgo.ApplicationId);
    }

    [Fact]
    public void The_selection_counts_each_candidate_once_and_orders_by_id()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var applications = new[] { App(first, StageKind.Rejected, Old), App(first, StageKind.Hired, Old), App(second, StageKind.Hired, Old) };

        var selected = RetentionSelection.Select(applications, new HashSet<Guid>(), Cutoff);

        selected.CandidateCount.Should().Be(2);
        selected.ApplicationIds.Should().BeInAscendingOrder();
    }

    [Fact]
    public void The_options_default_to_the_documented_values()
    {
        var requests = new DataRequestOptions();
        var retention = new RecordRetentionOptions();

        (requests.DueDays, requests.LinkLifetime, requests.LinkCooldown, requests.ExportLifetime, requests.PermitsPerMinute)
            .Should().Be((30, TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), TimeSpan.FromDays(7), 10));
        requests.ExportContainer.Should().Be("data-exports");
        requests.DueIn.Should().Be(TimeSpan.FromDays(30));
        retention.BatchSize.Should().Be(200);
    }

    private static RetentionApplication App(Guid candidateId, StageKind kind, DateTimeOffset enteredAt) =>
        new(Guid.CreateVersion7(), candidateId, kind, enteredAt, null, JobStatus.Open, null);

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(line => line.Sentence());
}
