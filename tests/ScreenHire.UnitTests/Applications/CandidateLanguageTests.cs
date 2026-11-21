using System.Globalization;
using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Applications;

/// <summary>
/// The language a candidate's emails write dates in: which tags are kept, which culture a tag reads as, and the date and
/// clock text that culture gives. en-GB is the language of an application with none recorded.
/// </summary>
public sealed class CandidateLanguageTests
{
    private static readonly DateTimeOffset Instant = new(2026, 11, 29, 23, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("  de-DE ", "de-DE")]
    [InlineData("zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("de", "de")]
    public void A_language_tag_is_kept(string sent, string kept) => CandidateLanguage.Normalize(sent).Should().Be(kept);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("en_US")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("en-US, de;q=0.9")]
    [InlineData("x")]
    [InlineData("en-u-ca-hebrew")]
    [InlineData("en-x-private")]
    public void Anything_else_is_dropped(string? sent) => CandidateLanguage.Normalize(sent).Should().BeNull();

    [Fact]
    public void A_tag_longer_than_the_column_is_dropped() =>
        CandidateLanguage.Normalize("en-" + new string('a', CandidateLanguage.MaxLength)).Should().BeNull();

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de-DE")]
    public void A_known_language_reads_as_itself(string tag, string expected) => CandidateLanguage.Culture(tag).Name.Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx-ZZ")]
    [InlineData("<script>")]
    public void An_unknown_or_missing_language_reads_as_en_GB(string? tag) => CandidateLanguage.Culture(tag).Name.Should().Be("en-GB");

    [Fact]
    public void The_long_date_follows_the_language_and_leaves_out_the_weekday()
    {
        CandidateLanguage.LongDate(Instant, CultureInfo.GetCultureInfo("en-US")).Should().Be("November 29, 2026");
        CandidateLanguage.LongDate(Instant, CultureInfo.GetCultureInfo("en-GB")).Should().Be("29 November 2026");
        CandidateLanguage.LongDate(Instant, CultureInfo.GetCultureInfo("de")).Should().Be("29. November 2026");
    }

    [Fact]
    public void The_long_date_is_the_day_in_utc_whatever_the_offset_it_came_with()
    {
        var late = new DateTimeOffset(2026, 11, 30, 4, 0, 0, TimeSpan.FromHours(5));

        CandidateLanguage.LongDate(late, CultureInfo.GetCultureInfo("en-GB")).Should().Be("29 November 2026");
    }

    [Fact]
    public void The_clock_is_12_hour_in_en_US_and_24_hour_in_en_GB_and_has_no_no_break_space()
    {
        var afternoon = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

        CandidateLanguage.Clock(afternoon, CultureInfo.GetCultureInfo("en-US")).Should().Be("3:00 PM");
        CandidateLanguage.Clock(afternoon, CultureInfo.GetCultureInfo("en-GB")).Should().Be("15:00");
    }

    [Fact]
    public void Plain_turns_no_break_spaces_into_spaces() => CandidateLanguage.Plain("3:00 PM today").Should().Be("3:00 PM today");
}
