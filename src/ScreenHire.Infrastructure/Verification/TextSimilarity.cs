using System.Globalization;
using System.Numerics;
using System.Text;

namespace ScreenHire.Infrastructure.Verification;

/// <summary>
/// Near-duplicate text without a model (<c>CNT_NEAR_DUPLICATE_ANSWER</c>, verification-engine.md §10.10). Answers become
/// sets of three-word sequences (shingles). A 64-bit SimHash of the set finds candidates cheaply; the overlap of the sets
/// themselves (Jaccard similarity) decides.
/// </summary>
/// <remarks>
/// Thresholds, tested in <c>TextSimilarityTests</c>: answers of at least <see cref="MinWords"/> words; candidates within
/// <see cref="MaxHammingDistance"/> bits; a match when at least <see cref="MinJaccard"/> of the combined shingles are
/// shared. Unrelated answers sit near 32 differing bits and share almost no shingles; changing one word in
/// twenty changes about three shingles in twenty. The thresholds favour missing a reworded copy over flagging two
/// people who happened to write alike.
/// </remarks>
public static class TextSimilarity
{
    public const int ShingleSize = 3;

    /// <summary>Shorter answers ("One month", "Yes, from June") are alike by nature and never compared.</summary>
    public const int MinWords = 20;

    /// <summary>SimHash prefilter. Generous on purpose: the shingle overlap makes the decision.</summary>
    public const int MaxHammingDistance = 20;

    public const double MinJaccard = 0.6;

    /// <summary>Lower-case words: runs of letters and digits, everything else a separator.</summary>
    public static IReadOnlyList<string> Words(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>The hashes of every <see cref="ShingleSize"/>-word sequence.</summary>
    public static HashSet<ulong> Shingles(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var shingles = new HashSet<ulong>();
        for (var i = 0; i + ShingleSize <= words.Count; i++)
        {
            shingles.Add(Hash(string.Join(' ', words.Skip(i).Take(ShingleSize))));
        }

        return shingles;
    }

    /// <summary>A 64-bit SimHash: each bit is the majority vote of that bit across the shingle hashes.</summary>
    public static long SimHash(IReadOnlyCollection<ulong> shingles)
    {
        ArgumentNullException.ThrowIfNull(shingles);
        Span<int> votes = stackalloc int[64];
        foreach (var shingle in shingles)
        {
            for (var bit = 0; bit < 64; bit++)
            {
                votes[bit] += ((shingle >> bit) & 1) == 1 ? 1 : -1;
            }
        }

        ulong hash = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (votes[bit] > 0)
            {
                hash |= 1UL << bit;
            }
        }

        return unchecked((long)hash);
    }

    public static int HammingDistance(long left, long right) => BitOperations.PopCount(unchecked((ulong)(left ^ right)));

    /// <summary>Shingles in both sets, and in either.</summary>
    public static (int Shared, int Combined) Overlap(IReadOnlySet<ulong> left, IReadOnlySet<ulong> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var shared = left.Count(right.Contains);
        return (shared, left.Count + right.Count - shared);
    }

    /// <summary>Whether two answers are near-duplicates by the documented thresholds.</summary>
    public static bool IsNearDuplicate(IReadOnlySet<ulong> left, IReadOnlySet<ulong> right)
    {
        var (shared, combined) = Overlap(left, right);
        return combined > 0 && shared >= MinJaccard * combined;
    }

    // FNV-1a over UTF-8, then SplitMix64's finaliser so every bit is well mixed for the SimHash vote. Stable across
    // processes and versions, unlike string.GetHashCode: fingerprints are stored.
    private static ulong Hash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9UL;
        hash ^= hash >> 27;
        hash *= 0x94D049BB133111EBUL;
        hash ^= hash >> 31;
        return hash;
    }

    /// <summary>A count for evidence text, invariant culture.</summary>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
