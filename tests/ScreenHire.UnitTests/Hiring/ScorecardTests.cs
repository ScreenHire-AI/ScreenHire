using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Scorecards and the reviewer decision (workflow-engine.md §8): what a scorecard may ask, how answers of each kind are
/// checked and kept, how the ReviewerDecision trigger and the <c>reviewer.recommendation</c> fact are checked, and that
/// the integrity guardrail still holds when a person's decision sits between a signal and an action.
/// </summary>
public sealed class ScorecardTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    [Fact]
    public void The_default_workflow_ships_job_related_scorecards_on_its_review_stages()
    {
        var stages = HiringWorkflow.Default.Stages;

        stages.Where(s => s.Scorecard is not null).Select(s => s.ScorecardTemplateId).Should().Equal("screening", "interview");
        stages.Single(s => s.Id == "interview").Scorecard.Should().Be(BuiltInScorecards.Interview.ToScorecard());
        DefinitionValidator.Validate(HiringWorkflow.Default).Should().BeEmpty();
    }

    [Fact]
    public void A_hiring_template_names_its_scorecards_and_holds_no_copy()
    {
        var stages = HiringTemplateDefinition.Default.Workflow.Stages;

        stages.Where(s => s.HasScorecard()).Select(s => (s.ScorecardTemplateId, s.Scorecard)).Should().Equal(("screening", null), ("interview", null));
        DefinitionValidator.Validate(HiringTemplateDefinition.Default).Should().BeEmpty();
        DefinitionValidator.Validate(new HiringTemplateDefinition(HiringWorkflow.Default, PostingDefaults.None))
            .Select(e => e.Path).Should().Equal("workflow.stages[1].scorecard", "workflow.stages[2].scorecard");
    }

    [Fact]
    public void A_stage_names_a_built_in_or_an_organization_s_own_scorecard()
    {
        var own = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, ScorecardTemplateId: Guid.CreateVersion7().ToString("D")));
        var unknown = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, ScorecardTemplateId: "great-interview"));

        DefinitionValidator.Validate(own).Should().BeEmpty();
        DefinitionValidator.Validate(unknown).Should().ContainSingle().Which.Path.Should().EndWith("scorecardTemplateId");
    }

    [Theory]
    [InlineData(StageKind.Hired)]
    [InlineData(StageKind.Rejected)]
    [InlineData(StageKind.Withdrawn)]
    public void A_stage_that_ends_the_application_has_no_scorecard(StageKind kind)
    {
        var workflow = WithStage(new WorkflowStage("done", "Done", kind, Scorecard: Card(Rating("skills", "Skills"))));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Path.Should().Be($"stages[{workflow.Stages.Count - 1}].scorecard");
    }

    [Fact]
    public void A_scorecard_needs_questions_with_unique_ids()
    {
        var empty = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card()));
        var twice = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(Rating("skills", "Skills"), Rating("skills", "More skills"))));
        var last = empty.Stages.Count - 1;

        DefinitionValidator.Validate(empty).Select(e => e.Path).Should().Equal($"stages[{last}].scorecard.questions");
        DefinitionValidator.Validate(twice).Select(e => e.Path).Should().Equal($"stages[{last}].scorecard.questions[1].id");
    }

    [Fact]
    public void Only_choice_questions_have_options()
    {
        QuestionOption[] options = [new("a", "System design"), new("b", "Testing")];
        var choice = new ScorecardQuestion("next", ScorecardQuestionType.MultiChoice, "What should the next interview cover?", Options: options);

        DefinitionValidator.ValidateScorecardQuestions([choice], "q").Should().BeEmpty();
        DefinitionValidator.ValidateScorecardQuestions([choice with { Options = null }], "q").Select(e => e.Path).Should().Equal("q[0].options");
        DefinitionValidator.ValidateScorecardQuestions([choice with { Type = ScorecardQuestionType.Rating10 }], "q")
            .Select(e => e.Path).Should().Equal("q[0].options");
    }

    [Theory]
    [InlineData("Personality")]
    [InlineData("Emotional maturity")]
    [InlineData("Seems honest")]
    [InlineData("Culture fit")]
    [InlineData("Trustworthy")]
    [InlineData("Positive attitude")]
    [InlineData("Passion for the product")]
    [InlineData("Motivation")]
    [InlineData("Demeanour")]
    public void A_question_never_rates_personality_emotion_or_honesty(string label)
    {
        var workflow = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(Rating("c", label))));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Message.Should().Contain("never rate personality, emotion or honesty");
    }

    [Theory]
    [InlineData("Data integrity", null)]
    [InlineData("Energy sector knowledge", "Character encoding and statistical confidence in the exercise.")]
    [InlineData("Honours deadlines in the exercise", null)]
    [InlineData("Testing", "Covers the edge cases the exercise names.")]
    public void Job_related_questions_pass(string label, string? guidance)
    {
        var workflow = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(Rating("c", label) with { Guidance = guidance })));

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void Guidance_and_options_are_held_to_the_same_rule()
    {
        var guidance = Card(Rating("c", "Communication") with { Guidance = "Whether they come across as sincere" });
        var option = Card(new ScorecardQuestion("c", ScorecardQuestionType.SingleChoice, "Strongest area",
            Options: [new("a", "Testing"), new("b", "Their enthusiasm")]));

        DefinitionValidator.Validate(WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: guidance)))
            .Should().ContainSingle().Which.Path.Should().EndWith("questions[0].guidance");
        DefinitionValidator.Validate(WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: option)))
            .Should().ContainSingle().Which.Path.Should().EndWith("questions[0].options[1].label");
    }

    [Fact]
    public void A_reviewer_decision_may_name_a_stage_only_if_it_has_a_scorecard()
    {
        Automation On(string? stageId) => new("decision", "Decision", new AutomationTrigger(TriggerType.ReviewerDecision, stageId), null,
            [new AutomationAction(ActionType.AddTag, Tag: "reviewed")]);

        DefinitionValidator.Validate(Workflow(On(null))).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(On("interview"))).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(On("offer"))).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].trigger.stageId" && e.Message.Contains("has no scorecard"));
        DefinitionValidator.Validate(Workflow(On("nowhere"))).Should().ContainSingle()
            .Which.Message.Should().Contain("No stage has the id");
    }

    [Fact]
    public void The_recommendation_is_a_fact_only_on_a_reviewer_decision()
    {
        var rule = new AutomationCondition(FactSchema.ReviewerRecommendation, ConditionOperator.In, Values: ["Yes", "StrongYes"]);
        Automation On(TriggerType type) => new("a", "A", new AutomationTrigger(type, type == TriggerType.StageEntered ? "interview" : null),
            new AutomationConditions(ConditionMatch.All, [rule]), [new AutomationAction(ActionType.MoveToStage, StageId: "offer")]);

        DefinitionValidator.Validate(Workflow(On(TriggerType.ReviewerDecision))).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(On(TriggerType.StageEntered))).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].conditions.rules[0].fact" && e.Message.Contains("ReviewerDecision"));
    }

    [Theory]
    [InlineData(ConditionOperator.EqualTo, "Maybe")]
    [InlineData(ConditionOperator.EqualTo, "strongyes")]
    [InlineData(ConditionOperator.GreaterThan, "No")]
    public void A_recommendation_compares_with_a_recommendation_name(ConditionOperator op, string value)
    {
        var workflow = Workflow(new Automation("a", "A", new AutomationTrigger(TriggerType.ReviewerDecision),
            new AutomationConditions(ConditionMatch.All, [new AutomationCondition(FactSchema.ReviewerRecommendation, op, value)]),
            [new AutomationAction(ActionType.AddTag, Tag: "x")]));

        DefinitionValidator.Validate(workflow).Should().ContainSingle().Which.Path.Should().StartWith("automations[0].conditions.rules[0]");
    }

    // No automation acts on the candidate's rating (ADR-0013): a condition on it is refused with the reason, whatever the
    // trigger or the action, the way an integrity signal may never drive a rejection.
    [Theory]
    [InlineData("application.rating", TriggerType.ReviewerDecision)]
    [InlineData("candidate.rating", TriggerType.StageEntered)]
    [InlineData("rating", TriggerType.ReviewerDecision)]
    [InlineData("scorecard.role_skills", TriggerType.ReviewerDecision)]
    [InlineData("reviewer.rating", TriggerType.ReviewerDecision)]
    public void No_automation_may_test_the_rating(string fact, TriggerType trigger)
    {
        var workflow = Workflow(new Automation("a", "A", new AutomationTrigger(trigger, trigger == TriggerType.StageEntered ? "interview" : null),
            new AutomationConditions(ConditionMatch.All, [new AutomationCondition(fact, ConditionOperator.GreaterThanOrEqual, "4")]),
            [new AutomationAction(ActionType.AddTag, Tag: "shortlist")]));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Should().Match<DefinitionError>(e => e.Path == "automations[0].conditions.rules[0].fact" && e.Message.Contains("ADR-0013"));
    }

    [Fact]
    public void An_application_question_called_rating_is_still_a_condition()
    {
        FactSchema.IsRating("answers.rating").Should().BeFalse();
        FactSchema.IsRating("application.rating").Should().BeTrue();
    }

    // A reviewer's "Strong no" may ask a person to decide, never reject by itself: only an opted-in knockout rejects.
    [Fact]
    public void A_reviewer_decision_queues_a_rejection_review_but_never_rejects()
    {
        Automation With(ActionType type, bool optIn = false) => new("no", "No", new AutomationTrigger(TriggerType.ReviewerDecision, "interview"),
            new AutomationConditions(ConditionMatch.All, [new AutomationCondition(FactSchema.ReviewerRecommendation, ConditionOperator.EqualTo, "StrongNo")]),
            [new AutomationAction(type, Reason: "The interviewer recommended not to continue.")], AllowAutoReject: optIn);

        DefinitionValidator.Validate(Workflow(With(ActionType.QueueForRejectionReview))).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(With(ActionType.Reject, optIn: true))).Should().ContainSingle()
            .Which.Message.Should().Contain("Only a knockout automation may reject");
    }

    [Fact]
    public void An_integrity_condition_on_a_reviewer_decision_may_not_queue_a_rejection()
    {
        var workflow = Workflow(new Automation("a", "A", new AutomationTrigger(TriggerType.ReviewerDecision),
            new AutomationConditions(ConditionMatch.All,
            [
                new AutomationCondition(FactSchema.ReviewerRecommendation, ConditionOperator.EqualTo, "No"),
                new AutomationCondition(FactSchema.IntegrityAuthenticity, ConditionOperator.EqualTo, "VerificationRecommended"),
            ]),
            [new AutomationAction(ActionType.QueueForRejectionReview, Reason: "No")]));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Message.Should().Contain("never drive rejection");
    }

    // A signal that routes candidates into a stage taints that stage's reviewer decisions, and an any-stage reviewer
    // decision too, as StageExited is: the scorecard was filled only because the signal put the candidate there.
    [Theory]
    [InlineData("interview")]
    [InlineData(null)]
    public void A_reviewer_decision_after_a_signal_move_may_not_queue_a_rejection(string? stageId)
    {
        var workflow = Workflow(
            new Automation("route", "Route", new AutomationTrigger(TriggerType.IntegrityAssessed),
                new AutomationConditions(ConditionMatch.All, [new AutomationCondition(FactSchema.IntegrityAuthenticity, ConditionOperator.EqualTo, "ReviewSuggested")]),
                [new AutomationAction(ActionType.MoveToStage, StageId: "interview")]),
            new Automation("no", "No", new AutomationTrigger(TriggerType.ReviewerDecision, stageId),
                new AutomationConditions(ConditionMatch.All, [new AutomationCondition(FactSchema.ReviewerRecommendation, ConditionOperator.EqualTo, "StrongNo")]),
                [new AutomationAction(ActionType.QueueForRejectionReview, Reason: "No")]));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Path.Should().Be("automations[1].actions[0]");
    }

    [Fact]
    public void A_scorecard_fires_any_stage_decisions_and_its_own_stage_s()
    {
        var workflow = HiringWorkflow.Default with
        {
            Automations =
            [
                Tagging("any", null),
                Tagging("interview", "interview"),
                Tagging("screening", "screening"),
            ],
        };

        AutomationEvaluator.Triggered(workflow, TriggerType.ReviewerDecision, "interview").Select(a => a.Id).Should().Equal("any", "interview");
    }

    [Fact]
    public void Only_the_reviewer_decision_reads_the_recommendation_and_it_reads_its_own_scorecard()
    {
        var (application, job) = Application();
        var interview = job.Workflow.FindStage("interview")!;
        var (_, yes) = application.SubmitScorecard(interview, Reviewer, NoAnswers, Recommendation.Yes, null, 1, Monday.AddHours(1));
        var (_, no) = application.SubmitScorecard(interview, Guid.CreateVersion7(), NoAnswers, Recommendation.No, null, 1, Monday.AddHours(2));
        var moved = new ApplicationEvent
        {
            OrganizationId = application.OrganizationId,
            ApplicationId = application.Id,
            Type = ApplicationEventType.StageChanged,
            Data = new("interview", "offer"),
            OccurredAt = Monday.AddHours(3),
        };
        ApplicationEvent[] timeline = [yes, no, moved];

        AutomationFacts.FromTimeline(timeline, yes.Id, TriggerType.ReviewerDecision, yes.OccurredAt).ReviewerRecommendation.Should().Be("Yes");
        AutomationFacts.FromTimeline(timeline, no.Id, TriggerType.ReviewerDecision, no.OccurredAt).ReviewerRecommendation.Should().Be("No");
        AutomationFacts.FromTimeline(timeline, moved.Id, TriggerType.StageEntered, moved.OccurredAt).ReviewerRecommendation.Should().BeNull();
    }

    [Fact]
    public void A_submitted_scorecard_keeps_each_question_as_it_was_asked()
    {
        var (application, job) = Application();
        var interview = job.Workflow.FindStage("interview")!;

        var (scorecard, entry) = application.SubmitScorecard(
            interview, Reviewer,
            new Dictionary<string, ScorecardAnswerValue> { ["problem_solving"] = new(Rating: 5), ["role_skills"] = new(Rating: 4) },
            Recommendation.StrongYes, "  Clear walk-through of the cache design.  ", job.WorkflowVersion, Monday);

        scorecard.Answers.Should().Equal(
            new ScorecardAnswer("role_skills", "Skills the role needs", ScorecardQuestionType.Rating5, Rating: 4),
            new ScorecardAnswer("problem_solving", "Problem solving", ScorecardQuestionType.Rating5, Rating: 5),
            new ScorecardAnswer("work_knowledge", "Knowledge of the work", ScorecardQuestionType.Rating5));
        scorecard.Ratings.Should().BeEmpty();
        scorecard.Note.Should().Be("Clear walk-through of the cache design.");
        scorecard.TimelineEventId.Should().Be(entry.Id);
        entry.Type.Should().Be(ApplicationEventType.ScorecardSubmitted);
        entry.ActorUserId.Should().Be(Reviewer);
        entry.Data.Should().Be(new ApplicationEventData(ScorecardId: scorecard.Id, StageId: "interview", Recommendation: "StrongYes"));
    }

    [Fact]
    public void Every_answer_kind_takes_its_own_shape()
    {
        var card = EveryKind();
        var good = new Dictionary<string, ScorecardAnswerValue>
        {
            ["readable"] = new("yes"),
            ["level"] = new("senior"),
            ["next"] = new(Values: ["design", "testing"]),
            ["summary"] = new("  Solid.  "),
            ["notes"] = new("Finished early and refactored."),
            ["problem"] = new(Rating: 4),
            ["dotnet"] = new(Rating: 10),
            ["finished"] = new("no"),
        };

        ScorecardAnswers.Check(card, good).Should().BeEmpty();
        ScorecardAnswers.Keep(card, good).Select(a => (a.QuestionId, a.Value, a.Rating)).Should().Equal(
            ("readable", "yes", null), ("level", "senior", null), ("next", null, null), ("summary", "Solid.", null),
            ("notes", "Finished early and refactored.", null), ("problem", null, 4), ("dotnet", null, 10), ("finished", "no", null));
        ScorecardAnswers.Keep(card, good).Single(a => a.QuestionId == "next").Options.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("readable", "maybe", null)]
    [InlineData("level", "principal", null)]
    [InlineData("problem", null, 6)]
    [InlineData("problem", null, 0)]
    [InlineData("dotnet", null, 11)]
    [InlineData("finished", "Yes", null)]
    [InlineData("summary", null, 3)]
    public void An_answer_that_does_not_fit_its_question_is_refused(string questionId, string? value, int? rating)
    {
        var answers = new Dictionary<string, ScorecardAnswerValue> { ["problem"] = new(Rating: 3), [questionId] = new(value, Rating: rating) };

        ScorecardAnswers.Check(EveryKind(), answers).Should().ContainSingle().Which.QuestionId.Should().Be(questionId);
    }

    [Fact]
    public void A_required_question_must_be_answered_and_an_unknown_one_is_refused()
    {
        var card = EveryKind();

        ScorecardAnswers.Check(card, new Dictionary<string, ScorecardAnswerValue>()).Select(e => e.QuestionId).Should().Equal("problem");
        ScorecardAnswers.Check(card, new Dictionary<string, ScorecardAnswerValue> { ["problem"] = new(Rating: 1), ["nope"] = new("yes") })
            .Select(e => e.QuestionId).Should().Equal("nope");
        ScorecardAnswers.Check(card, new Dictionary<string, ScorecardAnswerValue> { ["problem"] = new("   ") })
            .Select(e => e.QuestionId).Should().Equal("problem");
        ScorecardAnswers.Check(card, new Dictionary<string, ScorecardAnswerValue> { ["problem"] = new(Rating: 2), ["next"] = new(Values: ["design", "design"]) })
            .Select(e => e.QuestionId).Should().Equal("next");
    }

    [Fact]
    public void A_retried_submission_is_the_same_scorecard_and_a_different_one_is_not()
    {
        var (application, job) = Application();
        var answers = new Dictionary<string, ScorecardAnswerValue> { ["role_skills"] = new(Rating: 4) };
        var (scorecard, _) = application.SubmitScorecard(job.Workflow.FindStage("interview")!, Reviewer, answers, Recommendation.Yes, " Good ", 1, Monday);

        scorecard.SameAs(new Dictionary<string, ScorecardAnswerValue> { ["role_skills"] = new(Rating: 4), ["work_knowledge"] = new() }, Recommendation.Yes, "Good")
            .Should().BeTrue();
        scorecard.SameAs(answers, Recommendation.StrongYes, "Good").Should().BeFalse();
        scorecard.SameAs(answers, Recommendation.Yes, "Changed my mind").Should().BeFalse();
        scorecard.SameAs(new Dictionary<string, ScorecardAnswerValue> { ["role_skills"] = new(Rating: 3) }, Recommendation.Yes, "Good").Should().BeFalse();
    }

    [Fact]
    public void A_job_copies_the_scorecards_its_template_names()
    {
        var own = Guid.CreateVersion7().ToString("D");
        var workflow = HiringTemplateDefinition.Default.Workflow with
        {
            Stages = [.. HiringTemplateDefinition.Default.Workflow.Stages.Select(s => s.Id == "interview" ? s with { ScorecardTemplateId = own } : s)],
        };
        var library = new Dictionary<string, Scorecard> { [own] = EveryKind() };

        var copied = workflow.WithScorecardCopies(library, out var missing);
        workflow.WithScorecardCopies(new Dictionary<string, Scorecard>(), out var notFound);

        missing.Should().BeEmpty();
        copied.FindStage("screening")!.Scorecard.Should().Be(BuiltInScorecards.Screening.ToScorecard());
        copied.FindStage("interview")!.Scorecard.Should().Be(library[own]);
        notFound.Should().Equal(workflow.Stages.ToList().FindIndex(s => s.Id == "interview"));
    }

    private static readonly Dictionary<string, ScorecardAnswerValue> NoAnswers = [];

    private static Automation Tagging(string id, string? stageId) =>
        new(id, id, new AutomationTrigger(TriggerType.ReviewerDecision, stageId), null, [new AutomationAction(ActionType.AddTag, Tag: id)]);

    private static ScorecardQuestion Rating(string id, string label) => new(id, ScorecardQuestionType.Rating5, label);

    private static Scorecard Card(params ScorecardQuestion[] questions) => new("Panel interview", questions);

    private static Scorecard EveryKind() => Card(
        new("readable", ScorecardQuestionType.YesNo, "Is the code readable?"),
        new("level", ScorecardQuestionType.SingleChoice, "Level", Options: [new("mid", "Mid"), new("senior", "Senior")]),
        new("next", ScorecardQuestionType.MultiChoice, "What should the next interview cover?",
            Options: [new("design", "System design"), new("testing", "Testing"), new("data", "Databases")]),
        new("summary", ScorecardQuestionType.ShortText, "In a sentence"),
        new("notes", ScorecardQuestionType.LongText, "Notes on the exercise"),
        new("problem", ScorecardQuestionType.Rating5, "Problem solving", Required: true),
        new("dotnet", ScorecardQuestionType.Rating10, ".NET and C#"),
        new("finished", ScorecardQuestionType.RatingYesNo, "Did they finish the exercise in the time?"));

    private static HiringWorkflow WithStage(WorkflowStage stage) => HiringWorkflow.Default with { Stages = [.. HiringWorkflow.Default.Stages, stage] };

    private static HiringWorkflow Workflow(params Automation[] automations) => HiringWorkflow.Default with { Automations = automations };

    private static (JobApplication Application, Job Job) Application()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Platform engineer", Slug = "platform-engineer" };
        var candidate = Candidate.Create(organization, "Alex Rivera", "alex@example.com", null, Monday);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, Reviewer);
        return (application, job);
    }
}
