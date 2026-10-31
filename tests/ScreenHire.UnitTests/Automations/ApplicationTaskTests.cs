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

    [Theory]
    [InlineData(ApplicationTaskKind.Task, true)]
    [InlineData(ApplicationTaskKind.RejectionReview, true)]
    [InlineData(ApplicationTaskKind.Review, false)]
    [InlineData(ApplicationTaskKind.Scorecard, false)]
    public void Only_a_to_do_and_a_rejection_review_are_ticked_off_by_hand(ApplicationTaskKind kind, bool byHand) =>
        new ApplicationTask { Kind = kind, Note = "Something to do.", CreatedAt = Monday }.CompletesByHand.Should().Be(byHand);

    [Fact]
    public void A_review_is_finished_by_any_scorecard_of_its_assignee_and_never_by_someone_else_s()
    {
        var assignee = Guid.CreateVersion7();
        var review = new ApplicationTask { Kind = ApplicationTaskKind.Review, Note = "Review this application.", AssigneeUserId = assignee, CreatedAt = Monday };

        review.CompletesWith(assignee, "interview").Should().BeTrue();
        review.CompletesWith(assignee, null).Should().BeTrue("a stage copy that names no library scorecard is still a scorecard");
        review.CompletesWith(Guid.CreateVersion7(), "interview").Should().BeFalse("it is someone else's task");
    }

    [Fact]
    public void A_review_for_the_whole_team_is_finished_by_anyone_s_scorecard()
    {
        var review = new ApplicationTask { Kind = ApplicationTaskKind.Review, Note = "Review this application.", CreatedAt = Monday };

        review.CompletesWith(Guid.CreateVersion7(), "screening").Should().BeTrue();
    }

    [Fact]
    public void A_requested_scorecard_is_finished_only_by_its_assignee_giving_that_scorecard()
    {
        var assignee = Guid.CreateVersion7();
        var request = new ApplicationTask
        {
            Kind = ApplicationTaskKind.Scorecard, Note = "Fill in the Interview scorecard.", AssigneeUserId = assignee, ScorecardTemplateId = "interview", CreatedAt = Monday,
        };

        request.CompletesWith(assignee, "interview").Should().BeTrue();
        request.CompletesWith(assignee, "screening").Should().BeFalse("another scorecard is not the one asked for");
        request.CompletesWith(assignee, null).Should().BeFalse();
        request.CompletesWith(Guid.CreateVersion7(), "interview").Should().BeFalse("it is someone else's task");
    }

    [Fact]
    public void A_done_task_and_a_task_done_by_hand_are_never_finished_by_a_scorecard()
    {
        var assignee = Guid.CreateVersion7();
        var review = new ApplicationTask { Kind = ApplicationTaskKind.Review, Note = "Review this application.", AssigneeUserId = assignee, CreatedAt = Monday };
        review.Complete(assignee, Monday);
        var toDo = new ApplicationTask { Kind = ApplicationTaskKind.Task, Note = "Call the references.", AssigneeUserId = assignee, CreatedAt = Monday };

        review.CompletesWith(assignee, "interview").Should().BeFalse();
        toDo.CompletesWith(assignee, "interview").Should().BeFalse();
    }

    [Theory]
    [InlineData("Interview", null, "Fill in the Interview scorecard.")]
    [InlineData("Culture add scorecard", null, "Fill in the Culture add scorecard.")]
    [InlineData("Interview", "  Focus on system design. ", "Fill in the Interview scorecard. Focus on system design.")]
    [InlineData("Interview", "   ", "Fill in the Interview scorecard.")]
    public void A_requested_scorecard_s_task_names_the_scorecard_then_the_note(string name, string? note, string expected) =>
        ApplicationTask.ScorecardRequestNote(name, note).Should().Be(expected);
}
