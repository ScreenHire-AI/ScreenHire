using ScreenHire.Application.Distribution;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>
/// A posting the team makes themselves (docs/architecture/job-distribution.md, "Posted by you"): the key its link carries,
/// which fields of the job count as changed since it was posted, and taking it down and posting it again.
/// </summary>
public sealed class ManualPostingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static JobSnapshot Job(
        string title = "Engineer",
        string description = "<p>Build things.</p>",
        EmploymentType type = EmploymentType.FullTime,
        JobSalary? salary = null,
        string slug = "engineer",
        string? summary = null,
        string? department = null) =>
        new(title, slug, description, department, type, WorkplaceType.Remote, [], ["GB"], salary, null, summary);

    // The same cases as manual-postings.test.ts in the dashboard: the key the dashboard sends with a save is the one this
    // makes, so a link copied before saving counts once the posting is saved.
    [Theory]
    [InlineData("LinkedIn", "linkedin")]
    [InlineData("Riverside Jobs Weekly", "riverside-jobs-weekly")]
    [InlineData("  Indeed (free listing)  ", "indeed-free-listing")]
    [InlineData("Café Emplois", "cafe-emplois")]
    [InlineData("Talent.com", "talent-com")]
    [InlineData("***", "")]
    public void Source_key_for_a_board_name(string name, string key)
    {
        ManualPosting.SourceKeyFor(name).Should().Be(key);
    }

    [Fact]
    public void A_long_name_makes_a_key_the_column_holds_with_no_trailing_hyphen()
    {
        var key = ManualPosting.SourceKeyFor("A very long board name that goes on and on for ever");

        key.Length.Should().BeLessThanOrEqualTo(ManualPosting.SourceKeyMaxLength);
        key.Should().NotEndWith("-");
        ManualPosting.IsSourceKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("linkedin", true)]
    [InlineData("a-1", true)]
    [InlineData("LinkedIn", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void A_source_key_is_what_the_careers_app_passes_on(string key, bool valid)
    {
        ManualPosting.IsSourceKey(key).Should().Be(valid);
    }

    [Fact]
    public void Only_the_fields_a_person_copies_count_as_changed()
    {
        var posted = Job();

        ManualPosting.ChangedFields(posted, posted).Should().BeEmpty();
        ManualPosting.ChangedFields(posted, Job(summary: "A new summary", department: "Platform")).Should().BeEmpty(
            "the summary and the department are not on the listing");
        ManualPosting.ChangedFields(posted, Job(title: "Lead engineer", salary: new JobSalary(50000, 60000, "GBP", SalaryPeriod.Year)))
            .Should().Equal(PostedField.Title, PostedField.Salary);
        ManualPosting.ChangedFields(posted, Job(type: EmploymentType.Contract, description: "<p>Other.</p>"))
            .Should().Equal(PostedField.Type, PostedField.Description);
        ManualPosting.ChangedFields(posted, posted with { WorkplaceType = WorkplaceType.Onsite, Locations = [new JobLocation("Leeds", null, "GB")] })
            .Should().Equal(PostedField.Location);
        ManualPosting.ChangedFields(posted, Job(slug: "engineer-2")).Should().Equal([PostedField.Link], "the apply link holds the slug");
    }

    [Fact]
    public void Marking_it_updated_takes_the_job_as_it_is_now()
    {
        var posting = ManualPosting.Create(Guid.NewGuid(), Guid.NewGuid(), "LinkedIn", "linkedin", null, new DateOnly(2026, 11, 20), null, Job(), Now);
        var changed = Job(title: "Lead engineer");
        posting.ChangedSince(changed).Should().Equal(PostedField.Title);

        posting.MarkUpdated(changed, Now.AddHours(1));

        posting.ChangedSince(changed).Should().BeEmpty();
        posting.ContentAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Taking_it_down_twice_changes_nothing_the_second_time_and_posting_again_keeps_the_key()
    {
        var posting = ManualPosting.Create(Guid.NewGuid(), Guid.NewGuid(), "Indeed", "indeed", null, new DateOnly(2026, 11, 20), null, Job(), Now);

        posting.TakeDown(Now.AddDays(1)).Should().BeTrue();
        posting.TakeDown(Now.AddDays(2)).Should().BeFalse();
        posting.TakenDownAt.Should().Be(Now.AddDays(1));

        posting.PostAgain("Indeed UK", "https://www.indeed.example/job/1", new DateOnly(2026, 11, 25), null, Job(title: "Lead engineer"), Now.AddDays(3));

        posting.IsTakenDown.Should().BeFalse();
        posting.SourceKey.Should().Be("indeed");
        posting.BoardName.Should().Be("Indeed UK");
        posting.ChangedSince(Job(title: "Lead engineer")).Should().BeEmpty();
    }

    [Fact]
    public void A_key_is_refused_when_it_is_not_one_the_careers_app_passes_on()
    {
        var create = () => ManualPosting.Create(Guid.NewGuid(), Guid.NewGuid(), "LinkedIn", "Linked In", null, new DateOnly(2026, 11, 20), null, Job(), Now);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_tracked_link_carries_the_key_as_its_source()
    {
        BoardUrls.WithSource("https://careers.example.com/harbour/jobs/engineer", "riverside-jobs-weekly")
            .Should().Be("https://careers.example.com/harbour/jobs/engineer?src=riverside-jobs-weekly");
        Boards.All.Should().Contain(BoardUrls.TaggedBoards).And.Contain([Boards.CareersPage, Boards.GoogleIndexing, Boards.JobBoardApi]);
    }
}
