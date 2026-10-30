using ScreenHire.Api.Endpoints;
using ScreenHire.Api.Reports;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Reports;

/// <summary>The Reports page's funnel rows and the checks on a report's range (docs/architecture/reports.md).</summary>
public sealed class HiringReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_funnel_runs_from_applied_to_hired_in_the_order_stage_kinds_are_declared()
    {
        HiringReportReads.FunnelKinds.Should().Equal(
            StageKind.Applied, StageKind.Screening, StageKind.Assessment, StageKind.Interview, StageKind.Offer, StageKind.Hired);
        HiringReportReads.FunnelKinds.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Verification_custom_and_the_stages_that_end_an_application_without_a_hire_have_no_row()
    {
        // A verification step is where integrity conditions route candidates, so counting it would be verification data.
        HiringReportReads.FunnelKinds.Should().NotContain([StageKind.Verification, StageKind.Custom, StageKind.Rejected, StageKind.Withdrawn]);
    }

    [Fact]
    public void A_range_has_both_ends_ends_after_it_starts_and_covers_five_years_at_most()
    {
        ReportEndpoints.Problems(Now.AddDays(-30), Now, null).Should().BeEmpty();
        ReportEndpoints.Problems(null, null, null).Keys.Should().BeEquivalentTo("from", "to");
        ReportEndpoints.Problems(Now, Now, null).Keys.Should().Equal("to");
        ReportEndpoints.Problems(Now, Now.AddDays(-1), null).Keys.Should().Equal("to");
        ReportEndpoints.Problems(Now.AddDays(-1827), Now, null).Should().BeEmpty("five years is allowed");
        ReportEndpoints.Problems(Now.AddDays(-1828), Now, null).Keys.Should().Equal("to");
    }

    [Fact]
    public void A_report_filters_by_two_hundred_jobs_at_most()
    {
        ReportEndpoints.Problems(Now.AddDays(-1), Now, [.. Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())]).Should().BeEmpty();
        ReportEndpoints.Problems(Now.AddDays(-1), Now, [.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())]).Keys.Should().Equal("jobId");
    }
}
