using System.Reflection;
using System.Text.RegularExpressions;
using ScreenHire.Api.Contracts;

namespace ScreenHire.UnitTests.Privacy;

/// <summary>
/// The decision record goes to whoever answers a candidate's access or review request, and from there possibly to the
/// candidate. ADR-0004 says verification output never becomes a number: no score, no percentage, no probability, and a
/// level, weight or confidence is a category. This walks every type the record can carry, the verification contract it
/// shares with the verification route included, so such a field added anywhere fails here, whether or not a test's data
/// fills it. A metric's amount in its unit (seconds, a count) is a measurement and stays allowed.
/// </summary>
public sealed partial class DecisionRecordContractTests
{
    private static readonly Type[] Numeric =
        [typeof(int), typeof(long), typeof(short), typeof(byte), typeof(double), typeof(float), typeof(decimal), typeof(Half)];

    // A scorecard is the interview form a reviewer fills in, not a score, so its id is no offender.
    [GeneratedRegex("score(?!card)|percent|probability|likelihood", RegexOptions.IgnoreCase)]
    internal static partial Regex ScoreLike();

    [GeneratedRegex("^(Level|Weight|Confidence)$")]
    internal static partial Regex Categorical();

    [Fact]
    public void The_record_carries_no_score_and_its_levels_weights_and_confidences_are_categories()
    {
        var offenders = Reachable(typeof(ApplicationDecisionRecord))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => (type, property)))
            .Where(p => ScoreLike().IsMatch(p.property.Name)
                || (Categorical().IsMatch(p.property.Name) && Numeric.Contains(Nullable.GetUnderlyingType(p.property.PropertyType) ?? p.property.PropertyType)))
            .Select(p => $"{p.type.Name}.{p.property.Name}");

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_walk_reaches_the_levels_and_the_evidence()
    {
        var reached = Reachable(typeof(ApplicationDecisionRecord));

        reached.Should().Contain([typeof(ApplicationVerification), typeof(AuthenticityAssessmentView), typeof(VerificationSignalView)],
            "a check that never reached the evidence would pass whatever it held");
        typeof(VerificationSignalView).GetProperty(nameof(VerificationSignalView.Weight))!.PropertyType.IsEnum.Should().BeTrue(
            "the categorical rule has a field to hold, so it is not vacuous");
    }

    // Every ScreenHire type reachable through public properties, collections and nullables included.
    private static HashSet<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            if (type.Namespace?.StartsWith("ScreenHire", StringComparison.Ordinal) != true || type.IsEnum || !seen.Add(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var next in Unwrap(property.PropertyType))
                {
                    queue.Enqueue(next);
                }
            }

            // A polymorphic base (an automation action) carries its fields on its derived records.
            foreach (var derived in type.Assembly.GetTypes().Where(t => t.BaseType == type))
            {
                queue.Enqueue(derived);
            }
        }

        return seen;
    }

    private static IEnumerable<Type> Unwrap(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap)
        : type.IsArray ? Unwrap(type.GetElementType()!)
        : [type];
}
