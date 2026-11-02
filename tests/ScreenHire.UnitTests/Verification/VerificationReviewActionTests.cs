using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// Marking a reason explained (verification-engine.md §7): one reason of a raised assessment, with a reason and, for Other, a
/// note; undone only by its author; and the latest explain or undo row decides, because the log is append-only. It
/// decides nothing, so no automation fact or webhook event reads it.
/// </summary>
public sealed class VerificationReviewActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.CreateVersion7();
    private static readonly Guid Teammate = Guid.CreateVersion7();

    [Fact]
    public void A_reason_of_a_raised_assessment_is_explained_with_its_reason_and_note()
    {
        var assessment = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out var signalId);

        var action = VerificationReviewAction.Explain(assessment, signalId, ExplainReason.ExpectedForRole, "  Remote role, works through a VPN. ", Reviewer, Now);

        action.Type.Should().Be(VerificationReviewActionType.ReasonExplained);
        action.AssessmentId.Should().Be(assessment.Id);
        action.SignalId.Should().Be(signalId);
        action.ExplainReason.Should().Be(ExplainReason.ExpectedForRole);
        action.Note.Should().Be("Remote role, works through a VPN.");
        action.ActorUserId.Should().Be(Reviewer);
        action.OccurredAt.Should().Be(Now);
        action.DismissReason.Should().BeNull("explaining a reason is not a dismissal");
    }

    [Fact]
    public void Explaining_refuses_a_calm_assessment_a_signal_it_does_not_have_and_other_without_a_note()
    {
        var calm = Assessment(nameof(AuthenticityLevel.NoConcerns), out var calmSignal);
        var raised = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out var signalId);

        var onCalm = () => VerificationReviewAction.Explain(calm, calmSignal, ExplainReason.KnownFalsePositive, null, Reviewer, Now);
        var elsewhere = () => VerificationReviewAction.Explain(raised, Guid.CreateVersion7(), ExplainReason.KnownFalsePositive, null, Reviewer, Now);
        var otherWithoutNote = () => VerificationReviewAction.Explain(raised, signalId, ExplainReason.Other, "  ", Reviewer, Now);

        onCalm.Should().Throw<InvalidOperationException>();
        elsewhere.Should().Throw<ArgumentException>().WithParameterName("signalId");
        otherWithoutNote.Should().Throw<ArgumentException>().WithParameterName("note");
    }

    [Fact]
    public void Only_the_author_undoes_an_explanation()
    {
        var assessment = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out var signalId);
        var explanation = VerificationReviewAction.Explain(assessment, signalId, ExplainReason.ConfirmedWithCandidate, null, Reviewer, Now);

        var byTeammate = () => VerificationReviewAction.UndoExplanation(explanation, Teammate, Now);
        var undo = VerificationReviewAction.UndoExplanation(explanation, Reviewer, Now.AddMinutes(1));

        byTeammate.Should().Throw<InvalidOperationException>();
        undo.Type.Should().Be(VerificationReviewActionType.ExplanationUndone);
        undo.AssessmentId.Should().Be(assessment.Id);
        undo.SignalId.Should().Be(signalId);
        undo.ExplainReason.Should().BeNull();
    }

    [Fact]
    public void The_latest_explain_or_undo_row_decides_per_assessment_and_reason()
    {
        var assessment = Assessment(nameof(AuthenticityLevel.VerificationRecommended), out var first, out var second);
        var reassessed = Assessment(nameof(AuthenticityLevel.VerificationRecommended), out _);
        var firstExplained = VerificationReviewAction.Explain(assessment, first, ExplainReason.ExpectedForRole, null, Reviewer, Now);
        var firstUndone = VerificationReviewAction.UndoExplanation(firstExplained, Reviewer, Now.AddMinutes(1));
        var firstAgain = VerificationReviewAction.Explain(assessment, first, ExplainReason.ConfirmedWithCandidate, null, Teammate, Now.AddMinutes(2));
        var secondExplained = VerificationReviewAction.Explain(assessment, second, ExplainReason.KnownFalsePositive, null, Reviewer, Now);
        var secondUndone = VerificationReviewAction.UndoExplanation(secondExplained, Reviewer, Now.AddMinutes(1));
        var note = VerificationReviewAction.AddNote(assessment.OrganizationId, assessment.ApplicationId, assessment.Id, "Called them.", Reviewer, Now);

        // Out of order on purpose: the time decides, not the list's order.
        var current = VerificationReviewAction.CurrentExplanations([firstAgain, secondUndone, note, firstUndone, secondExplained, firstExplained]);

        current.Should().ContainSingle();
        current[(assessment.Id, first)].Should().BeSameAs(firstAgain, "explaining again after an undo counts");
        current.Should().NotContainKey((assessment.Id, second), "an undo clears the explanation");
        current.Should().NotContainKey((reassessed.Id, first), "a new assessment starts with nothing explained");
    }

    [Fact]
    public void An_evidence_change_is_the_systems_entry_with_no_actor_and_a_trimmed_reason()
    {
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var assessmentId = Guid.CreateVersion7();

        var action = VerificationReviewAction.EvidenceChanged(
            organizationId, applicationId, assessmentId, "  New evidence: shares a device with another application. ", Now);

        action.Type.Should().Be(VerificationReviewActionType.EvidenceChanged);
        action.ActorUserId.Should().BeNull("the system wrote it, not a reviewer");
        action.Note.Should().Be("New evidence: shares a device with another application.");
        action.OrganizationId.Should().Be(organizationId);
        action.ApplicationId.Should().Be(applicationId);
        action.AssessmentId.Should().Be(assessmentId);
        action.OccurredAt.Should().Be(Now);
        action.SignalId.Should().BeNull();
        action.DismissReason.Should().BeNull();
        action.ExplainReason.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_evidence_change_needs_a_reason(string? reason)
    {
        var act = () => VerificationReviewAction.EvidenceChanged(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), reason!, Now);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(reason));
    }

    [Fact]
    public void An_evidence_change_never_changes_what_is_explained()
    {
        var assessment = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out var signalId);
        var explanation = VerificationReviewAction.Explain(assessment, signalId, ExplainReason.ExpectedForRole, null, Reviewer, Now);
        // Written later than the explanation, for the same assessment: it must not clear or replace it.
        var change = VerificationReviewAction.EvidenceChanged(
            assessment.OrganizationId, assessment.ApplicationId, assessment.Id, "New evidence: shares a device.", Now.AddMinutes(5));

        var current = VerificationReviewAction.CurrentExplanations([explanation, change]);

        current.Should().ContainSingle().Which.Value.Should().BeSameAs(explanation);
        VerificationReviewAction.CurrentExplanations([change]).Should().BeEmpty();
    }

    [Fact]
    public void An_evidence_change_can_be_neither_undone_nor_taken_as_an_explanation()
    {
        var change = VerificationReviewAction.EvidenceChanged(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "New evidence.", Now);

        var undo = () => VerificationReviewAction.UndoExplanation(change, Reviewer, Now);

        undo.Should().Throw<ArgumentException>().WithParameterName("explanation");
    }

    [Fact]
    public void An_explanation_with_no_recorded_author_is_undone_by_nobody()
    {
        // The actor column is nullable now. A row without one must not read as "anyone may undo it".
        var assessment = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out var signalId);
        var orphan = new VerificationReviewAction
        {
            OrganizationId = assessment.OrganizationId,
            ApplicationId = assessment.ApplicationId,
            AssessmentId = assessment.Id,
            SignalId = signalId,
            Type = VerificationReviewActionType.ReasonExplained,
            ExplainReason = ExplainReason.ExpectedForRole,
            ActorUserId = null,
            OccurredAt = Now,
        };

        var undo = () => VerificationReviewAction.UndoExplanation(orphan, Reviewer, Now);

        undo.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_dismissal_records_its_reviewer_reason_and_trimmed_note()
    {
        var assessment = Assessment(nameof(AuthenticityLevel.VerificationRecommended), out _);

        var action = VerificationReviewAction.Dismiss(assessment, DismissReason.ExplainedByCandidate, "  Shared family phone. ", Reviewer, Now);

        action.Type.Should().Be(VerificationReviewActionType.Dismissed);
        action.ActorUserId.Should().Be(Reviewer);
        action.DismissReason.Should().Be(DismissReason.ExplainedByCandidate);
        action.Note.Should().Be("Shared family phone.");
        action.AssessmentId.Should().Be(assessment.Id);
    }

    [Fact]
    public void Dismissing_refuses_a_calm_assessment_and_other_without_a_note()
    {
        var calm = Assessment(nameof(AuthenticityLevel.NoConcerns), out _);
        var raised = Assessment(nameof(AuthenticityLevel.ReviewSuggested), out _);

        var onCalm = () => VerificationReviewAction.Dismiss(calm, DismissReason.FalsePositive, null, Reviewer, Now);
        var otherWithoutNote = () => VerificationReviewAction.Dismiss(raised, DismissReason.Other, " ", Reviewer, Now);

        onCalm.Should().Throw<InvalidOperationException>();
        otherWithoutNote.Should().Throw<ArgumentException>().WithParameterName("note");
    }

    [Fact]
    public void A_note_records_its_author_and_a_blank_note_is_refused()
    {
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();

        var note = VerificationReviewAction.AddNote(organizationId, applicationId, null, "  Called the number. ", Reviewer, Now);
        var blank = () => VerificationReviewAction.AddNote(organizationId, applicationId, null, " ", Reviewer, Now);

        note.Type.Should().Be(VerificationReviewActionType.NoteAdded);
        note.ActorUserId.Should().Be(Reviewer);
        note.Note.Should().Be("Called the number.");
        note.AssessmentId.Should().BeNull();
        blank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void No_automation_fact_or_webhook_event_reads_explained_reasons()
    {
        FactSchema.ApplicationFacts.Select(f => f.Name).Should().NotContain(name => name.Contains("explain", StringComparison.OrdinalIgnoreCase));
        FactSchema.Find("verification.explained", new Dictionary<string, Question>()).Should().BeNull();
        WebhookEventTypes.All.Should().NotContain(type => type.Contains("explain", StringComparison.OrdinalIgnoreCase));
    }

    private static VerificationAssessment Assessment(string level, out Guid signalId) => Assessment(level, out signalId, out _);

    private static VerificationAssessment Assessment(string level, out Guid firstSignal, out Guid secondSignal)
    {
        firstSignal = Guid.CreateVersion7();
        secondSignal = Guid.CreateVersion7();
        return new VerificationAssessment
        {
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            Kind = VerificationAssessmentKind.Authenticity,
            Level = level,
            RulesetVersion = VerificationRuleset.Version,
            SignalIds = [firstSignal, secondSignal],
            CountedSignalIds = [firstSignal, secondSignal],
            Summary = "2 observations from network and automation checks back each other up.",
            NextStep = "Read the evidence before the first screen.",
            Fingerprint = "fingerprint",
            AssessedAt = Now,
        };
    }
}
