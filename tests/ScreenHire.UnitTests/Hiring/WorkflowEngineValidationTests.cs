using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// The validator rules the workflow engine added: the typed facts schema, the email templates, the kinds the engine does
/// not run yet, and the guardrails stated as the brief states them (a verification condition with Reject is refused).
/// </summary>
public sealed class WorkflowEngineValidationTests
{
    private static readonly Question WorkAuthorization = new(
        "work_authorization", QuestionType.YesNo, "Do you have the right to work here?", Required: true, Knockout: new Knockout(false));

    private static readonly Question Shifts = new(
        "shifts", QuestionType.MultiChoice, "Which shifts?", Options: [new("mornings", "Mornings"), new("nights", "Nights")]);

    private static readonly Question Experience = new("experience", QuestionType.Number, "Years of experience");

    [Fact]
    public void An_verification_condition_with_reject_is_refused_at_save()
    {
        var workflow = Workflow(automations:
        [
            new("reject-on-verification", "Reject on verification", new AutomationTrigger(TriggerType.VerificationAssessed),
                Rules(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "VerificationRecommended")),
                [new AutomationAction(ActionType.Reject, Reason: "Looks automated")],
                AllowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].actions[0]" && e.Message.Contains("never drive rejection"));
    }

    [Theory]
    [InlineData(ActionType.Reject)]
    [InlineData(ActionType.QueueForRejectionReview)]
    public void An_verification_condition_may_not_even_queue_a_rejection(ActionType type)
    {
        var workflow = Workflow(automations:
        [
            new("a", "A", new AutomationTrigger(TriggerType.VerificationAssessed),
                Rules(new AutomationCondition("verification.aiAssistance", ConditionOperator.In, Values: ["SomeIndicators", "StrongIndicators"])),
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
    [InlineData(TriggerType.VerificationAssessed)]
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
            .Which.Message.Should().Contain("application-received, application-rejected, questionnaire-request, stage-change");
    }

    [Fact]
    public void Only_a_reject_action_sends_the_rejection_email()
    {
        var workflow = Workflow([WorkAuthorization],
        [
            new("mailer", "Mailer", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
                [new AutomationAction(ActionType.SendEmail, EmailTemplate: "application-rejected")]),
            new("knockout", "Knockout", new AutomationTrigger(TriggerType.QuestionnaireCompleted),
                Rules(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
                [new AutomationAction(ActionType.Reject, EmailTemplate: "stage-change", Reason: "No right to work.")],
                AllowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().BeEquivalentTo(
            "automations[0].actions[0].emailTemplate", "automations[1].actions[0].emailTemplate");
    }

    // "Moved to the next step: Rejected" would be a rejection email no Reject action guarded.
    [Theory]
    [InlineData("rejected", true)]
    [InlineData("withdrawn", true)]
    [InlineData("screening", false)]
    public void The_stage_change_email_is_not_sent_on_entering_an_ended_stage(string stageId, bool refused)
    {
        var workflow = Workflow(automations:
        [
            new("mailer", "Mailer", new AutomationTrigger(TriggerType.StageEntered, stageId), null,
                [new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change")]),
        ]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should()
            .BeEquivalentTo(refused ? ["automations[0].actions[0].emailTemplate"] : Array.Empty<string>());
    }

    // The validator judges a tainted tag with the evaluator's own comparison, so a rule the worker would match ("1.0"
    // equals the tag "1" as a number) cannot slip past the guardrail.
    [Fact]
    public void A_signal_tag_read_through_a_numeric_match_still_counts_as_signal_driven()
    {
        var workflow = Workflow(automations:
        [
            new("tagger", "Tagger", new AutomationTrigger(TriggerType.VerificationAssessed),
                Rules(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                [new AutomationAction(ActionType.AddTag, Tag: "1")]),
            new("queue", "Queue", new AutomationTrigger(TriggerType.QuestionnaireCompleted),
                Rules(new AutomationCondition("application.tags", ConditionOperator.In, Values: ["1.0"])),
                [new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Flagged")]),
        ]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().Equal("automations[1].actions[0]");
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
    [InlineData("verification.authenticity", ConditionOperator.GreaterThanOrEqual, "ReviewSuggested", "operator")]
    public void Conditions_fit_the_type_of_their_fact(string fact, ConditionOperator op, string value, string member)
    {
        // AddTag is safe even for a verification fact, so the only error is the condition's.
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
