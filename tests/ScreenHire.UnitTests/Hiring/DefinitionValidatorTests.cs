using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

public sealed class DefinitionValidatorTests
{
    private static readonly Question WorkAuthorization = new(
        "work_authorization", QuestionType.YesNo, "Do you have the right to work here?", Required: true, Knockout: new Knockout(false));

    private static readonly Question Portfolio = new("portfolio", QuestionType.Url, "Portfolio");

    [Fact]
    public void The_default_definition_and_workflow_are_valid()
    {
        DefinitionValidator.Validate(HiringTemplateDefinition.Default).Should().BeEmpty();
        DefinitionValidator.Validate(HiringWorkflow.Default).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_definition_or_workflow_is_an_error_not_an_exception()
    {
        DefinitionValidator.Validate((HiringTemplateDefinition?)null).Should().ContainSingle();
        DefinitionValidator.Validate(new HiringTemplateDefinition(null!, null!)).Select(e => e.Path)
            .Should().BeEquivalentTo("workflow", "postingDefaults");
        DefinitionValidator.Validate(new HiringWorkflow(null!, null!, null!, null!)).Select(e => e.Path)
            .Should().BeEquivalentTo("stages", "questionnaire", "integrityPolicy", "automations");
    }

    [Fact]
    public void Applications_start_in_the_one_applied_stage_which_comes_first()
    {
        var screeningFirst = Workflow(stages: [new("screening", "Screening", StageKind.Screening), new("applied", "Applied", StageKind.Applied)]);
        var twoApplied = Workflow(stages: [new("applied", "Applied", StageKind.Applied), new("again", "Again", StageKind.Applied)]);

        Paths(screeningFirst).Should().Contain("stages[0].kind");
        Paths(twoApplied).Should().Contain("stages");
    }

    [Theory]
    [InlineData("Screening", "uppercase")]
    [InlineData("1st", "starts with a digit")]
    [InlineData("", "empty")]
    [InlineData("a-very-long-stage-identifier-that-goes-on-and-on", "over 40 characters")]
    public void Ids_are_short_lowercase_keys(string id, string because)
    {
        var workflow = Workflow(stages: [new("applied", "Applied", StageKind.Applied), new(id, "Stage", StageKind.Custom)]);

        Paths(workflow).Should().Contain("stages[1].id", because);
    }

    [Fact]
    public void Ids_are_unique_within_their_list()
    {
        var workflow = Workflow(
            stages: [new("applied", "Applied", StageKind.Applied), new("applied", "Again", StageKind.Screening)],
            questions: [Portfolio, Portfolio with { Label = "Another" }]);

        Paths(workflow).Should().Contain(["stages[1].id", "questionnaire.questions[1].id"]);
    }

    [Fact]
    public void Each_question_type_takes_only_its_own_settings()
    {
        Question[] questions =
        [
            new("choice", QuestionType.SingleChoice, "Pick one", Options: [new("only", "Only")]),
            new("text", QuestionType.ShortText, "Text", Options: [new("a", "A"), new("b", "B")], Min: 1),
            new("number", QuestionType.Number, "Number", Min: 10, Max: 5),
            new("long", QuestionType.LongText, "Long", MaxLength: 20_000),
            new("url", QuestionType.Url, "Link", Knockout: new Knockout(true)),
            new("file", QuestionType.File, "File", AcceptedFileTypes: ["pdf"]),
        ];

        Paths(Workflow(questions: questions)).Should().BeEquivalentTo(
            "questionnaire.questions[0].options",
            "questionnaire.questions[1].options",
            "questionnaire.questions[1].min",
            "questionnaire.questions[2].max",
            "questionnaire.questions[3].maxLength",
            "questionnaire.questions[4].knockout",
            "questionnaire.questions[5].acceptedFileTypes");
    }

    [Fact]
    public void Every_question_type_validates_when_set_up_correctly()
    {
        Question[] questions =
        [
            new("short", QuestionType.ShortText, "Short", Required: true, MaxLength: 100),
            new("long", QuestionType.LongText, "Long", MaxLength: 5000),
            new("single", QuestionType.SingleChoice, "One", Options: [new("a", "A"), new("b", "B")]),
            new("multi", QuestionType.MultiChoice, "Many", Options: [new("a", "A"), new("b", "B"), new("c", "C")]),
            WorkAuthorization,
            new("number", QuestionType.Number, "Years", Min: 0, Max: 50),
            Portfolio,
            new("resume", QuestionType.File, "CV", Required: true, AcceptedFileTypes: [".pdf", ".docx"]),
        ];

        DefinitionValidator.Validate(Workflow(questions: questions)).Should().BeEmpty();
    }

    [Fact]
    public void Conditions_test_known_facts_and_existing_questions()
    {
        var workflow = Workflow(questions: [Portfolio], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.missing", ConditionOperator.Exists)), new AutomationAction(ActionType.AddTag, Tag: "x")),
            Automation(Conditions(new AutomationCondition("candidate.mood", ConditionOperator.EqualTo, "calm")), new AutomationAction(ActionType.AddTag, Tag: "x"), "b"),
            Automation(Conditions(new AutomationCondition("answers.portfolio", ConditionOperator.Exists)), new AutomationAction(ActionType.AddTag, Tag: "x"), "c"),
        ]);

        var errors = DefinitionValidator.Validate(workflow);

        errors.Select(e => e.Path).Should().BeEquivalentTo("automations[0].conditions.rules[0].fact", "automations[1].conditions.rules[0].fact");
        errors[0].Message.Should().Contain("missing");
    }

    [Fact]
    public void Accommodation_is_not_a_fact_any_condition_can_test()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("application.accommodationRequested", ConditionOperator.EqualTo, "true")),
                new AutomationAction(ActionType.AddTag, Tag: "x")),
        ]);

        Paths(workflow).Should().Contain("automations[0].conditions.rules[0].fact");
    }

    [Fact]
    public void Operators_take_the_operands_they_need()
    {
        var workflow = Workflow(questions: [Portfolio], automations:
        [
            Automation(Conditions(
                new AutomationCondition("answers.portfolio", ConditionOperator.EqualTo),
                new AutomationCondition("answers.portfolio", ConditionOperator.In, "x"),
                new AutomationCondition("answers.portfolio", ConditionOperator.Exists, "x"),
                new AutomationCondition("answers.portfolio", ConditionOperator.In, Values: ["a", "b"])),
                new AutomationAction(ActionType.AddTag, Tag: "x")),
        ]);

        Paths(workflow).Should().BeEquivalentTo(
            "automations[0].conditions.rules[0].value",
            "automations[0].conditions.rules[1].values",
            "automations[0].conditions.rules[2]");
    }

    [Fact]
    public void Triggers_reference_existing_stages_and_only_time_triggers_wait()
    {
        var workflow = Workflow(automations:
        [
            Automation(null, new AutomationAction(ActionType.AddTag, Tag: "x"), "a", new AutomationTrigger(TriggerType.StageEntered, "nowhere")),
            Automation(null, new AutomationAction(ActionType.AddTag, Tag: "x"), "b", new AutomationTrigger(TriggerType.TimeElapsed, "screening")),
            Automation(null, new AutomationAction(ActionType.AddTag, Tag: "x"), "c", new AutomationTrigger(TriggerType.ApplicationSubmitted, "screening", 5)),
            Automation(null, new AutomationAction(ActionType.AddTag, Tag: "x"), "d", new AutomationTrigger(TriggerType.TimeElapsed, "screening", 72)),
        ]);

        Paths(workflow).Should().BeEquivalentTo(
            "automations[0].trigger.stageId",
            "automations[1].trigger.afterHours",
            "automations[2].trigger.stageId",
            "automations[2].trigger.afterHours");
    }

    [Fact]
    public void Each_action_type_needs_its_fields_and_takes_no_others()
    {
        var workflow = Workflow(automations:
        [
            Automation(null, new AutomationAction(ActionType.MoveToStage), "a"),
            Automation(null, new AutomationAction(ActionType.AddTag, StageId: "screening", Tag: "x"), "b"),
            Automation(null, new AutomationAction(ActionType.SendEmail), "c"),
            Automation(null, new AutomationAction(ActionType.CallWebhook, Url: "http://example.com/hook"), "d"),
            Automation(null, new AutomationAction(ActionType.QueueForRejectionReview), "e"),
            Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "nowhere"), "f"),
        ]);

        Paths(workflow).Should().BeEquivalentTo(
            "automations[0].actions[0].stageId",
            "automations[1].actions[0].stageId",
            "automations[2].actions[0].emailTemplate",
            "automations[3].actions[0].url",
            "automations[4].actions[0].reason",
            "automations[5].actions[0].stageId");
    }

    // Guardrail 1 (ADR-0004, workflow-engine.md §2): integrity and verification signals may only route and step up.
    [Theory]
    [InlineData(ActionType.Reject)]
    [InlineData(ActionType.QueueForRejectionReview)]
    [InlineData(ActionType.SendEmail)]
    [InlineData(ActionType.NotifyTeam)]
    [InlineData(ActionType.CallWebhook)]
    public void An_integrity_condition_cannot_drive_anything_but_routing(ActionType action)
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                Action(action), allowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("ADR-0004");
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("hired")]
    public void An_integrity_condition_cannot_move_an_application_to_a_terminal_stage(string stage)
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.aiAssistance", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.MoveToStage, StageId: stage)),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void An_integrity_trigger_counts_as_a_signal_even_without_conditions()
    {
        var workflow = Workflow(automations:
        [
            Automation(null, new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Looks automated"), "a",
                new AutomationTrigger(TriggerType.IntegrityAssessed)),
            Automation(null, new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change"), "b",
                new AutomationTrigger(TriggerType.VerificationCompleted)),
        ]);

        // VerificationCompleted is refused until phase 2, but it still counts as a signal: both errors show.
        Paths(workflow).Should().BeEquivalentTo("automations[0].actions[0]", "automations[1].trigger.type", "automations[1].actions[0]");
    }

    [Fact]
    public void A_verification_result_is_a_signal_too()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.result", ConditionOperator.EqualTo, "failed")),
                new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Verification failed")),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void Integrity_signals_may_tag_assign_or_move_to_a_review_stage()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
            [
                new AutomationAction(ActionType.AddTag, Tag: "integrity-review"),
                new AutomationAction(ActionType.AssignReviewer),
                new AutomationAction(ActionType.MoveToStage, StageId: "screening"),
            ], trigger: new AutomationTrigger(TriggerType.IntegrityAssessed)),
        ]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void An_integrity_condition_mixed_with_a_knockout_still_cannot_reject()
    {
        var workflow = Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(
                new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no"),
                new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.Reject, Reason: "Not eligible"), allowAutoReject: true),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void A_signal_cannot_be_laundered_through_a_tag()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.AddTag, Tag: "Integrity-Review"), "tagger", new AutomationTrigger(TriggerType.IntegrityAssessed)),
            Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "integrity-review")),
                new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change"), "mailer"),
            Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "referral")),
                new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change"), "unrelated"),
        ]);

        Paths(workflow).Should().Equal("automations[1].actions[0]");
    }

    [Fact]
    public void A_signal_cannot_be_laundered_through_a_stage_even_along_a_chain()
    {
        var workflow = Workflow(automations:
        [
            Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "screening"), "route",
                new AutomationTrigger(TriggerType.IntegrityAssessed)),
            Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "interview"), "hop",
                new AutomationTrigger(TriggerType.StageEntered, "screening")),
            Automation(null, new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Stalled"), "queue",
                new AutomationTrigger(TriggerType.TimeElapsed, "interview", 72)),
        ]);

        Paths(workflow).Should().Equal("automations[2].actions[0]");
    }

    // A signal-driven move also fires StageExited on the stage it leaves, which can be any stage the application is in
    // when the action runs. So once a signal moves applications, every StageExited automation counts as signal-driven.
    [Fact]
    public void A_signal_cannot_be_laundered_through_the_stage_it_moves_an_application_out_of()
    {
        var workflow = Workflow(
            stages: [.. HiringWorkflow.Default.Stages, new WorkflowStage("verify", "Verification", StageKind.Verification),
                new WorkflowStage("parked", "Not progressing", StageKind.Custom)],
            automations:
            [
                Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "verify"), "route",
                    new AutomationTrigger(TriggerType.IntegrityAssessed)),
                Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "parked"), "park",
                    new AutomationTrigger(TriggerType.StageExited, "screening")),
            ]);

        AssertRefusedAsSignalDriven(workflow, "automations[1].actions[0]");
    }

    [Fact]
    public void A_stage_exit_automation_is_ordinary_when_no_signal_moves_applications()
    {
        var workflow = Workflow(
            stages: [.. HiringWorkflow.Default.Stages, new WorkflowStage("parked", "Not progressing", StageKind.Custom)],
            automations:
            [
                Automation(null, new AutomationAction(ActionType.AddTag, Tag: "integrity-review"), "tag",
                    new AutomationTrigger(TriggerType.IntegrityAssessed)),
                Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "parked"), "park",
                    new AutomationTrigger(TriggerType.StageExited, "screening")),
            ]);

        Paths(workflow).Should().BeEmpty();
    }

    // A tag a signal sets is read through application.tags by substring (Contains) or by list (In). Either way the reader
    // is signal-driven, so it may not reject or move to a rejected stage.
    [Theory]
    [InlineData(ConditionOperator.Contains, ActionType.Reject)]
    [InlineData(ConditionOperator.Contains, ActionType.MoveToStage)]
    [InlineData(ConditionOperator.In, ActionType.Reject)]
    [InlineData(ConditionOperator.In, ActionType.MoveToStage)]
    public void A_signal_tag_read_by_contains_or_in_cannot_drive_a_rejection(ConditionOperator op, ActionType action)
    {
        var readsTag = op == ConditionOperator.Contains
            ? new AutomationCondition("application.tags", op, "integrity")
            : new AutomationCondition("application.tags", op, Values: ["referral", "integrity-review"]);
        var reject = action == ActionType.Reject
            ? new AutomationAction(ActionType.Reject, Reason: "Not a fit")
            : new AutomationAction(ActionType.MoveToStage, StageId: "rejected");
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.AddTag, Tag: "Integrity-Review"), "tagger", new AutomationTrigger(TriggerType.IntegrityAssessed)),
            Automation(Conditions(readsTag), reject, "decliner", allowAutoReject: true),
        ]);

        AssertRefusedAsSignalDriven(workflow, "automations[1].actions[0]");
    }

    // A stage a signal routes into is signal-driven for every trigger that fires from it, not only StageEntered.
    [Theory]
    [InlineData(TriggerType.StageExited, null)]
    [InlineData(TriggerType.TimeElapsed, 48)]
    public void A_stage_a_signal_routes_into_cannot_trigger_a_rejection(TriggerType trigger, int? afterHours)
    {
        var workflow = Workflow(
            stages: [.. HiringWorkflow.Default.Stages, new WorkflowStage("verify", "Verification", StageKind.Verification)],
            automations:
            [
                Automation(Conditions(new AutomationCondition("verification.result", ConditionOperator.EqualTo, "failed")),
                    new AutomationAction(ActionType.MoveToStage, StageId: "verify"), "route", new AutomationTrigger(TriggerType.IntegrityAssessed)),
                Automation(null, new AutomationAction(ActionType.Reject, Reason: "Stalled in verification"), "decliner",
                    new AutomationTrigger(trigger, "verify", afterHours), allowAutoReject: true),
            ]);

        AssertRefusedAsSignalDriven(workflow, "automations[1].actions[0]");
    }

    [Fact]
    public void A_signal_cannot_move_an_application_straight_to_a_withdrawn_stage()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("integrity.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.MoveToStage, StageId: "withdrawn"), "a", new AutomationTrigger(TriggerType.IntegrityAssessed),
                allowAutoReject: true),
        ]);

        AssertRefusedAsSignalDriven(workflow, "automations[0].actions[0]");
    }

    [Theory]
    [InlineData(StageKind.Custom)]
    [InlineData(StageKind.Offer)]
    public void A_signal_may_only_route_into_a_review_stage(StageKind kind)
    {
        var workflow = Workflow(
            stages: [.. HiringWorkflow.Default.Stages, new WorkflowStage("parked", "Not progressing", kind)],
            automations:
            [
                Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "parked"), "a",
                    new AutomationTrigger(TriggerType.IntegrityAssessed)),
            ]);

        Paths(workflow).Should().Equal("automations[0].actions[0]");
    }

    [Theory]
    [InlineData(ConditionOperator.EqualTo, "yes", false)]
    [InlineData(ConditionOperator.EqualTo, "no", true)]
    [InlineData(ConditionOperator.Exists, null, false)]
    public void A_knockout_rejection_tests_exactly_the_disqualifying_answer(ConditionOperator op, string? value, bool negate)
    {
        var workflow = Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.work_authorization", op, value, Negate: negate)),
                new AutomationAction(ActionType.Reject, Reason: "Not eligible"), allowAutoReject: true),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void Accommodation_and_the_ai_use_declaration_are_not_facts()
    {
        DefinitionValidator.KnownFacts.Should().NotContain(f =>
            f.Contains("accommodation", StringComparison.OrdinalIgnoreCase) || f.Contains("aiUse", StringComparison.OrdinalIgnoreCase));
    }

    // Guardrail 2: only knockouts may reject, and only with the per-rule opt-in.
    [Fact]
    public void A_knockout_automation_may_reject_only_with_the_explicit_opt_in()
    {
        var withoutOptIn = KnockoutReject(allowAutoReject: false);
        var withOptIn = KnockoutReject(allowAutoReject: true);

        Paths(withoutOptIn).Should().Equal("automations[0].allowAutoReject");
        DefinitionValidator.Validate(withOptIn).Should().BeEmpty();
    }

    [Fact]
    public void A_knockout_automation_queues_for_review_without_any_opt_in()
    {
        var workflow = Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
                new AutomationAction(ActionType.QueueForRejectionReview, Reason: "No right to work")),
        ]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void Rejecting_on_anything_but_knockout_answers_is_refused_even_with_the_opt_in()
    {
        var workflow = Workflow(questions: [Portfolio], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.portfolio", ConditionOperator.Exists, Negate: true)),
                new AutomationAction(ActionType.Reject, Reason: "No portfolio"), "a", allowAutoReject: true),
            Automation(null, new AutomationAction(ActionType.Reject, Reason: "Everyone"), "b", allowAutoReject: true),
            Automation(Conditions(new AutomationCondition("application.hoursInStage", ConditionOperator.GreaterThan, "72")),
                new AutomationAction(ActionType.MoveToStage, StageId: "rejected"), "c", allowAutoReject: true),
        ]);

        Paths(workflow).Should().BeEquivalentTo("automations[0].actions[0]", "automations[1].actions[0]", "automations[2].actions[0]");
    }

    [Fact]
    public void A_knockout_rejection_says_why_instead_of_moving_to_a_rejected_stage()
    {
        var workflow = Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
                new AutomationAction(ActionType.MoveToStage, StageId: "rejected"), allowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("reason the candidate sees");
    }

    [Fact]
    public void Posting_defaults_are_checked()
    {
        var definition = HiringTemplateDefinition.Default with
        {
            PostingDefaults = new PostingDefaults(SalaryCurrency: "pounds", ValidForDays: 400, Department: " "),
        };

        DefinitionValidator.Validate(definition).Select(e => e.Path).Should().BeEquivalentTo(
            "postingDefaults.salaryCurrency", "postingDefaults.validForDays", "postingDefaults.department");
    }

    private static HiringWorkflow KnockoutReject(bool allowAutoReject) =>
        Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
                new AutomationAction(ActionType.Reject, Reason: "This role needs the right to work here."), allowAutoReject: allowAutoReject),
        ]);

    // Exactly one error, at the action, and it is the integrity guardrail (ADR-0004), not the knockout-only rejection rule.
    private static void AssertRefusedAsSignalDriven(HiringWorkflow workflow, string actionPath)
    {
        var errors = DefinitionValidator.Validate(workflow);

        errors.Should().ContainSingle(e => e.Path == actionPath).Which.Message
            .Should().Contain("integrity or verification signal").And.Contain("Integrity signals never drive rejection (ADR-0004)");
        errors.Should().HaveCount(1);
    }

    private static IEnumerable<string> Paths(HiringWorkflow workflow) => DefinitionValidator.Validate(workflow).Select(e => e.Path);

    private static HiringWorkflow Workflow(
        IReadOnlyList<WorkflowStage>? stages = null, IReadOnlyList<Question>? questions = null, IReadOnlyList<Automation>? automations = null) =>
        HiringWorkflow.Default with
        {
            Stages = stages ?? HiringWorkflow.Default.Stages,
            Questionnaire = new Questionnaire(questions ?? []),
            Automations = automations ?? [],
        };

    private static AutomationConditions Conditions(params AutomationCondition[] rules) => new(ConditionMatch.All, rules);

    private static Automation Automation(
        AutomationConditions? conditions, AutomationAction action, string id = "a", AutomationTrigger? trigger = null, bool allowAutoReject = false) =>
        Automation(conditions, [action], id, trigger, allowAutoReject);

    private static Automation Automation(
        AutomationConditions? conditions, IReadOnlyList<AutomationAction> actions, string id = "a", AutomationTrigger? trigger = null, bool allowAutoReject = false) =>
        new(id, $"Automation {id}", trigger ?? new AutomationTrigger(TriggerType.QuestionnaireCompleted), conditions, actions, AllowAutoReject: allowAutoReject);

    // A valid instance of each action type, so a test isolates the guardrail from the field checks.
    private static AutomationAction Action(ActionType type) => type switch
    {
        ActionType.Reject => new(type, Reason: "Not a fit"),
        ActionType.QueueForRejectionReview => new(type, Reason: "Check this"),
        ActionType.SendEmail => new(type, EmailTemplate: "stage-change"),
        ActionType.NotifyTeam => new(type, Note: "Look at this"),
        ActionType.CallWebhook => new(type, Url: "https://example.com/hook"),
        _ => new(type),
    };
}
