using ScreenHire.Api.Reports;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Equality monitoring totals hide what could point at a person (docs/architecture/reports.md, Equality monitoring): every
/// count under 5, zero included, and the next smallest when only one count of a question would be hidden, so the number of
/// responses minus the shown counts never gives the hidden one.
/// </summary>
public sealed class SmallCountsTests
{
    [Fact]
    public void Counts_under_five_are_hidden_zero_included()
    {
        SmallCounts.Suppress([12, 4, 0, 7]).Should().Equal(12, null, null, 7);
        SmallCounts.Suppress([5, 6]).Should().Equal(5, 6);
    }

    [Fact]
    public void A_lone_hidden_count_takes_the_next_smallest_with_it()
    {
        SmallCounts.Suppress([9, 6, 3, 20]).Should().Equal([9, null, null, 20], "9 + 6 + 20 shown with 38 responses would give the 3 away");
        SmallCounts.Suppress([3]).Should().Equal([(int?)null], "nothing is left to hide with it");
        SmallCounts.Suppress([7, 7, 1]).Should().Equal([null, 7, null], "the first of two equal counts goes");
    }
}
