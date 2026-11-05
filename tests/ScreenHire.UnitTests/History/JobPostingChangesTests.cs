using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// A board's own title and description in the job's history: a line only when the text the board has of its own changes,
/// never for the state changes boards make all day.
/// </summary>
public sealed class JobPostingChangesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid JobId = Guid.CreateVersion7();

    private static readonly JobSnapshot Job = new(
        "Backend Engineer", "backend-engineer", "<p>Old text</p>", null, EmploymentType.FullTime, WorkplaceType.Onsite,
        [new JobLocation("London", null, "GB")], [], null, null);

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(l => l.Sentence());

    [Fact]
    public void Customizing_the_title_says_so_with_the_board_and_the_new_title()
    {
        var before = Posting("Adzuna");
        var after = Posting("Adzuna", title: "Senior Backend Engineer");

        var lines = JobPostingChanges.Describe(before, after);

        Sentences(lines).Should().Equal("customized the title for Adzuna");
        lines[0].Parts.Single(p => p.Strong).Text.Should().Be("Adzuna", "the board is the name of the thing that changed");
        lines[0].Details.Should().Equal(new ChangeDetail("Title", null, "Senior Backend Engineer"));
    }

    [Fact]
    public void Changing_an_existing_title_override_shows_the_old_and_the_new()
    {
        var lines = JobPostingChanges.Describe(Posting("Adzuna", title: "Backend Engineer II"), Posting("Adzuna", title: "Backend Engineer III"));

        Sentences(lines).Should().Equal("customized the title for Adzuna");
        lines[0].Details.Should().Equal(new ChangeDetail("Title", "Backend Engineer II", "Backend Engineer III"));
    }

    [Fact]
    public void Going_back_to_the_jobs_title_says_so()
    {
        var lines = JobPostingChanges.Describe(Posting("Adzuna", title: "Senior Backend Engineer"), Posting("Adzuna"));

        Sentences(lines).Should().Equal("went back to the job's title for Adzuna");
        lines[0].Details.Should().Equal(new ChangeDetail("Title", "Senior Backend Engineer", null));
    }

    [Fact]
    public void Customizing_the_description_shows_the_words_as_paragraphs()
    {
        var lines = JobPostingChanges.Describe(Posting("Adzuna"), Posting("Adzuna", description: "<p>New <em>text</em></p>"));

        Sentences(lines).Should().Equal("customized the description for Adzuna");
        lines[0].Details.Should().Equal(new ChangeDetail("Description", null, "New text", ChangeValueKind.Paragraphs));
    }

    [Fact]
    public void Going_back_to_the_jobs_description_says_so()
    {
        var lines = JobPostingChanges.Describe(Posting("Adzuna", description: "<p>New <em>text</em></p>"), Posting("Adzuna"));

        Sentences(lines).Should().Equal("went back to the job's description for Adzuna");
        lines[0].Details.Should().Equal(new ChangeDetail("Description", "New text", null, ChangeValueKind.Paragraphs));
    }

    [Fact]
    public void A_formatting_only_change_to_the_description_says_so_without_a_before_and_after_that_read_the_same()
    {
        var lines = JobPostingChanges.Describe(
            Posting("Adzuna", description: "<p>Same words</p>"), Posting("Adzuna", description: "<p>Same <strong>words</strong></p>"));

        Sentences(lines).Should().Equal("changed the formatting of the description for Adzuna");
        lines[0].Details.Should().BeNullOrEmpty();
    }

    [Fact]
    public void A_save_that_changes_both_texts_gives_the_title_line_then_the_description_line()
    {
        var lines = JobPostingChanges.Describe(Posting("Adzuna"), Posting("Adzuna", title: "Senior Backend Engineer", description: "<p>New text</p>"));

        Sentences(lines).Should().Equal("customized the title for Adzuna", "customized the description for Adzuna");
    }

    [Fact]
    public void The_board_id_is_named_when_no_display_name_was_set()
    {
        var lines = JobPostingChanges.Describe(Posting(null), Posting(null, title: "Senior Backend Engineer"));

        Sentences(lines).Should().Equal("customized the title for adzuna");
    }

    [Fact]
    public void A_posting_created_by_a_customization_says_what_it_was_customized_with()
    {
        var lines = JobPostingChanges.Describe(null, Posting("Adzuna", description: "<p>New text</p>"));

        Sentences(lines).Should().Equal("customized the description for Adzuna");
        lines[0].Details.Should().Equal(new ChangeDetail("Description", null, "New text", ChangeValueKind.Paragraphs));
    }

    [Fact]
    public void A_posting_created_by_a_publish_says_nothing()
    {
        JobPostingChanges.Describe(null, Posting("Adzuna")).Should().BeEmpty("publishing the job is the job's line");
    }

    [Fact]
    public void A_deleted_posting_says_nothing()
    {
        JobPostingChanges.Describe(Posting("Adzuna", title: "Senior Backend Engineer"), null).Should().BeEmpty();
    }

    [Fact]
    public void A_boards_state_changes_say_nothing_even_when_it_has_its_own_text()
    {
        var before = Posting("Adzuna", title: "Senior Backend Engineer", description: "<p>New text</p>");
        var after = Posting("Adzuna", title: "Senior Backend Engineer", description: "<p>New text</p>");
        after.Queue(2, "https://careers.example.com/harbour/jobs/backend-engineer", [], Monday.AddHours(1));
        after.MarkSubmitted(2, "ext-1", Monday.AddHours(2), null, Monday.AddHours(1));
        after.MarkLive(2, "ext-1", Monday.AddHours(2));
        after.RequestClose(null, null, Monday.AddHours(3));

        JobPostingChanges.Describe(before, after).Should().BeEmpty("a worker's save of the posting records nothing");
    }

    [Fact]
    public void Moving_the_base_an_override_was_written_against_says_nothing()
    {
        var before = Posting("Adzuna", description: "<p>New text</p>");
        var after = Posting("Adzuna", description: "<p>New text</p>", job: Job with { Description = "<p>The job changed</p>" });
        after.DescriptionBase.Should().NotBe(before.DescriptionBase, "the base did move");

        JobPostingChanges.Describe(before, after).Should().BeEmpty("the board shows the same text as before");
    }

    [Fact]
    public void Nothing_changing_says_nothing()
    {
        JobPostingChanges.Describe(Posting("Adzuna"), Posting("Adzuna")).Should().BeEmpty();
        JobPostingChanges.Describe(
            Posting("Adzuna", title: "Senior Backend Engineer", description: "<p>New text</p>"),
            Posting("Adzuna", title: "Senior Backend Engineer", description: "<p>New text</p>")).Should().BeEmpty();
    }

    // A fresh posting each time, so a before and an after never share state.
    private static JobPosting Posting(string? boardLabel, string? title = null, string? description = null, JobSnapshot? job = null)
    {
        var posting = new JobPosting { OrganizationId = Guid.CreateVersion7(), JobId = JobId, BoardId = "adzuna", CreatedAt = Monday, BoardLabel = boardLabel };
        posting.Customize(title, description, job ?? Job, Monday);
        return posting;
    }
}
