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
            .Should().BeEquivalentTo("stages", "questionnaire", "verificationPolicy", "automations");
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

    // An organization's own template is named by its id; whether it exists is the API's check at save
    // (EmailTemplateReferences), because the validator also runs in the worker.
    [Theory]
    [InlineData("application-received", true)]
    [InlineData("0199f0c4-0000-7000-8000-000000000001", true)]
    [InlineData("welcome", false)]
    [InlineData("0199f0c40000700080000000000000001", false)]
    public void A_SendEmail_action_names_a_built_in_template_or_an_organization_template_by_id(string templateId, bool valid)
    {
        var workflow = Workflow(automations: [Automation(null, new AutomationAction(ActionType.SendEmail, EmailTemplate: templateId), "mailer")]);

        if (valid)
        {
            Paths(workflow).Should().BeEmpty();
        }
        else
        {
            Paths(workflow).Should().Equal("automations[0].actions[0].emailTemplate");
        }
    }

    // Guardrail 1 (ADR-0004, workflow-engine.md §2): verification signals may only route and step up.
    [Theory]
    [InlineData(ActionType.Reject)]
    [InlineData(ActionType.QueueForRejectionReview)]
    [InlineData(ActionType.SendEmail)]
    [InlineData(ActionType.NotifyTeam)]
    [InlineData(ActionType.CallWebhook)]
    public void A_verification_condition_cannot_drive_anything_but_routing(ActionType action)
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                Action(action), allowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("ADR-0004");
    }

    // ADR-0019 decision 5: a definition that still says integrity.* after the rename is held to the same guardrail.
    [Theory]
    [InlineData("integrity.authenticity")]
    [InlineData("integrity.aiAssistance")]
    public void A_leftover_integrity_condition_still_cannot_reject(string fact)
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition(fact, ConditionOperator.EqualTo, "ReviewSuggested")),
                Action(ActionType.Reject), allowAutoReject: true),
        ]);

        DefinitionValidator.Validate(workflow).Should().Contain(e => e.Path == "automations[0].actions[0]" && e.Message.Contains("ADR-0004"));
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("hired")]
    public void A_verification_condition_cannot_move_an_application_to_a_terminal_stage(string stage)
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.aiAssistance", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.MoveToStage, StageId: stage)),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void A_verification_trigger_counts_as_a_signal_even_without_conditions()
    {
        var workflow = Workflow(automations:
        [
            Automation(null, new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Looks automated"), "a",
                new AutomationTrigger(TriggerType.VerificationAssessed)),
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
    public void Verification_signals_may_tag_assign_or_move_to_a_review_stage()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
            [
                new AutomationAction(ActionType.AddTag, Tag: "verification-review"),
                new AutomationAction(ActionType.AssignReviewer),
                new AutomationAction(ActionType.MoveToStage, StageId: "screening"),
            ], trigger: new AutomationTrigger(TriggerType.VerificationAssessed)),
        ]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void A_verification_condition_mixed_with_a_knockout_still_cannot_reject()
    {
        var workflow = Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(
                new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no"),
                new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.Reject, Reason: "Not eligible"), allowAutoReject: true),
        ]);

        Paths(workflow).Should().Contain("automations[0].actions[0]");
    }

    [Fact]
    public void A_signal_cannot_be_laundered_through_a_tag()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.AddTag, Tag: "Verification-Review"), "tagger", new AutomationTrigger(TriggerType.VerificationAssessed)),
            Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "verification-review")),
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
                new AutomationTrigger(TriggerType.VerificationAssessed)),
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
                    new AutomationTrigger(TriggerType.VerificationAssessed)),
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
                Automation(null, new AutomationAction(ActionType.AddTag, Tag: "verification-review"), "tag",
                    new AutomationTrigger(TriggerType.VerificationAssessed)),
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
            ? new AutomationCondition("application.tags", op, "verification")
            : new AutomationCondition("application.tags", op, Values: ["referral", "verification-review"]);
        var reject = action == ActionType.Reject
            ? new AutomationAction(ActionType.Reject, Reason: "Not a fit")
            : new AutomationAction(ActionType.MoveToStage, StageId: "rejected");
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.AddTag, Tag: "Verification-Review"), "tagger", new AutomationTrigger(TriggerType.VerificationAssessed)),
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
                    new AutomationAction(ActionType.MoveToStage, StageId: "verify"), "route", new AutomationTrigger(TriggerType.VerificationAssessed)),
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
            Automation(Conditions(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")),
                new AutomationAction(ActionType.MoveToStage, StageId: "withdrawn"), "a", new AutomationTrigger(TriggerType.VerificationAssessed),
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
                    new AutomationTrigger(TriggerType.VerificationAssessed)),
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

    // An SLA breach measures the team's delay (workflow-engine.md section 8): only reminders, on a stage whose time limit
    // counts, or on any stage once one has a limit.
    [Theory]
    [InlineData(ActionType.NotifyTeam)]
    [InlineData(ActionType.AssignReviewer)]
    [InlineData(ActionType.CreateTask)]
    [InlineData(ActionType.AddTag)]
    public void A_time_limit_breach_may_remind_the_team(ActionType type)
    {
        var workflow = Workflow(automations: [Automation(null, ReminderAction(type), "remind", new AutomationTrigger(TriggerType.SlaBreached, "screening"))]);

        Paths(workflow).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ActionType.SendEmail)]
    [InlineData(ActionType.MoveToStage)]
    [InlineData(ActionType.CallWebhook)]
    [InlineData(ActionType.QueueForRejectionReview)]
    [InlineData(ActionType.Reject)]
    public void A_time_limit_breach_may_do_nothing_that_could_count_against_the_candidate(ActionType type)
    {
        var action = type == ActionType.MoveToStage ? new AutomationAction(type, StageId: "interview") : Action(type);
        var workflow = Workflow(automations: [Automation(null, action, "slow", new AutomationTrigger(TriggerType.SlaBreached), allowAutoReject: true)]);

        var errors = DefinitionValidator.Validate(workflow);

        errors.Should().ContainSingle().Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].actions[0]"
            && e.Message.Contains("hiring team's delay, not the candidate", StringComparison.Ordinal)
            && e.Message.Contains("notify the team, assign a reviewer, create a task or add a tag", StringComparison.Ordinal));
    }

    [Fact]
    public void A_time_limit_breach_names_a_stage_whose_limit_counts_or_any_stage_when_one_has_a_limit()
    {
        var noLimits = HiringWorkflow.Default.Stages.Select(s => s with { SlaDays = null }).ToList();
        var terminalLimit = HiringWorkflow.Default.Stages.Select(s => s.Id == "rejected" ? s with { SlaDays = 2 } : s).ToList();
        AutomationTrigger On(string? stageId) => new(TriggerType.SlaBreached, stageId);

        Paths(Workflow(automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", On("interview"))]))
            .Should().Equal(["automations[0].trigger.stageId"], "interview has no time limit");
        Paths(Workflow(stages: terminalLimit, automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", On("rejected"))]))
            .Should().Equal(["automations[0].trigger.stageId"], "a rejected stage ends the application");
        Paths(Workflow(automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", On("nowhere"))]))
            .Should().Equal("automations[0].trigger.stageId");
        Paths(Workflow(stages: noLimits, automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", On(null))]))
            .Should().Equal(["automations[0].trigger.type"], "no stage has a time limit");
        Paths(Workflow(automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", On(null))]))
            .Should().BeEmpty("screening has a time limit in the default workflow");
        Paths(Workflow(automations: [Automation(null, ReminderAction(ActionType.CreateTask), "a", new AutomationTrigger(TriggerType.SlaBreached, AfterHours: 4))]))
            .Should().Equal("automations[0].trigger.afterHours");
    }

    // The engine skips a timing condition for an application with an accommodation; the breach must never be skipped.
    [Fact]
    public void A_time_limit_breach_cannot_test_time_in_stage()
    {
        var workflow = Workflow(automations:
        [
            Automation(Conditions(new AutomationCondition("application.hoursInStage", ConditionOperator.GreaterThan, "100")),
                ReminderAction(ActionType.NotifyTeam), "slow", new AutomationTrigger(TriggerType.SlaBreached, "screening")),
            Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "referral")),
                ReminderAction(ActionType.NotifyTeam), "referral", new AutomationTrigger(TriggerType.SlaBreached, "screening")),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].conditions.rules[0].fact" && e.Message.Contains("accommodation", StringComparison.Ordinal));
    }

    // A breach never counts against the candidate, not even through the tag it adds: a reader of that tag may remind the
    // team or move into a review stage, and nothing that could be a rejection under another name.
    [Fact]
    public void A_tag_a_time_limit_breach_adds_can_only_drive_reminders_and_review_moves()
    {
        var stages = HiringWorkflow.Default.Stages.Append(new WorkflowStage("parked", "Not progressing", StageKind.Custom)).ToList();
        var readsTag = Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "overdue"));
        var workflow = Workflow(stages: stages, automations:
        [
            Automation(null, ReminderAction(ActionType.AddTag), "late", new AutomationTrigger(TriggerType.SlaBreached, "screening")),
            Automation(readsTag, new AutomationAction(ActionType.QueueForRejectionReview, Reason: "Waiting too long"), "queue"),
            Automation(readsTag, new AutomationAction(ActionType.MoveToStage, StageId: "parked"), "park"),
            Automation(readsTag, new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change"), "mail"),
            Automation(readsTag, new AutomationAction(ActionType.NotifyTeam, Note: "Still waiting"), "nudge"),
            Automation(readsTag, new AutomationAction(ActionType.MoveToStage, StageId: "interview"), "advance"),
        ]);

        var errors = DefinitionValidator.Validate(workflow);

        errors.Select(e => e.Path).Should().Equal("automations[1].actions[0]", "automations[2].actions[0]", "automations[3].actions[0]");
        errors.Should().OnlyContain(e => e.Message.Contains("never counts against the candidate", StringComparison.Ordinal));
    }

    // The reach of "at one remove" is the signal rule's: a negated tag rule depends on the breach's tag as much as a
    // positive one, and once a breach-tag reader moves applications, its stage's triggers, every stage exit and every
    // stage-less reviewer decision count too.
    [Fact]
    public void A_time_limit_breach_reaches_negated_tag_rules_and_the_stages_its_readers_move_into()
    {
        var mail = new AutomationAction(ActionType.SendEmail, EmailTemplate: "stage-change");
        var stages = HiringWorkflow.Default.Stages.Select(s => s.Id == "interview" ? s with { Scorecard = BuiltInScorecards.Interview.ToScorecard() } : s).ToList();
        var tagger = Automation(null, ReminderAction(ActionType.AddTag), "late", new AutomationTrigger(TriggerType.SlaBreached, "screening"));
        var negated = Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "referral", Negate: true)),
            mail, "no-referral", new AutomationTrigger(TriggerType.ApplicationSubmitted));

        Paths(Workflow(automations: [tagger, negated])).Should().Equal("automations[1].actions[0]");
        Paths(Workflow(automations: [negated])).Should().BeEmpty("without a breach the same rule is ordinary");

        var advance = Automation(Conditions(new AutomationCondition("application.tags", ConditionOperator.Contains, "overdue")),
            new AutomationAction(ActionType.MoveToStage, StageId: "interview"), "advance");
        var onInterview = Automation(null, mail, "on-interview", new AutomationTrigger(TriggerType.StageEntered, "interview"));
        var onExit = Automation(null, mail, "on-exit", new AutomationTrigger(TriggerType.StageExited, "applied"));
        var onDecision = Automation(null, new AutomationAction(ActionType.QueueForRejectionReview, Reason: "A reviewer said no"), "on-decision",
            new AutomationTrigger(TriggerType.ReviewerDecision));

        Paths(Workflow(stages: stages, automations: [tagger, advance, onInterview, onExit, onDecision]))
            .Should().Equal("automations[2].actions[0]", "automations[3].actions[0]", "automations[4].actions[0]");
    }

    // On a stage a signal routes into, the breach is signal-driven too: it may assign or tag, as any such automation.
    [Fact]
    public void A_time_limit_breach_on_a_stage_a_signal_routes_into_follows_the_verification_guardrail()
    {
        var stages = HiringWorkflow.Default.Stages.Append(new WorkflowStage("verify", "Verification", StageKind.Verification, SlaDays: 2)).ToList();
        var route = Automation(null, new AutomationAction(ActionType.MoveToStage, StageId: "verify"), "route", new AutomationTrigger(TriggerType.VerificationAssessed));

        AssertRefusedAsSignalDriven(
            Workflow(stages: stages, automations: [route, Automation(null, ReminderAction(ActionType.NotifyTeam), "remind", new AutomationTrigger(TriggerType.SlaBreached, "verify"))]),
            "automations[1].actions[0]");
        AssertRefusedAsSignalDriven(
            Workflow(stages: stages, automations: [route, Automation(null, ReminderAction(ActionType.CreateTask), "remind", new AutomationTrigger(TriggerType.SlaBreached))]),
            "automations[1].actions[0]");
        Paths(Workflow(stages: stages, automations: [route, Automation(null, ReminderAction(ActionType.AssignReviewer), "remind", new AutomationTrigger(TriggerType.SlaBreached, "verify"))]))
            .Should().BeEmpty();
    }

    [Fact]
    public void A_terminal_stage_has_no_time_limit_that_counts()
    {
        new WorkflowStage("rejected", "Rejected", StageKind.Rejected, SlaDays: 3).SlaDueAt(DateTimeOffset.UnixEpoch).Should().BeNull();
        new WorkflowStage("screening", "Screening", StageKind.Screening).SlaDueAt(DateTimeOffset.UnixEpoch).Should().BeNull();
        new WorkflowStage("screening", "Screening", StageKind.Screening, SlaDays: 3).SlaDueAt(DateTimeOffset.UnixEpoch)
            .Should().Be(DateTimeOffset.UnixEpoch.AddDays(3));
    }

    private static HiringWorkflow KnockoutReject(bool allowAutoReject) =>
        Workflow(questions: [WorkAuthorization], automations:
        [
            Automation(Conditions(new AutomationCondition("answers.work_authorization", ConditionOperator.EqualTo, "no")),
                new AutomationAction(ActionType.Reject, Reason: "This role needs the right to work here."), allowAutoReject: allowAutoReject),
        ]);

    // Exactly one error, at the action, and it is the verification guardrail (ADR-0004), not the knockout-only rejection rule.
    private static void AssertRefusedAsSignalDriven(HiringWorkflow workflow, string actionPath)
    {
        var errors = DefinitionValidator.Validate(workflow);

        errors.Should().ContainSingle(e => e.Path == actionPath).Which.Message
            .Should().Contain("uses a verification signal").And.Contain("Verification signals never drive rejection (ADR-0004)");
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

    // A valid reminder, the only kind of action a time limit breach may take.
    private static AutomationAction ReminderAction(ActionType type) => type switch
    {
        ActionType.AddTag => new(type, Tag: "overdue"),
        ActionType.CreateTask => new(type, Note: "Reply to this candidate"),
        _ => Action(type),
    };

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
