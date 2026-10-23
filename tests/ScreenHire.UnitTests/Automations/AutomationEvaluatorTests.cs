using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Automations;

public sealed class AutomationEvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();
    private static readonly Guid ApplicationId = Guid.CreateVersion7();

    [Fact]
    public void A_trigger_fires_the_enabled_automations_on_its_stage_in_definition_order()
    {
        var workflow = HiringWorkflow.Default with
        {
            Automations =
            [
                Automation("screening-1", new AutomationTrigger(TriggerType.StageEntered, "screening")),
                Automation("interview", new AutomationTrigger(TriggerType.StageEntered, "interview")),
                Automation("disabled", new AutomationTrigger(TriggerType.StageEntered, "screening")) with { Enabled = false },
                Automation("screening-2", new AutomationTrigger(TriggerType.StageEntered, "screening")),
                Automation("exit", new AutomationTrigger(TriggerType.StageExited, "screening")),
            ],
        };

        AutomationEvaluator.Triggered(workflow, TriggerType.StageEntered, "screening").Select(a => a.Id).Should().Equal("screening-1", "screening-2");
    }

    // Replay determinism: the snapshot is rebuilt from the append-only timeline as it stood at the event, so tags and
    // moves that happened later do not change what a replayed (or late) evaluation sees.
    [Fact]
    public void Evaluation_is_deterministic_on_replay()
    {
        var submitted = Event(ApplicationEventType.Submitted, Start, new(ToStageId: "applied"));
        var tagged = Event(ApplicationEventType.TagAdded, Start.AddHours(1), new(Tag: "referral"));
        var moved = Event(ApplicationEventType.StageChanged, Start.AddHours(5), new("applied", "screening"));
        var automation = Automation("a", new AutomationTrigger(TriggerType.StageEntered, "screening"), Conditions(ConditionMatch.All,
            new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, "referral"),
            new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, "priority", Negate: true)));

        var atEvent = AutomationFacts.FromTimeline([submitted, tagged, moved], moved.Id, TriggerType.StageEntered, moved.OccurredAt);
        var later = AutomationFacts.FromTimeline(
            [submitted, tagged, moved, Event(ApplicationEventType.TagAdded, Start.AddHours(9), new(Tag: "priority")),
                Event(ApplicationEventType.StageChanged, Start.AddHours(10), new("screening", "interview"))],
            moved.Id, TriggerType.StageEntered, moved.OccurredAt);

        later.Should().BeEquivalentTo(atEvent);
        var first = AutomationEvaluator.Evaluate(automation, atEvent, accommodationRequested: false, []);
        var replay = AutomationEvaluator.Evaluate(automation, later, accommodationRequested: false, []);
        replay.Should().BeEquivalentTo(first);
        first.Outcome.Should().Be(EvaluationOutcome.Matched);
    }

    [Fact]
    public void Hours_in_stage_count_from_the_stage_entry_and_a_stage_exit_measures_the_stage_left()
    {
        var submitted = Event(ApplicationEventType.Submitted, Start, new(ToStageId: "applied"));
        var moved = Event(ApplicationEventType.StageChanged, Start.AddHours(30.5), new("applied", "screening"));

        AutomationFacts.FromTimeline([submitted, moved], moved.Id, TriggerType.StageExited, moved.OccurredAt).HoursInStage.Should().Be(30);
        AutomationFacts.FromTimeline([submitted, moved], moved.Id, TriggerType.StageEntered, moved.OccurredAt).HoursInStage.Should().Be(0);
        AutomationFacts.FromTimeline([submitted, moved], Guid.CreateVersion7(), TriggerType.TimeElapsed, Start.AddHours(78.5))
            .HoursInStage.Should().Be(48);
    }

    // A timer is keyed on its stage entry (that is how leaving the stage cancels it) but fires at its due time, so it sees
    // what happened while it waited, and nothing after.
    [Fact]
    public void A_timer_sees_the_tags_added_while_it_waited_and_counts_hours_from_the_stage_entry()
    {
        var submitted = Event(ApplicationEventType.Submitted, Start, new(ToStageId: "applied"));
        var moved = Event(ApplicationEventType.StageChanged, Start.AddHours(2), new("applied", "screening"));
        var reviewed = Event(ApplicationEventType.TagAdded, Start.AddHours(10), new(Tag: "reviewed"));
        var late = Event(ApplicationEventType.TagAdded, Start.AddHours(30), new(Tag: "late"));
        var due = moved.OccurredAt.AddHours(24);

        var facts = AutomationFacts.FromTimeline([submitted, moved, reviewed, late], moved.Id, TriggerType.TimeElapsed, due);

        facts.Tags.Should().Equal("reviewed");
        facts.HoursInStage.Should().Be(24);
    }

    // People remove tags by hand, so the snapshot replays additions and removals in order, ignoring case as tags do.
    [Fact]
    public void Tags_replay_in_order_so_a_removed_tag_is_gone_and_one_added_again_is_back()
    {
        var submitted = Event(ApplicationEventType.Submitted, Start, new(ToStageId: "applied"));
        var referral = Event(ApplicationEventType.TagAdded, Start.AddHours(1), new(Tag: "Referral"));
        var priority = Event(ApplicationEventType.TagAdded, Start.AddHours(2), new(Tag: "priority"));
        var removed = Event(ApplicationEventType.TagRemoved, Start.AddHours(3), new(Tag: "referral"));
        var priorityGone = Event(ApplicationEventType.TagRemoved, Start.AddHours(4), new(Tag: "PRIORITY"));
        var back = Event(ApplicationEventType.TagAdded, Start.AddHours(5), new(Tag: "referral"));
        var timeline = new[] { submitted, referral, priority, removed, priorityGone, back };

        AutomationFacts.FromTimeline(timeline, removed.Id, TriggerType.Manual, removed.OccurredAt).Tags.Should().Equal("priority");
        AutomationFacts.FromTimeline(timeline, back.Id, TriggerType.Manual, back.OccurredAt).Tags.Should().Equal("referral");
    }

    [Fact]
    public void An_automation_that_its_own_actions_led_back_to_is_blocked_as_a_cycle()
    {
        var evaluation = AutomationEvaluator.Evaluate(
            Automation("to-screening", new AutomationTrigger(TriggerType.StageEntered, "interview")), AutomationFacts.None, false,
            ["to-interview", "to-screening"]);

        evaluation.Outcome.Should().Be(EvaluationOutcome.Blocked);
        evaluation.Reason.Should().Contain("loop").And.Contain("to-screening <- to-interview <- to-screening");
    }

    [Fact]
    public void A_chain_stops_at_the_depth_limit()
    {
        var chain = Enumerable.Range(1, AutomationEvaluator.MaxDepth).Select(i => $"step-{i}").ToList();
        var automation = Automation("next", new AutomationTrigger(TriggerType.StageEntered, "screening"));

        AutomationEvaluator.Evaluate(automation, AutomationFacts.None, false, chain).Outcome.Should().Be(EvaluationOutcome.Blocked);
        AutomationEvaluator.Evaluate(automation, AutomationFacts.None, false, chain[..^1]).Outcome.Should().Be(EvaluationOutcome.Matched);
    }

    // Guardrail 4: timing facts are off for an application with an accommodation, so an automation that uses them is
    // skipped for it, and others still run.
    [Fact]
    public void An_accommodation_skips_automations_that_use_timing()
    {
        var timer = Automation("nudge", new AutomationTrigger(TriggerType.TimeElapsed, "screening", 48));
        var slow = Automation("slow", new AutomationTrigger(TriggerType.StageEntered, "screening"),
            Conditions(ConditionMatch.Any, new AutomationCondition(FactSchema.HoursInStage, ConditionOperator.GreaterThan, "0")));
        var plain = Automation("plain", new AutomationTrigger(TriggerType.StageEntered, "screening"));
        var facts = AutomationFacts.None with { HoursInStage = 72 };

        AutomationEvaluator.Evaluate(timer, facts, accommodationRequested: true, []).Outcome.Should().Be(EvaluationOutcome.Skipped);
        AutomationEvaluator.Evaluate(slow, facts, accommodationRequested: true, []).Outcome.Should().Be(EvaluationOutcome.Skipped);
        AutomationEvaluator.Evaluate(plain, facts, accommodationRequested: true, []).Outcome.Should().Be(EvaluationOutcome.Matched);
        AutomationEvaluator.Evaluate(slow, facts, accommodationRequested: false, []).Outcome.Should().Be(EvaluationOutcome.Matched);
    }

    // Telemetry facts arrive with the integrity engine; the skip already covers their prefix.
    [Fact]
    public void An_accommodation_skips_automations_that_use_telemetry()
    {
        FactSchema.IsTimingOrTelemetry("telemetry.pasteEvents").Should().BeTrue();
        FactSchema.IsTimingOrTelemetry(FactSchema.Tags).Should().BeFalse();
        var telemetry = Automation("paste", new AutomationTrigger(TriggerType.QuestionnaireCompleted),
            Conditions(ConditionMatch.All, new AutomationCondition("telemetry.pasteEvents", ConditionOperator.GreaterThan, "3")));

        var evaluation = AutomationEvaluator.Evaluate(telemetry, AutomationFacts.None, accommodationRequested: true, []);

        evaluation.Outcome.Should().Be(EvaluationOutcome.Skipped);
        evaluation.Reason.Should().Be("Not evaluated for this application.", "the record says nothing about why");
    }

    [Theory]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "no", false, true)]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "NO", false, true)]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "yes", false, false)]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "yes", true, true)]
    [InlineData("answers.shifts", ConditionOperator.Contains, "nights", false, true)]
    [InlineData("answers.experience", ConditionOperator.GreaterThan, "4", false, true)]
    [InlineData("answers.experience", ConditionOperator.LessThanOrEqual, "4.5", false, false)]
    [InlineData("answers.experience", ConditionOperator.EqualTo, "5.0", false, true)]
    [InlineData("answers.missing", ConditionOperator.Exists, null, false, false)]
    [InlineData("answers.missing", ConditionOperator.Exists, null, true, true)]
    [InlineData("application.tags", ConditionOperator.Contains, "refer", false, true)]
    [InlineData("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested", false, false)]
    public void Rules_read_facts_by_type(string fact, ConditionOperator op, string? value, bool negate, bool expected)
    {
        var facts = new AutomationFacts(
            [new("work_authorization", ["no"]), new("shifts", ["mornings", "nights"]), new("experience", ["5"])], ["referral"]);

        AutomationEvaluator.Holds(new AutomationCondition(fact, op, value, Negate: negate), facts).Should().Be(expected);
    }

    [Fact]
    public void In_matches_any_listed_value_and_any_matches_one_rule()
    {
        var facts = new AutomationFacts([new("shifts", ["weekends"])], []);
        var automation = Automation("a", new AutomationTrigger(TriggerType.QuestionnaireCompleted), Conditions(ConditionMatch.Any,
            new AutomationCondition("answers.shifts", ConditionOperator.In, Values: ["nights", "weekends"]),
            new AutomationCondition(FactSchema.Tags, ConditionOperator.Exists)));

        var evaluation = AutomationEvaluator.Evaluate(automation, facts, false, []);

        evaluation.Outcome.Should().Be(EvaluationOutcome.Matched);
        evaluation.Rules.Select(r => r.Held).Should().Equal(true, false);
    }

    [Fact]
    public void A_run_records_its_snapshot_and_a_pending_action_per_action_keyed_by_application_automation_and_event()
    {
        var eventId = Guid.CreateVersion7();
        var trigger = new AutomationTriggerEvent(Organization, ApplicationId, Guid.CreateVersion7(), TriggerType.StageEntered, "screening", eventId, Start);
        var automation = Automation("mail", trigger: new AutomationTrigger(TriggerType.StageEntered, "screening")) with
        {
            Actions = [new(ActionType.SendEmail, EmailTemplate: "stage-change"), new(ActionType.AddTag, Tag: "contacted")],
        };
        var evaluation = AutomationEvaluator.Evaluate(automation, AutomationFacts.None, false, []);

        var (run, actions) = AutomationRun.Record(trigger, automation, 1, AutomationFacts.None, evaluation, [], Start);

        run.Status.Should().Be(AutomationRunStatus.Running);
        actions.Select(a => (a.Index, a.Action.Type, a.Status)).Should().Equal(
            (0, ActionType.SendEmail, AutomationActionStatus.Pending), (1, ActionType.AddTag, AutomationActionStatus.Pending));
        run.ActionKey(1).Should().Be($"{ApplicationId}:mail:{eventId}:1");
    }

    private static Automation Automation(string id, AutomationTrigger trigger, AutomationConditions? conditions = null) =>
        new(id, $"Automation {id}", trigger, conditions, [new AutomationAction(ActionType.AddTag, Tag: "x")]);

    private static AutomationConditions Conditions(ConditionMatch match, params AutomationCondition[] rules) => new(match, rules);

    private static ApplicationEvent Event(ApplicationEventType type, DateTimeOffset at, ApplicationEventData data) =>
        new() { OrganizationId = Organization, ApplicationId = ApplicationId, Type = type, Data = data, OccurredAt = at };
}
