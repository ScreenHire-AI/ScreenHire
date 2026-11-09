using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobSummaryTextTests
{
    [Fact]
    public void The_first_sentence_of_the_description_is_the_summary()
    {
        JobSummaryText.FromDescription("<p>Fix leaks and fit bathrooms across Leeds. You lead your own jobs from week one.</p>")
            .Should().Be("Fix leaks and fit bathrooms across Leeds.");
    }

    [Theory]
    [InlineData("<p>Is this the role for you? Read on.</p>", "Is this the role for you?")]
    [InlineData("<p>Join us!  Then stay.</p>", "Join us!")]
    [InlineData("<p>No full stop at the end</p>", "No full stop at the end")]
    [InlineData("<p>Version 2.5 of the product needs an owner. Apply.</p>", "Version 2.5 of the product needs an owner.")]
    [InlineData("<p>Acme Inc. is hiring a plumber. Apply.</p>", "Acme Inc. is hiring a plumber.")]
    [InlineData("<p>Fit kitchens, e.g. worktops and units. Apply.</p>", "Fit kitchens, e.g. worktops and units.")]
    [InlineData("<p>Fix leaks. 2 years on the tools helps.</p>", "Fix leaks.")]
    public void A_sentence_ends_at_a_full_stop_question_or_exclamation_mark_that_ends_a_word(string html, string expected) =>
        JobSummaryText.FromDescription(html).Should().Be(expected);

    [Fact]
    public void A_heading_and_a_list_before_the_first_paragraph_are_skipped()
    {
        JobSummaryText.FromDescription("<h2>About the role</h2><ul><li>Python</li></ul><p>Build the data platform. More text.</p>")
            .Should().Be("Build the data platform.");
    }

    [Fact]
    public void Markup_entities_and_line_breaks_become_plain_text()
    {
        JobSummaryText.FromDescription("<p>Fish &amp; chips,<br>served <strong>fast</strong>. Next.</p>").Should().Be("Fish & chips, served fast.");
    }

    [Fact]
    public void A_long_sentence_is_cut_at_a_word_and_fits_the_limit()
    {
        var words = string.Join(' ', Enumerable.Repeat("plumbing", 30)) + ".";

        var summary = JobSummaryText.FromDescription($"<p>{words}</p>");

        summary.Length.Should().BeLessThanOrEqualTo(JobSummaryText.MaxLength);
        summary.Should().EndWith("plumbing…", "the cut falls between words");
        summary.TrimEnd('…').Split(' ').Should().OnlyContain(word => word == "plumbing");
    }

    [Fact]
    public void A_link_reads_as_its_text_without_its_address()
    {
        JobSummaryText.FromDescription("<p>Join <a href=\"https://example.com/team\">our support team</a> in Leeds. More.</p>")
            .Should().Be("Join our support team in Leeds.");
    }

    [Fact]
    public void A_long_run_with_no_space_is_never_cut_inside_a_character()
    {
        // An emoji outside the basic plane is two UTF-16 units, so the 159-unit cut would land between its halves.
        const string Emoji = "\U0001F600";
        var run = string.Concat(Enumerable.Repeat(Emoji, 100));

        var summary = JobSummaryText.FromDescription($"<p>{run}</p>");

        summary.Length.Should().BeLessThanOrEqualTo(JobSummaryText.MaxLength);
        summary.Should().EndWith(Emoji + "…");
        summary.TrimEnd('…').Should().Be(string.Concat(Enumerable.Repeat(Emoji, 79)), "no half of a pair is left behind");
    }

    [Fact]
    public void A_sentence_of_exactly_the_limit_is_kept_whole()
    {
        var sentence = new string('a', JobSummaryText.MaxLength - 1) + ".";

        JobSummaryText.FromDescription($"<p>{sentence}</p>").Should().Be(sentence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    [InlineData("<h2>Only a heading</h2>")]
    [InlineData("<ul><li>Only a list</li></ul>")]
    public void A_description_with_no_paragraph_gives_no_summary(string? html) =>
        JobSummaryText.FromDescription(html).Should().BeEmpty();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \n ", null)]
    [InlineData("  Own  the\npayments service. ", "Own the payments service.")]
    public void A_written_summary_is_stored_trimmed_with_single_spaces_or_not_at_all(string? written, string? stored) =>
        JobSummaryText.Normalize(written).Should().Be(stored);

    [Fact]
    public void A_job_shows_what_was_written_and_follows_the_description_until_then()
    {
        var job = new Job { Title = "Plumber", Slug = "plumber", Description = "<p>Fix leaks. More.</p>" };
        job.EffectiveSummary.Should().Be("Fix leaks.");

        job.Revise(job.Snapshot() with { Summary = "Service boilers across Leeds." }, DateTimeOffset.UnixEpoch);
        job.EffectiveSummary.Should().Be("Service boilers across Leeds.");

        job.Revise(job.Snapshot() with { Summary = null, Description = "<p>Learn the trade. More.</p>" }, DateTimeOffset.UnixEpoch);
        job.EffectiveSummary.Should().Be("Learn the trade.");
    }

    [Fact]
    public void A_summary_change_is_a_content_change_so_an_open_job_shows_unpublished_changes()
    {
        var job = new Job { Title = "Plumber", Slug = "plumber", Description = "<p>Fix leaks.</p>" };
        var before = job.Snapshot();

        job.Revise(before with { Summary = "Service boilers." }, DateTimeOffset.UnixEpoch).Should().BeTrue();

        job.Snapshot().Should().NotBe(before);
    }
}
