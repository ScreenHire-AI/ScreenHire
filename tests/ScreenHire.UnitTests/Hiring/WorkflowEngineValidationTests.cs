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
    public void A_verification_condition_with_reject_is_refused_at_save()
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
    public void A_verification_condition_may_not_even_queue_a_rejection(ActionType type)
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

    [Fact]
    public void Requesting_a_verification_is_refused_until_phase_2()
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(TriggerType.ApplicationSubmitted), null, [new AutomationAction(ActionType.RequestVerification)])]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0].type")
            .Which.Message.Should().Contain("cannot be used yet");
    }

    [Theory]
    [InlineData(TriggerType.VerificationCompleted)]
    [InlineData(TriggerType.CandidateReplied)]
    public void Triggers_nothing_raises_yet_are_refused(TriggerType type)
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(type), null, [new AutomationAction(ActionType.AddTag, Tag: "x")])]);

        DefinitionValidator.Validate(workflow).Select(e => e.Path).Should().Contain("automations[0].trigger.type");
    }

    // Booking an interview raises it (PublicInterviewEndpoints), so an automation may wait for it.
    [Fact]
    public void The_interview_booked_trigger_is_accepted()
    {
        var workflow = Workflow(automations:
        [
            new("booked", "Booked", new AutomationTrigger(TriggerType.InterviewScheduled), null, [new AutomationAction(ActionType.AddTag, Tag: "interview-booked")]),
        ]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void A_schedule_interview_action_with_all_four_fields_is_accepted()
    {
        DefinitionValidator.Validate(WorkflowWith(Interview())).Should().BeEmpty();
        DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewerUserIds = [Avery, Robin, Sam, Kim, Lee], InterviewWindowDays = 60, InterviewMinutes = 120 }))
            .Should().BeEmpty();
        DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewWindowDays = 1, InterviewMinutes = 15, InterviewTimeZone = "UTC" })).Should().BeEmpty();
    }

    [Fact]
    public void A_schedule_interview_action_needs_each_of_its_four_fields()
    {
        var action = new AutomationAction(ActionType.ScheduleInterview);

        DefinitionValidator.Validate(WorkflowWith(action)).Select(e => e.Path).Should().BeEquivalentTo(
            "automations[0].actions[0].interviewerUserIds",
            "automations[0].actions[0].interviewMinutes",
            "automations[0].actions[0].interviewWindowDays",
            "automations[0].actions[0].interviewTimeZone");
    }

    [Fact]
    public void A_schedule_interview_action_takes_one_to_five_different_interviewers()
    {
        Guid[][] refused = [[], [Avery, Robin, Sam, Kim, Lee, Guid.NewGuid()], [Avery, Avery], [Guid.Empty]];
        foreach (var interviewers in refused)
        {
            DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewerUserIds = interviewers })).Should()
                .ContainSingle(e => e.Path == "automations[0].actions[0].interviewerUserIds")
                .Which.Message.Should().Contain("1 to 5");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(61)]
    [InlineData(-30)]
    public void A_schedule_interview_action_takes_an_interview_length_a_request_allows(int minutes)
    {
        DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewMinutes = minutes })).Should()
            .ContainSingle(e => e.Path == "automations[0].actions[0].interviewMinutes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    [InlineData(365)]
    public void A_schedule_interview_action_gives_the_candidate_one_to_sixty_days(int days)
    {
        DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewWindowDays = days })).Should()
            .ContainSingle(e => e.Path == "automations[0].actions[0].interviewWindowDays")
            .Which.Message.Should().Contain("between 1 and 60");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("a-name-of-more-than-sixty-four-characters-is-never-a-zone-id-on-any-host-at-all")]
    public void A_schedule_interview_action_takes_a_time_zone_this_host_knows(string zone)
    {
        DefinitionValidator.Validate(WorkflowWith(Interview() with { InterviewTimeZone = zone })).Should()
            .ContainSingle(e => e.Path == "automations[0].actions[0].interviewTimeZone");
    }

    [Fact]
    public void A_schedule_interview_action_no_longer_takes_a_url()
    {
        DefinitionValidator.Validate(WorkflowWith(Interview() with { Url = "https://example.com/book" })).Should()
            .ContainSingle(e => e.Path == "automations[0].actions[0].url")
            .Which.Message.Should().Contain("does not take url");
    }

    [Fact]
    public void The_interview_fields_are_refused_on_every_other_action_type()
    {
        foreach (var type in Enum.GetValues<ActionType>().Where(t => t is not (ActionType.ScheduleInterview or ActionType.RequestVerification)))
        {
            var action = new AutomationAction(
                type, StageId: "screening", EmailTemplate: "application-received", Tag: "x", Reason: "r", Url: "https://example.com/hook", Note: "n",
                InterviewerUserIds: [Avery], InterviewMinutes: 30, InterviewWindowDays: 7, InterviewTimeZone: "UTC");

            DefinitionValidator.Validate(WorkflowWith(action)).Where(e => e.Message.StartsWith($"{type} does not take interview", StringComparison.Ordinal))
                .Select(e => e.Path).Should().BeEquivalentTo(
                    "automations[0].actions[0].interviewerUserIds",
                    "automations[0].actions[0].interviewMinutes",
                    "automations[0].actions[0].interviewWindowDays",
                    "automations[0].actions[0].interviewTimeZone");
        }
    }

    // Offering a candidate an interview is a candidate-facing outcome, so a verification signal never drives it
    // (CLAUDE.md non-negotiable 2), directly or through a tag or stage a signal-driven automation sets.
    [Fact]
    public void Scheduling_an_interview_is_refused_for_an_automation_a_verification_signal_drives()
    {
        var onTrigger = Workflow(automations: [new("a", "A", new AutomationTrigger(TriggerType.VerificationAssessed), null, [Interview()])]);
        var onFact = Workflow(automations:
        [
            new("b", "B", new AutomationTrigger(TriggerType.ApplicationSubmitted),
                Rules(new AutomationCondition("verification.authenticity", ConditionOperator.EqualTo, "ReviewSuggested")), [Interview()]),
        ]);
        var onTaggedByASignal = Workflow(automations:
        [
            new("tagger", "Tagger", new AutomationTrigger(TriggerType.VerificationAssessed), null, [new AutomationAction(ActionType.AddTag, Tag: "look")]),
            new("c", "C", new AutomationTrigger(TriggerType.ApplicationSubmitted),
                Rules(new AutomationCondition("application.tags", ConditionOperator.Contains, "look")), [Interview()]),
        ]);

        DefinitionValidator.Validate(onTrigger).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("Verification signals never drive rejection");
        DefinitionValidator.Validate(onFact).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("Verification signals never drive rejection");
        DefinitionValidator.Validate(onTaggedByASignal).Should().ContainSingle(e => e.Path == "automations[1].actions[0]")
            .Which.Message.Should().Contain("Verification signals never drive rejection");
    }

    [Fact]
    public void Scheduling_an_interview_is_refused_on_a_stage_a_verification_signal_moves_candidates_to()
    {
        var workflow = Workflow(automations:
        [
            new("mover", "Mover", new AutomationTrigger(TriggerType.VerificationAssessed), null, [new AutomationAction(ActionType.MoveToStage, StageId: "screening")]),
            new("scheduler", "Scheduler", new AutomationTrigger(TriggerType.StageEntered, "screening"), null, [Interview()]),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[1].actions[0]")
            .Which.Message.Should().Contain("Verification signals never drive rejection");
    }

    // A passed time limit measures the team's delay; nothing it does may reach the candidate.
    [Fact]
    public void Scheduling_an_interview_is_refused_for_a_passed_time_limit()
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(TriggerType.SlaBreached), null, [Interview()])]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0]")
            .Which.Message.Should().Contain("may only notify the team");
    }

    [Theory]
    [InlineData(TriggerType.ApplicationSubmitted)]
    [InlineData(TriggerType.QuestionnaireCompleted)]
    [InlineData(TriggerType.ReviewerDecision)]
    [InlineData(TriggerType.InterviewScheduled)]
    [InlineData(TriggerType.Manual)]
    public void Scheduling_an_interview_is_allowed_on_the_other_triggers(TriggerType type)
    {
        var workflow = Workflow(automations: [new("a", "A", new AutomationTrigger(type), null, [Interview()])]);

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
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
            .Which.Message.Should().Contain("application-received, application-rejected, interview-cancelled, interview-confirmation, interview-invitation, questionnaire-request, stage-change");
    }

    [Theory]
    [InlineData("interview-invitation")]
    [InlineData("interview-confirmation")]
    [InlineData("interview-cancelled")]
    public void The_interview_emails_are_sent_only_by_scheduling_never_by_a_send_email_action(string template)
    {
        var workflow = Workflow([WorkAuthorization],
        [
            new("a", "A", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
                [new AutomationAction(ActionType.SendEmail, EmailTemplate: template)]),
        ]);

        DefinitionValidator.Validate(workflow).Should().ContainSingle(e => e.Path == "automations[0].actions[0].emailTemplate")
            .Which.Message.Should().Contain("sent only when an interview is scheduled");
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

    private static readonly Guid Avery = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Robin = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Sam = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Kim = Guid.Parse("00000000-0000-0000-0000-0000000000a4");
    private static readonly Guid Lee = Guid.Parse("00000000-0000-0000-0000-0000000000a5");

    private static AutomationAction Interview() =>
        new(ActionType.ScheduleInterview, InterviewerUserIds: [Avery, Robin], InterviewMinutes: 45, InterviewWindowDays: 14, InterviewTimeZone: "America/New_York");

    private static HiringWorkflow WorkflowWith(AutomationAction action) =>
        Workflow(automations: [new("a", "A", new AutomationTrigger(TriggerType.ApplicationSubmitted), null, [action])]);

    private static AutomationConditions Rules(params AutomationCondition[] rules) => new(ConditionMatch.All, rules);

    private static HiringWorkflow Workflow(IReadOnlyList<Question>? questions = null, IReadOnlyList<Automation>? automations = null) =>
        HiringWorkflow.Default with { Questionnaire = new Questionnaire(questions ?? []), Automations = automations ?? [] };
}
