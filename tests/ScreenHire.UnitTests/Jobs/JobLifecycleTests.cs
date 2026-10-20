using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobLifecycleTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_job_is_a_draft_that_was_never_published_or_closed()
    {
        var job = NewJob();

        job.Status.Should().Be(JobStatus.Draft);
        job.PublishedAt.Should().BeNull();
        job.ClosedAt.Should().BeNull();
        job.PublishedVersion.Should().Be(0);
        job.Workflow.Should().Be(HiringWorkflow.Default);
    }

    [Fact]
    public void A_new_job_gets_a_time_ordered_version_7_id()
    {
        NewJob().Id.Version.Should().Be(7);
    }

    [Fact]
    public void Publishing_opens_the_job_and_freezes_its_content_as_version_1()
    {
        var job = NewJob();

        var version = job.Publish(Monday);

        job.Status.Should().Be(JobStatus.Open);
        job.PublishedAt.Should().Be(Monday);
        job.PublishedVersion.Should().Be(1);
        version.Should().NotBeNull();
        version!.Version.Should().Be(1);
        version.JobId.Should().Be(job.Id);
        version.OrganizationId.Should().Be(job.OrganizationId);
        version.PublishedAt.Should().Be(Monday);
        version.Snapshot.Should().Be(job.Snapshot());
    }

    [Fact]
    public void Publishing_an_open_job_whose_content_has_not_changed_does_nothing()
    {
        var job = NewJob();
        var first = job.Publish(Monday);

        var again = job.Publish(Monday.AddDays(3), first);

        again.Should().BeNull();
        job.PublishedVersion.Should().Be(1);
        job.PublishedAt.Should().Be(Monday);
    }

    [Fact]
    public void Publishing_changed_content_freezes_the_next_version_and_keeps_the_first_publish_date()
    {
        var job = NewJob();
        var first = job.Publish(Monday);
        job.Revise(job.Snapshot() with { Title = "Staff .NET Engineer" }, Monday.AddDays(1));

        var second = job.Publish(Monday.AddDays(2), first);

        second!.Version.Should().Be(2);
        second.Snapshot.Title.Should().Be("Staff .NET Engineer");
        first!.Snapshot.Title.Should().Be("Senior .NET Engineer", "a published version never changes");
        job.PublishedAt.Should().Be(Monday);
        job.PublishedVersion.Should().Be(2);
    }

    [Fact]
    public void A_posting_whose_valid_through_date_has_passed_cannot_be_published()
    {
        var job = NewJob();
        job.ValidThrough = Monday;

        var publish = () => job.Publish(Monday.AddMinutes(1));

        publish.Should().Throw<InvalidOperationException>().WithMessage("*validThrough*");
        job.Status.Should().Be(JobStatus.Draft);
    }

    [Fact]
    public void Valid_through_keeps_the_databases_microsecond_precision_so_a_frozen_snapshot_matches_the_job_read_back()
    {
        var job = NewJob();

        job.ValidThrough = Monday.AddTicks(1234567);

        job.ValidThrough.Should().Be(Monday.AddTicks(1234560));
        job.Snapshot().ValidThrough!.Value.Ticks.Should().Be(Monday.AddTicks(1234560).Ticks);
    }

    [Fact]
    public void Closing_an_open_job_records_when_and_keeps_the_publish_date()
    {
        var job = NewJob();
        job.Publish(Monday);

        var closed = job.Close(Monday.AddDays(14));

        closed.Should().BeTrue();
        job.Status.Should().Be(JobStatus.Closed);
        job.PublishedAt.Should().Be(Monday);
        job.ClosedAt.Should().Be(Monday.AddDays(14));
    }

    [Fact]
    public void Closing_a_closed_job_changes_nothing()
    {
        var job = NewJob();
        job.Close(Monday);

        var closedAgain = job.Close(Monday.AddDays(1));

        closedAgain.Should().BeFalse();
        job.ClosedAt.Should().Be(Monday);
    }

    [Fact]
    public void A_draft_can_be_closed_without_ever_being_published()
    {
        var job = NewJob();

        job.Close(Monday);

        job.Status.Should().Be(JobStatus.Closed);
        job.PublishedAt.Should().BeNull();
        job.ClosedAt.Should().Be(Monday);
    }

    [Fact]
    public void A_closed_job_cannot_be_republished_or_changed()
    {
        var job = NewJob();
        job.Publish(Monday);
        job.Close(Monday.AddDays(1));

        var republish = () => job.Publish(Monday.AddDays(2));
        var revise = () => job.Revise(job.Snapshot() with { Title = "Changed" }, Monday.AddDays(2));

        republish.Should().Throw<InvalidOperationException>().WithMessage("*closed job cannot be republished*");
        revise.Should().Throw<InvalidOperationException>().WithMessage("*closed job cannot be changed*");
        job.Status.Should().Be(JobStatus.Closed);
        job.Title.Should().Be("Senior .NET Engineer");
    }

    [Fact]
    public void A_published_job_keeps_its_slug()
    {
        var job = NewJob();
        job.Publish(Monday);

        var rename = () => job.Revise(job.Snapshot() with { Slug = "another-slug" }, Monday.AddDays(1));

        rename.Should().Throw<InvalidOperationException>().WithMessage("*keeps its slug*");
        job.Slug.Should().Be("senior-dotnet-engineer");
    }

    [Fact]
    public void A_draft_can_change_its_slug()
    {
        var job = NewJob();

        job.Revise(job.Snapshot() with { Slug = "dotnet-engineer" }, Monday);

        job.Slug.Should().Be("dotnet-engineer");
    }

    [Fact]
    public void Revising_with_identical_content_reports_no_change_and_keeps_the_update_time()
    {
        var job = NewJob();

        var changed = job.Revise(job.Snapshot(), Monday);

        changed.Should().BeFalse();
        job.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Revising_replaces_the_content_including_salary_and_locations()
    {
        var job = NewJob();
        var content = job.Snapshot() with
        {
            WorkplaceType = WorkplaceType.Hybrid,
            Locations = [new JobLocation("Leeds", "England", "GB")],
            ApplicantLocationRequirements = [],
            Salary = new JobSalary(30, 40, "GBP", SalaryPeriod.Hour),
        };

        var changed = job.Revise(content, Monday);

        changed.Should().BeTrue();
        job.Snapshot().Should().Be(content);
        job.SalaryMin.Should().Be(30);
        job.Salary!.Period.Should().Be(SalaryPeriod.Hour);
        job.UpdatedAt.Should().Be(Monday);
    }

    [Fact]
    public void Snapshots_compare_by_content_including_list_members()
    {
        var job = NewJob();

        var first = job.Snapshot();
        var second = job.Snapshot();

        first.Should().Be(second);
        first.Locations.Should().NotBeSameAs(second.Locations);
        (first with { ApplicantLocationRequirements = ["IE"] }).Should().NotBe(second);
    }

    [Fact]
    public void Using_a_template_copies_its_workflow_and_remembers_the_source()
    {
        var job = NewJob();
        var template = HiringTemplate.Create(job.OrganizationId, "Engineering", null, Definition("onsite"), Monday, null);
        var version = template.Versions[0];
        template.Publish(version, Monday, null);

        job.UseTemplate(version);

        job.Workflow.Should().Be(version.Definition.Workflow);
        job.SourceTemplateId.Should().Be(template.Id);
        job.SourceTemplateVersionId.Should().Be(version.Id);
        job.SourceTemplateVersion.Should().Be(1);
    }

    [Fact]
    public void Only_a_published_template_version_can_seed_a_job()
    {
        var job = NewJob();
        var template = HiringTemplate.Create(job.OrganizationId, "Engineering", null, Definition("onsite"), Monday, null);

        var use = () => job.UseTemplate(template.Versions[0]);

        use.Should().Throw<InvalidOperationException>().WithMessage("*published template versions only*");
    }

    [Fact]
    public void Revising_the_workflow_starts_a_new_workflow_version_and_keeps_the_source_template()
    {
        var job = NewJob();
        var template = HiringTemplate.Create(job.OrganizationId, "Engineering", null, Definition("onsite"), Monday, null);
        template.Publish(template.Versions[0], Monday, null);
        job.UseTemplate(template.Versions[0]);
        job.Publish(Monday);
        var edited = job.Workflow with { Stages = [.. job.Workflow.Stages, new("offer", "Offer", StageKind.Offer)] };

        job.ReviseWorkflow(edited, Monday.AddDays(1));

        job.Workflow.Should().BeSameAs(edited);
        job.WorkflowVersion.Should().Be(2);
        job.SourceTemplateVersion.Should().Be(1);
        job.UpdatedAt.Should().Be(Monday.AddDays(1));
    }

    [Fact]
    public void A_closed_job_keeps_its_workflow()
    {
        var job = NewJob();
        job.Close(Monday);

        var revise = () => job.ReviseWorkflow(HiringWorkflow.Default, Monday);

        revise.Should().Throw<InvalidOperationException>().WithMessage("*closed job*");
    }

    [Fact]
    public void Applying_a_template_update_takes_the_newer_version_on_an_open_job()
    {
        var job = NewJob();
        var template = HiringTemplate.Create(job.OrganizationId, "Engineering", null, Definition("onsite"), Monday, null);
        template.Publish(template.Versions[0], Monday, null);
        job.UseTemplate(template.Versions[0]);
        job.Publish(Monday);
        var second = template.CreateDraft(Definition("panel"), Monday, null);
        template.Publish(second, Monday.AddDays(2), null);

        job.ApplyTemplateUpdate(second, Monday.AddDays(3));

        job.Workflow.Should().Be(second.Definition.Workflow);
        job.SourceTemplateVersionId.Should().Be(second.Id);
        job.SourceTemplateVersion.Should().Be(2);
        job.WorkflowVersion.Should().Be(2);
    }

    [Fact]
    public void A_template_update_comes_from_the_source_template_and_a_published_version()
    {
        var job = NewJob();
        var template = HiringTemplate.Create(job.OrganizationId, "Engineering", null, Definition("onsite"), Monday, null);
        template.Publish(template.Versions[0], Monday, null);
        job.UseTemplate(template.Versions[0]);
        var draft = template.CreateDraft(Definition("panel"), Monday, null);
        var other = HiringTemplate.Create(job.OrganizationId, "Sales", null, Definition("call"), Monday, null);
        other.Publish(other.Versions[0], Monday, null);

        var fromDraft = () => job.ApplyTemplateUpdate(draft, Monday);
        var fromOther = () => job.ApplyTemplateUpdate(other.Versions[0], Monday);

        fromDraft.Should().Throw<InvalidOperationException>().WithMessage("*published*");
        fromOther.Should().Throw<InvalidOperationException>().WithMessage("*template it was created from*");
        job.WorkflowVersion.Should().Be(1);
    }

    [Theory]
    [InlineData(WorkplaceType.Remote, "Remote (GB, IE)")]
    [InlineData(WorkplaceType.Hybrid, "London, England, GB; Leeds, GB")]
    public void The_location_label_reads_from_the_structured_fields(WorkplaceType workplace, string expected)
    {
        JobLocation[] locations = [new("London", "England", "GB"), new("Leeds", null, "GB")];

        JobLocation.Describe(workplace, locations, ["GB", "IE"]).Should().Be(expected);
    }

    private static HiringTemplateDefinition Definition(string firstStage) =>
        new(HiringWorkflow.Default with { Stages = [new("applied", "Applied", StageKind.Applied), new(firstStage, "On site", StageKind.Interview)] }, PostingDefaults.None);

    private static Job NewJob()
    {
        var job = new Job
        {
            OrganizationId = Guid.CreateVersion7(),
            Title = "Senior .NET Engineer",
            Slug = "senior-dotnet-engineer",
            Description = "<p>Build things.</p>",
            WorkplaceType = WorkplaceType.Remote,
            ApplicantLocationRequirements = ["GB", "IE"],
        };
        job.SetSalary(new JobSalary(75_000, 95_000, "GBP", SalaryPeriod.Year));
        return job;
    }
}
