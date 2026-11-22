using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// A stage's scheduling default and the ScheduleInterview action's mode and option (docs/architecture/workflow-engine.md
/// §2): the shapes the validator refuses, and that an action never sends we'll suggest a time, which needs a person.
/// </summary>
public sealed class SchedulingDefinitionTests
{
    private static readonly Guid Avery = Guid.CreateVersion7();
    private static readonly Guid Team = Guid.CreateVersion7();

    private static IReadOnlyList<DefinitionError> ValidateStage(StageScheduling scheduling, StageKind kind = StageKind.Interview) =>
        DefinitionValidator.Validate(HiringWorkflow.Default with
        {
            Stages = [new("applied", "Applied", StageKind.Applied), new("talk", "Talk", kind, Scheduling: scheduling), new("hired", "Hired", StageKind.Hired)],
            Automations = [],
        });

    private static IReadOnlyList<DefinitionError> ValidateAction(AutomationAction action) =>
        DefinitionValidator.Validate(HiringWorkflow.Default with
        {
            Automations = [new("a", "Offer", new AutomationTrigger(TriggerType.StageEntered, HiringWorkflow.Default.Stages[1].Id), null, [action])],
        });

    [Fact]
    public void A_stage_default_with_a_team_a_length_days_ahead_and_blocks_is_valid()
    {
        var blocks = new WeeklyHours([new DayHours(DayOfWeek.Tuesday, "14:00", "17:00")]);

        ValidateStage(new StageScheduling(InterviewOption.OurTimes, InterviewerTeamId: Team, DurationMinutes: 30, WindowDays: 14, Hours: blocks, UseBlocks: true))
            .Should().BeEmpty();
        ValidateStage(new StageScheduling(InterviewOption.TheirTimes, InterviewerUserIds: [Avery])).Should().BeEmpty();
    }

    [Fact]
    public void A_stage_default_names_people_or_a_team_never_both()
    {
        ValidateStage(new StageScheduling(InterviewerUserIds: [Avery], InterviewerTeamId: Team))
            .Should().ContainSingle(e => e.Path == "stages[1].scheduling.interviewerTeamId");
        ValidateStage(new StageScheduling(InterviewerUserIds: []))
            .Should().ContainSingle(e => e.Path == "stages[1].scheduling.interviewerUserIds");
    }

    [Fact]
    public void A_stage_default_out_of_range_is_refused_at_its_field()
    {
        var errors = ValidateStage(new StageScheduling(DurationMinutes: 7, WindowDays: 0, UseBlocks: true));

        errors.Select(e => e.Path).Should().BeEquivalentTo("stages[1].scheduling.durationMinutes", "stages[1].scheduling.windowDays", "stages[1].scheduling.hours");
    }

    [Fact]
    public void Interview_blocks_are_for_pick_from_our_times_only()
    {
        var blocks = new WeeklyHours([new DayHours(DayOfWeek.Tuesday, "14:00", "17:00")]);

        ValidateStage(new StageScheduling(InterviewOption.TheirTimes, Hours: blocks, UseBlocks: true))
            .Should().ContainSingle(e => e.Path == "stages[1].scheduling.useBlocks");
    }

    [Theory]
    [InlineData(StageKind.Applied)]
    [InlineData(StageKind.Offer)]
    [InlineData(StageKind.Rejected)]
    public void A_stage_that_holds_no_interviews_has_no_scheduling_default(StageKind kind) =>
        ValidateStage(new StageScheduling(InterviewOption.TheirTimes), kind).Should().Contain(e => e.Path == "stages[1].scheduling");

    [Fact]
    public void An_action_sends_with_the_stage_option_or_its_own_but_never_suggests_times()
    {
        var send = new AutomationAction(ActionType.ScheduleInterview, InterviewerUserIds: [Avery], InterviewMinutes: 30, InterviewWindowDays: 14, InterviewTimeZone: "UTC");

        ValidateAction(send).Should().BeEmpty("an action without a mode sends, as every action did before modes");
        ValidateAction(send with { InterviewMode = InterviewMode.Send, InterviewOption = InterviewOption.TheirTimes }).Should().BeEmpty();
        ValidateAction(send with { InterviewOption = InterviewOption.OurSuggestion })
            .Should().ContainSingle(e => e.Path == "automations[0].actions[0].interviewOption").Which.Message.Should().Contain("a person picks them");
    }

    [Fact]
    public void An_action_that_creates_a_task_to_choose_carries_nothing_else()
    {
        var choose = new AutomationAction(ActionType.ScheduleInterview, InterviewMode: InterviewMode.Choose);

        ValidateAction(choose).Should().BeEmpty();
        ValidateAction(choose with { InterviewerUserIds = [Avery] })
            .Should().ContainSingle(e => e.Path == "automations[0].actions[0].interviewerUserIds").Which.Message.Should().Contain("is not used");
    }

    // Guardrail 1 (workflow-engine.md §2): a verification signal never offers an interview, and creating a task to choose
    // how is still offering one, so the new mode is refused like a send.
    [Theory]
    [InlineData(InterviewMode.Send)]
    [InlineData(InterviewMode.Choose)]
    public void A_verification_signal_cannot_schedule_an_interview_in_either_mode(InterviewMode mode)
    {
        var action = new AutomationAction(ActionType.ScheduleInterview, InterviewMode: mode);

        DefinitionValidator.Validate(HiringWorkflow.Default with
        {
            Automations = [new("a", "Offer", new AutomationTrigger(TriggerType.VerificationAssessed), null, [action])],
        }).Should().Contain(e => e.Path == "automations[0].actions[0]");
    }
}
