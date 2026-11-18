using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>The warnings before a job goes live: they come from each board's own requirements and warn, never block.</summary>
public sealed class PublishChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly FixedTime Time = new(Now);
    private static readonly string[] OfficePrompts = ["Describe the work, the office and the team.", "List the skills and experience that matter."];

    private const string Written = "<h2>About the role</h2><p>Keep our books straight and our suppliers paid on time, working with a small friendly team that looks after local shops and cafes.</p>";

    private static readonly CareersPageAdapter Careers = new CareersPageAdapter();
    private static readonly TalentFeedAdapter Talent = new TalentFeedAdapter(Time);
    private static readonly AdzunaFeedAdapter Adzuna = new AdzunaFeedAdapter(Time);

    [Fact]
    public void A_job_that_is_ready_has_nothing_to_check()
    {
        Run(Job(), [Careers, Talent, Adzuna]).Should().BeEmpty();
    }

    [Fact]
    public void A_description_that_still_has_a_templates_prompt_is_named()
    {
        var job = Job("<h2>About the role</h2><p>Describe the work, the office and the team.</p><h2>What we are looking for</h2><p>Experience with ledgers.</p>");

        var item = Run(job, [Careers]).Should().ContainSingle().Which;

        item.Kind.Should().Be(PublishCheckKind.TemplatePrompt);
        item.Field.Should().Be(PublishCheckField.Description);
        item.Text.Should().Be("The description still has the template's fill-in text, \"Describe the work, the office and the team.\".");
    }

    [Fact]
    public void Two_prompts_are_one_item_that_counts_the_rest()
    {
        var job = Job("<h2>About the role</h2><p>Describe the work, the office and the team.</p><h2>What we are looking for</h2><p>List the skills and experience that matter.</p>");

        Run(job, [Careers]).Should().ContainSingle().Which.Text.Should().EndWith("and 1 more.");
    }

    [Fact]
    public void A_prompt_is_found_through_tags_entities_and_white_space_but_not_when_it_was_written_over()
    {
        Run(Job("<p>Describe  the work,\n <strong>the office</strong> and the team.</p>"), [Careers]).Should().ContainSingle();
        Run(Job("<p>Describe the work, the office &amp; the team.</p>" + Written), [Careers]).Should().BeEmpty("the ampersand makes it different text");
        Run(Job("<p>Describe the work, the office and the team, in our own words.</p>" + Written), [Careers]).Should().BeEmpty("a prompt that was edited is written text");
        Run(Job("<h2>Describe the work, the office and the team.</h2>" + Written), [Careers]).Should().ContainSingle("the text is the prompt wherever it sits");
    }

    [Fact]
    public void Headings_of_a_template_are_not_prompts()
    {
        var prompts = TemplatePrompts.From(["<h2>About the role</h2><p>Describe the work, the office and the team.</p><h2>What we are looking for</h2><p>List the skills and experience that matter.</p>"]);

        prompts.Should().Equal(OfficePrompts);
    }

    [Fact]
    public void The_prompts_of_every_template_are_collected_once()
    {
        var prompts = TemplatePrompts.From(
            [null, "", "<p>Say what a paid trial shift involves.</p>", "<p class=\"x\">Say what a paid trial shift involves.</p><p>  </p>"]);

        prompts.Should().Equal("Say what a paid trial shift involves.");
    }

    [Fact]
    public void An_onsite_job_with_no_city_is_named_for_the_boards_that_need_one()
    {
        var job = Job(locations: [new JobLocation(null, null, "GB")]);

        var item = Run(job, [Careers, Talent, Adzuna]).Should().ContainSingle().Which;

        item.Kind.Should().Be(PublishCheckKind.NoCity);
        item.Field.Should().Be(PublishCheckField.City);
        item.Boards.Should().Equal("Talent.com");
        item.Text.Should().Be("Talent.com needs a city for an on-site or hybrid job.");
    }

    [Theory]
    [InlineData(WorkplaceType.Onsite, true)]
    [InlineData(WorkplaceType.Hybrid, true)]
    [InlineData(WorkplaceType.Remote, false)]
    public void Only_a_job_that_is_not_remote_needs_a_city(WorkplaceType workplace, bool warned)
    {
        var job = Job(workplace: workplace, locations: [new JobLocation(null, null, "GB")]);

        Run(job, [Talent]).Any(i => i.Kind == PublishCheckKind.NoCity).Should().Be(warned);
    }

    [Fact]
    public void A_second_location_with_a_city_is_enough()
    {
        var job = Job(locations: [new JobLocation(null, null, "GB"), new JobLocation("Leeds", null, "GB")]);

        Run(job, [Talent]).Should().BeEmpty();
    }

    [Fact]
    public void A_board_that_needs_a_salary_names_it_and_a_job_with_one_is_fine()
    {
        var board = new FakeBoard("pay-board", "Pay Board", new BoardRequirements(Salary: true));
        var other = new FakeBoard("other-board", "Other Board", new BoardRequirements(Salary: true));

        var item = Run(Job(noSalary: true), [board, other, Talent]).Should().ContainSingle().Which;

        item.Kind.Should().Be(PublishCheckKind.NoSalary);
        item.Field.Should().Be(PublishCheckField.Salary);
        item.Text.Should().Be("Other Board and Pay Board need a salary.");
        Run(Job(), [board]).Should().BeEmpty();
    }

    [Fact]
    public void A_short_description_names_the_strictest_board_and_its_minimum()
    {
        var job = Job("<p>Keep the books straight for a small friendly team of five people.</p>");

        var item = Run(job, [Talent, Adzuna]).Should().ContainSingle().Which;

        item.Kind.Should().Be(PublishCheckKind.ShortDescription);
        item.Field.Should().Be(PublishCheckField.Description);
        item.Text.Should().Be("Adzuna needs a description of at least 100 characters.", "Talent.com accepts 50, so only Adzuna refuses it");
        item.Boards.Should().Equal("Adzuna");

        var shorter = Run(Job("<p>Keep the books.</p>"), [Talent, Adzuna]).Should().ContainSingle().Which;
        shorter.Text.Should().Be("Adzuna and Talent.com need a longer description, up to 100 characters for Adzuna.");
        shorter.Boards.Should().Equal("Adzuna", "Talent.com");
    }

    [Fact]
    public void The_length_ignores_tags()
    {
        var tagged = "<p><strong><em>" + new string('a', 60) + "</em></strong></p><ul><li></li></ul>";

        Run(Job(tagged), [Talent]).Should().BeEmpty();
        Run(Job(tagged), [Adzuna]).Should().ContainSingle().Which.Kind.Should().Be(PublishCheckKind.ShortDescription);
    }

    [Fact]
    public void A_boards_own_text_is_what_its_length_is_measured_on()
    {
        var job = Job("<p>Keep the books.</p>");
        var customized = job with { Description = Written };

        PublishChecks.Run(job, [(Adzuna, customized)], []).Should().BeEmpty("the board has its own, longer description");
    }

    [Fact]
    public void Items_come_in_a_fixed_order_with_the_prompt_first()
    {
        var job = Job("<p>Describe the work, the office and the team.</p>", locations: [new JobLocation(null, null, "GB")]);
        var board = new FakeBoard("pay-board", "Pay Board", new BoardRequirements(Salary: true));

        Run(job with { Salary = null }, [Careers, Talent, Adzuna, board]).Select(i => i.Kind).Should().Equal(
            PublishCheckKind.TemplatePrompt, PublishCheckKind.ShortDescription, PublishCheckKind.NoCity, PublishCheckKind.NoSalary);
    }

    [Fact]
    public void A_boards_validation_and_the_check_read_the_same_requirements()
    {
        var job = Job("<p>Keep the books.</p>", locations: [new JobLocation(null, null, "GB")]);
        var content = Content(job);

        Talent.Validate(content).Should().Equal(
            "Talent.com needs a description of at least 50 characters.", "Talent.com needs a city for on-site and hybrid jobs.");
        Adzuna.Validate(content).Should().Equal("Adzuna needs a description of at least 100 characters.");
    }

    [Fact]
    public async Task A_publish_goes_to_every_available_board_for_a_draft_and_to_the_open_ones_for_a_job_already_on_boards()
    {
        var registry = new BoardRegistry([Careers, Talent, Adzuna]);
        var available = registry.AvailableTo(new Organization { Name = "Harbour & Co", Slug = "harbour", HomeRegion = "eu" }).Select(b => b.BoardId).ToList();
        DistributionPlanner.ChosenBoards(available, [], null).Should().BeEquivalentTo(available);
        DistributionPlanner.ChosenBoards(available, [], ["adzuna"]).Should().Equal("adzuna");

        var planner = new DistributionPlanner(registry, Time);
        var organization = new Organization { Name = "Harbour & Co", Slug = "harbour", HomeRegion = "eu" };
        var postings = (await planner.PublishAsync(organization, Content(Job()), [], ["careers", "talent", "adzuna"], CancellationToken.None)).Added.ToList();
        postings.Single(p => p.BoardId == "adzuna").RequestClose(null, null, Now);

        DistributionPlanner.ChosenBoards(available, postings, null).Should().BeEquivalentTo(["careers", "talent"], "a board going down is not chosen");
        DistributionPlanner.ChosenBoards(["careers"], postings, null).Should().Equal(["careers"], "a board that is no longer available is left out");
    }

    private static IReadOnlyList<PublishCheckItem> Run(JobSnapshot job, IReadOnlyList<IJobBoardAdapter> boards) =>
        PublishChecks.Run(job, [.. boards.Select(b => (b, job))], OfficePrompts);

    private static JobSnapshot Job(
        string? description = null,
        WorkplaceType workplace = WorkplaceType.Onsite,
        IReadOnlyList<JobLocation>? locations = null,
        JobSalary? salary = null,
        bool noSalary = false) => new(
        "Bookkeeper", "bookkeeper", description ?? Written, "Finance", EmploymentType.FullTime, workplace,
        locations ?? [new JobLocation("Leeds", "England", "GB")], [], noSalary ? null : salary ?? new JobSalary(30_000, 35_000, "GBP", SalaryPeriod.Year), null);

    private static PostingContent Content(JobSnapshot job) => new(
        Guid.Parse("0199f0c4-0000-7000-8000-0000000000a1"), 1, job, Now, Now, "https://careers.example.com/harbour/jobs/bookkeeper", "Harbour & Co", "harbour");

    private sealed class FakeBoard(string id, string name, BoardRequirements requirements) : IJobBoardAdapter
    {
        public string BoardId => id;

        public string Name => name;

        public BoardCapabilities Capabilities { get; } = new(
            BoardMode.Push, CanClose: true, CanUnpublishPerBoard: true, ConfirmsLive: true, ConfirmsRemoval: true,
            PublishEta: null, RemovalEta: null, Requirements: requirements);

        public bool IsConfigured => true;

        public IReadOnlyList<string> Validate(PostingContent content) => [];

        public Task<BoardResult> PublishAsync(PostingRequest request, CancellationToken cancellationToken) => Task.FromResult(BoardResult.Pending());

        public Task<BoardResult> UpdateAsync(PostingRequest request, CancellationToken cancellationToken) => Task.FromResult(BoardResult.Pending());

        public Task<BoardResult> CloseAsync(PostingRequest request, CancellationToken cancellationToken) => Task.FromResult(BoardResult.Pending());

        public Task<BoardResult> GetStatusAsync(PostingRequest request, CancellationToken cancellationToken) => Task.FromResult(BoardResult.Pending());
    }
}
