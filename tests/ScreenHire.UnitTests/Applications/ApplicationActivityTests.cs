using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Applications;

public sealed class ApplicationActivityTests
{
    [Fact]
    public void Every_timeline_entry_type_either_counts_as_activity_or_is_named_as_not_counting()
    {
        var classified = ApplicationActivity.CountedTypes.Concat(ApplicationActivity.NotCounted).ToList();

        classified.Should().OnlyHaveUniqueItems("a type is in one set only");
        classified.Should().BeEquivalentTo(Enum.GetValues<ApplicationEventType>(), "a new timeline entry type is a decision about last activity");
    }

    [Fact]
    public void The_verification_engine_never_moves_last_activity() =>
        ApplicationActivity.CountedTypes.Should().NotContain(ApplicationEventType.VerificationAssessed);

    [Fact]
    public void Last_activity_is_the_latest_of_the_submission_the_timeline_and_finished_tasks()
    {
        var submitted = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

        ApplicationActivity.LastAt(submitted, null, null).Should().Be(submitted);
        ApplicationActivity.LastAt(submitted, submitted.AddHours(2), submitted.AddHours(1)).Should().Be(submitted.AddHours(2));
        ApplicationActivity.LastAt(submitted, submitted.AddHours(1), submitted.AddDays(1)).Should().Be(submitted.AddDays(1));
        ApplicationActivity.LastAt(submitted, submitted.AddHours(-1), null).Should().Be(submitted, "never earlier than the submission");
    }
}
