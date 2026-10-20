using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// The validator rules the workflow engine added: the typed facts schema, the email templates, the kinds the engine does
/// not run yet, and the guardrails stated as the brief states them (an integrity condition with Reject is refused).
/// </summary>
public sealed class WorkflowEngineValidationTests
{
    private static readonly Question WorkAuthorization = new(
        "work_authorization", QuestionType.YesNo, "Do you have the right to work here?", Required: true, Knockout: new Knockout(false));

    private static readonly Question Shifts = new(
        "shifts", QuestionType.MultiChoice, "Which shifts?", Options: [new("mornings", "Mornings"), new("nights", "Nights")]);

    private static readonly Question Experience = new("experience", QuestionType.Number, "Years of experience");

    [Fact]
    public void An_integrity_condition_with_reject_is_refused_at_save()
    {
        var workflow = Workflow(automations:
        [
            new("reject-on-integrity", "Reject on integrity", new AutomationTrigger(TriggerType.IntegrityAssessed),
                Rules(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "VerificationRecommended")),
                [new AutomationAction(ActionType.Reject, Reason: "Looks automated")],
                AllowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].actions[0]" && e.Message.Contains("never drive rejection"));
    }

    [Theory]
    [InlineData(ActionType.Reject)]
    [InlineData(ActionType.QueueForRejectionReview)]
    public void An_integrity_condition_may_not_even_queue_a_rejection(ActionType type)
    {
        var workflow = Workflow(automations:
        [
            new("a", "A", new AutomationTrigger(TriggerType.IntegrityAssessed),
                Rules(new AutomationCondition("integrity.aiAssistance", ConditionOperator.In, Values: ["SomeIndicators", "StrongIndicators"])),
                [new AutomationAction(type, Reason: "AI use")]),
        ]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().Equal("automations[0].actions[0]");
    }

    // The knockout default (workflow-engine.md §2, guardrail 2): queueing for review needs nothing; rejecting needs the
    // per-rule opt-in, and the error points at the safe default.
    [Fact]
    public void A_knockout_queues_for_review_by_default_and_rejects_only_with_the_opt_in()
    {
        Automation Knockout(ActionType type, bool optIn) => new(
            "knockout", "Right to work", new AutomationTrigger(TriggerType.QuestionnaireCompleted),
            Rules(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
            [new AutomationAction(type, Reason: "This role needs the right to work here.")],
            AllowAutoReject: optIn);

        DefinitionValidator.Validate(Workflow([WorkAuthorization], [Knockout(ActionType.QueueForRejectionReview, optIn: false)])).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow([WorkAuthorization], [Knockout(ActionType.Reject, optIn: false)])).Should().ContainSingle()
            .Which.Message.Should().Contain("explicit opt-in").And.Contain("QueueForRejectionReview");
        DefinitionValidator.Validate(Workflow([WorkAuthorization], [Knockout(ActionType.Reject, optIn: true)])).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ActionType.RequestVerification)]
    [InlineData(ActionType.SendQuestionnaire)]
    [InlineData(ActionType.ScheduleInterview)]
    public void Actions_the_engine_does_not_run_yet_are_refused(ActionType type)
    {
        var action = type == ActionType.ScheduleInterview ? new AutomationAction(type, Url: "https://example.com/book") : new AutomationAction(type);
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(TriggerType.ApplicationSubmitted), null, [action])]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0].type")
            .Which.Message.Should().Contain("cannot be used yet");
    }

    [Theory]
    [InlineData(TriggerType.VerificationCompleted)]
    [InlineData(TriggerType.CandidateReplied)]
    [InlineData(TriggerType.InterviewScheduled)]
    public void Triggers_nothing_raises_yet_are_refused(TriggerType type)
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(type), null, [new AutomationAction(ActionType.AddTag, Tag: "x")])]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().Contain("automations[0].trigger.type");
    }

    [Theory]
    [InlineData(TriggerType.ApplicationSubmitted)]
    [InlineData(TriggerType.QuestionnaireCompleted)]
    [InlineData(TriggerType.IntegrityAssessed)]
    [InlineData(TriggerType.ReviewerDecision)]
    [InlineData(TriggerType.Manual)]
    public void The_v1_triggers_without_a_stage_are_accepted(TriggerType type)
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(type), null, [new AutomationAction(ActionType.AddTag, Tag: "x")])]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void Email_actions_name_a_template_that_exists()
    {
        var workflow = Workflow(automations:
        [
            new("a", "A", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
                [new AutomationAction(ActionType.SendEmail, EmailTemplate: "follow-up"), new AutomationAction(ActionType.SendEmail, EmailTemplate: "application-received")]),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0].emailTemplate")
            .Which.Message.Should().Contain("application-received, application-rejected, stage-change");
    }

    // The typed facts schema: each fact type takes the operators that make sense for it, and operands it can hold.
    [Theory]
    [InlineData("answers.work_authorization", ConditionOperator.GreaterThan, "no", "operator")]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "maybe", "value")]
    [InlineData("answers.shifts", ConditionOperator.Contains, "Mornings", "value")]
    [InlineData("answers.shifts", ConditionOperator.EqualTo, "mornings", "operator")]
    [InlineData("answers.experience", ConditionOperator.GreaterThan, "five", "value")]
    [InlineData("application.tags", ConditionOperator.GreaterThan, "x", "operator")]
    [InlineData("application.hoursInStage", ConditionOperator.Contains, "4", "operator")]
    [InlineData("integrity.authenticity", ConditionOperator.GreaterThanOrEqual, "ReviewSuggested", "operator")]
    public void Conditions_fit_the_type_of_their_fact(string fact, ConditionOperator op, string value, string member)
    {
        // AddTag is safe even for an integrity fact, so the only error is the condition's.
        var workflow = Workflow([WorkAuthorization, Shifts, Experience],
        [
            new("a", "A", new AutomationTrigger(TriggerType.QuestionnaireCompleted), Rules(new AutomationCondition(fact, op, value)),
                [new AutomationAction(ActionType.AddTag, Tag: "look")]),
        ]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().Equal($"automations[0].conditions.rules[0].{member}");
    }

    [Theory]
    [InlineData("answers.work_authorization", ConditionOperator.EqualTo, "yes")]
    [InlineData("answers.shifts", ConditionOperator.Contains, "nights")]
    [InlineData("answers.experience", ConditionOperator.GreaterThanOrEqual, "2.5")]
    [InlineData("application.tags", ConditionOperator.Contains, "referral")]
    [InlineData("application.source", ConditionOperator.EqualTo, "careers")]
    [InlineData("application.hoursInStage", ConditionOperator.GreaterThan, "48")]
    public void Well_typed_conditions_are_accepted(string fact, ConditionOperator op, string value)
    {
        var workflow = Workflow([WorkAuthorization, Shifts, Experience],
        [
            new("a", "A", new AutomationTrigger(TriggerType.QuestionnaireCompleted), Rules(new AutomationCondition(fact, op, value)),
                [new AutomationAction(ActionType.NotifyTeam, Note: "Look")]),
        ]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    private static AutomationConditions Rules(params AutomationCondition[] rules) => new(ConditionMatch.All, rules);

    private static HiringWorkflow Workflow(IReadOnlyList<Question>? questions = null, IReadOnlyList<Automation>? automations = null) =>
        HiringWorkflow.Default with { Questionnaire = new Questionnaire(questions ?? []), Automations = automations ?? [] };
}
