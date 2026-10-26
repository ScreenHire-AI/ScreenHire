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

    [Fact]
    public void Reopening_clears_who_finished_and_leaves_an_open_task_as_it_is()
    {
        var task = new ApplicationTask { Kind = ApplicationTaskKind.Task, Note = "Call the references.", CreatedAt = Monday };
        task.Reopen().Should().BeFalse("an open task has nothing to undo");

        task.Complete(Guid.CreateVersion7(), Monday.AddHours(1));
        task.Reopen().Should().BeTrue();

        task.CompletedAt.Should().BeNull();
        task.CompletedByUserId.Should().BeNull();
        var again = Guid.CreateVersion7();
        task.Complete(again, Monday.AddHours(2)).Should().BeTrue("a reopened task can be done again");
        task.CompletedByUserId.Should().Be(again);
    }
}
