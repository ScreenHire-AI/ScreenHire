using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.UnitTests.Integrity.Content;

/// <summary>
/// The near-duplicate thresholds (<see cref="TextSimilarity"/>, integrity-engine.md §10.10): a copy with a word or two
/// changed matches; two people answering the same question in their own words do not; the SimHash prefilter keeps
/// every true match; fingerprints are stable across processes because they are stored.
/// </summary>
public sealed class TextSimilarityTests
{
    private const string Original =
        "I want to join because your team ships accessible products for small charities, and I have spent four years " +
        "building donation platforms that had to work on old phones and slow connections. I enjoy measuring what users " +
        "actually do and fixing the slowest pages first.";

    // Two words changed, as a farm operator edits a template answer.
    private const string LightlyEdited =
        "I want to join because your team ships accessible products for small charities, and I have spent five years " +
        "building donation platforms that had to work on old phones and slow networks. I enjoy measuring what users " +
        "actually do and fixing the slowest pages first.";

    // The same motivation in another person's words.
    private const string OwnWords =
        "Your work for charities stood out to me. Most of my career has been on fundraising sites where pages needed to " +
        "load quickly for supporters on cheap devices, and I like using analytics to decide which part of a site to " +
        "improve next.";

    [Fact]
    public void A_lightly_edited_copy_is_a_near_duplicate()
    {
        var original = Shingles(Original);
        var edited = Shingles(LightlyEdited);

        TextSimilarity.IsNearDuplicate(original, edited).Should().BeTrue();
        TextSimilarity.HammingDistance(TextSimilarity.SimHash(original), TextSimilarity.SimHash(edited))
            .Should().BeLessThanOrEqualTo(TextSimilarity.MaxHammingDistance, "the prefilter must keep every match the overlap would confirm");
    }

    [Fact]
    public void An_identical_answer_with_different_punctuation_and_case_is_a_near_duplicate()
    {
        TextSimilarity.IsNearDuplicate(Shingles(Original), Shingles(Original.ToUpperInvariant().Replace(",", " ;", StringComparison.Ordinal)))
            .Should().BeTrue();
    }

    [Fact]
    public void The_same_idea_in_someone_elses_words_is_not()
    {
        var (shared, combined) = TextSimilarity.Overlap(Shingles(Original), Shingles(OwnWords));

        TextSimilarity.IsNearDuplicate(Shingles(Original), Shingles(OwnWords)).Should().BeFalse();
        shared.Should().BeLessThan(combined / 10);
    }

    [Fact]
    public void Half_copied_answers_fall_under_the_threshold()
    {
        var halfCopied = string.Join(' ', Original.Split(' ').Take(20)) + " " + string.Join(' ', OwnWords.Split(' ').Skip(18));

        TextSimilarity.IsNearDuplicate(Shingles(Original), Shingles(halfCopied)).Should().BeFalse(
            "the threshold favours missing a partial copy over flagging two people who wrote alike");
    }

    [Fact]
    public void Words_are_lower_case_letters_and_digits()
    {
        TextSimilarity.Words("Café-owner, 10 YEARS!").Should().Equal("café", "owner", "10", "years");
    }

    [Fact]
    public void A_fingerprint_is_the_same_in_every_process()
    {
        // Stored fingerprints are compared with new ones, so the hash must not depend on the process or runtime.
        TextSimilarity.SimHash(Shingles(Original)).Should().Be(TextSimilarity.SimHash(Shingles(Original)));
        TextSimilarity.SimHash(Shingles("one two three four")).Should().Be(ExpectedSimHash);
    }

    // SimHash of "one two three four", computed once; it changes only if the hashing changes, which would make every
    // stored fingerprint stale.
    private const long ExpectedSimHash = -9151103326920363448;

    private static HashSet<ulong> Shingles(string text) => TextSimilarity.Shingles(TextSimilarity.Words(text));
}
