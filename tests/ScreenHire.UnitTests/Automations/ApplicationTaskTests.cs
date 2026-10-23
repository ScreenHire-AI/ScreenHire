using ScreenHire.Domain.Automations;

namespace ScreenHire.UnitTests.Automations;

public sealed class ApplicationTaskTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completing_records_who_and_when_once()
    {
        var task = new ApplicationTask { Kind = ApplicationTaskKind.Review, Note = "Review this application.", CreatedAt = Monday };
        var first = Guid.CreateVersion7();

        task.Complete(first, Monday.AddHours(1)).Should().BeTrue();
        task.Complete(Guid.CreateVersion7(), Monday.AddHours(2)).Should().BeFalse("a task that is done stays as it was");

        task.CompletedAt.Should().Be(Monday.AddHours(1));
        task.CompletedByUserId.Should().Be(first);
    }
}
