using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Scorecards and the reviewer decision (workflow-engine.md §8): what a scorecard may rate, how the ReviewerDecision
/// trigger and the <c>reviewer.recommendation</c> fact are checked, and that the integrity guardrail still holds when a
/// person's decision sits between a signal and an action.
/// </summary>
public sealed class ScorecardTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.CreateVersion7();

    [Fact]
    public void The_default_workflow_ships_job_related_scorecards_on_its_review_stages()
    {
        var stages = HiringWorkflow.Default.Stages;

        stages.Where(s => s.Scorecard is not null).Select(s => s.Id).Should().Equal("screening", "interview");
        DefinitionValidator.Validate(HiringWorkflow.Default).Should().BeEmpty();
    }

    [Theory]
    [InlineData(StageKind.Hired)]
    [InlineData(StageKind.Rejected)]
    [InlineData(StageKind.Withdrawn)]
    public void A_stage_that_ends_the_application_has_no_scorecard(StageKind kind)
    {
        var workflow = WithStage(new WorkflowStage("done", "Done", kind, Scorecard: Card(new ScorecardCriterion("skills", "Skills"))));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Path.Should().Be($"stages[{workflow.Stages.Count - 1}].scorecard");
    }

    [Fact]
    public void A_scorecard_needs_criteria_with_unique_ids()
    {
        var empty = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: new Scorecard([])));
        var twice = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(new ScorecardCriterion("skills", "Skills"), new ScorecardCriterion("skills", "More skills"))));
        var last = empty.Stages.Count - 1;

        DefinitionValidator.Validate(empty).Select(e => e.Path).Should().Equal($"stages[{last}].scorecard.criteria");
        DefinitionValidator.Validate(twice).Select(e => e.Path).Should().Equal($"stages[{last}].scorecard.criteria[1].id");
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
    public void A_criterion_never_rates_personality_emotion_or_honesty(string label)
    {
        var workflow = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(new ScorecardCriterion("c", label))));

        DefinitionValidator.Validate(workflow).Should().ContainSingle()
            .Which.Message.Should().Contain("never rate personality, emotion or honesty");
    }

    [Theory]
    [InlineData("Data integrity", null)]
    [InlineData("Energy sector knowledge", "Character encoding and statistical confidence in the exercise.")]
    [InlineData("Honours deadlines in the exercise", null)]
    [InlineData("Testing", "Covers the edge cases the exercise names.")]
    public void Job_related_criteria_pass(string label, string? guidance)
    {
        var workflow = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview, Scorecard: Card(new ScorecardCriterion("c", label, guidance))));

        DefinitionValidator.Validate(workflow).Should().BeEmpty();
    }

    [Fact]
    public void Guidance_is_held_to_the_same_rule()
    {
        var workflow = WithStage(new WorkflowStage("panel", "Panel", StageKind.Interview,
            Scorecard: Card(new ScorecardCriterion("c", "Communication", "Whether they come across as sincere"))));

        DefinitionValidator.Validate(workflow).Should().ContainSingle().Which.Path.Should().EndWith("criteria[0].guidance");
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
        var (_, yes) = application.SubmitScorecard(interview, Reviewer, new Dictionary<string, CriterionRating?>(), Recommendation.Yes, null, 1, Monday.AddHours(1));
        var (_, no) = application.SubmitScorecard(interview, Guid.CreateVersion7(), new Dictionary<string, CriterionRating?>(), Recommendation.No, null, 1, Monday.AddHours(2));
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
    public void A_submitted_scorecard_keeps_each_criterion_as_it_was_asked()
    {
        var (application, job) = Application();
        var interview = job.Workflow.FindStage("interview")!;

        var (scorecard, entry) = application.SubmitScorecard(
            interview, Reviewer,
            new Dictionary<string, CriterionRating?> { ["problem_solving"] = CriterionRating.StronglyDemonstrated, ["role_skills"] = CriterionRating.Demonstrated },
            Recommendation.StrongYes, "  Clear walk-through of the cache design.  ", job.WorkflowVersion, Monday);

        scorecard.Ratings.Should().Equal(
            new CriterionScore("role_skills", "Skills the role needs", CriterionRating.Demonstrated),
            new CriterionScore("problem_solving", "Problem solving", CriterionRating.StronglyDemonstrated),
            new CriterionScore("work_knowledge", "Knowledge of the work", null));
        scorecard.Note.Should().Be("Clear walk-through of the cache design.");
        scorecard.TimelineEventId.Should().Be(entry.Id);
        entry.Type.Should().Be(ApplicationEventType.ScorecardSubmitted);
        entry.ActorUserId.Should().Be(Reviewer);
        entry.Data.Should().Be(new ApplicationEventData(ScorecardId: scorecard.Id, StageId: "interview", Recommendation: "StrongYes"));
    }

    [Fact]
    public void A_retried_submission_is_the_same_scorecard_and_a_different_one_is_not()
    {
        var (application, job) = Application();
        var ratings = new Dictionary<string, CriterionRating?> { ["role_skills"] = CriterionRating.Demonstrated };
        var (scorecard, _) = application.SubmitScorecard(job.Workflow.FindStage("interview")!, Reviewer, ratings, Recommendation.Yes, " Good ", 1, Monday);

        scorecard.SameAs(new Dictionary<string, CriterionRating?> { ["role_skills"] = CriterionRating.Demonstrated, ["work_knowledge"] = null }, Recommendation.Yes, "Good")
            .Should().BeTrue();
        scorecard.SameAs(ratings, Recommendation.StrongYes, "Good").Should().BeFalse();
        scorecard.SameAs(ratings, Recommendation.Yes, "Changed my mind").Should().BeFalse();
        scorecard.SameAs(new Dictionary<string, CriterionRating?> { ["role_skills"] = CriterionRating.PartlyDemonstrated }, Recommendation.Yes, "Good")
            .Should().BeFalse();
    }

    private static Automation Tagging(string id, string? stageId) =>
        new(id, id, new AutomationTrigger(TriggerType.ReviewerDecision, stageId), null, [new AutomationAction(ActionType.AddTag, Tag: id)]);

    private static Scorecard Card(params ScorecardCriterion[] criteria) => new(criteria);

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
