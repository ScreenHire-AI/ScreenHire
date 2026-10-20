using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// Every rule of <see cref="IntegrityRuleset"/> (docs/architecture/integrity-engine.md, "Ruleset"), each on its own:
/// a level above the lowest needs two independent categories, telemetry never raises a level alone, context-only
/// signals never make a category count, the top level needs two categories besides telemetry, accommodation drops
/// accessibility-sensitive signals, and AI assistance is relative to the job's policy and the declaration.
/// </summary>
public sealed class IntegrityRulesetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_signals_is_the_lowest_level_with_no_reasons()
    {
        var (authenticity, ai) = Assess([]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns));
        authenticity.ReasonCodes.Should().BeEmpty();
        authenticity.Summary.Should().Be("Nothing unusual in the checks that ran.");
        authenticity.NextStep.Should().Be("No action needed.");
        ai.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated));
    }

    [Fact]
    public void A_single_category_never_raises_a_level_however_heavy()
    {
        var (authenticity, _) = Assess(
        [
            Signal(SignalCatalogue.Honeypot),
            Signal(SignalCatalogue.Webdriver),
            Signal(SignalCatalogue.SubmitVelocity),
        ]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns));
        authenticity.AppliedRules.Should().Equal(IntegrityRuleset.TwoIndependentCategories);
        authenticity.ReasonCodes.Should().BeEmpty("signals that did not raise the level are context, not reasons");
        authenticity.CountedSignalIds.Should().BeEmpty();
        authenticity.SignalIds.Should().HaveCount(3, "every signal is still shown");
        authenticity.Summary.Should().Contain("Shown for context only");
    }

    [Fact]
    public void Telemetry_alone_never_raises_a_level()
    {
        var (_, ai) = Assess(
        [
            Signal(SignalCatalogue.PasteDominant, SignalWeight.High),
            Signal(SignalCatalogue.PasteDominant, SignalWeight.High),
            Signal(SignalCatalogue.FocusSwitching, SignalWeight.High),
        ]);

        ai.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated));
        ai.AppliedRules.Should().Equal(IntegrityRuleset.TelemetryOnlyAlongsideAnotherCategory);
        ai.SignalIds.Should().BeEmpty("telemetry alone is not shown at all");
        ai.Summary.Should().StartWith("No signs that the content was generated or templated.");
    }

    [Fact]
    public void Telemetry_with_another_category_raises_at_most_to_the_middle_level()
    {
        // Content and telemetry both corroborate, heavily: still only one category besides telemetry.
        var (_, ai) = Assess(
        [
            Signal(SignalCatalogue.PasteDominant, SignalWeight.High),
            Signal(SignalCatalogue.FocusSwitching, SignalWeight.High),
            Signal(SignalCatalogue.NearDuplicateAnswer, SignalWeight.High),
            Signal(SignalCatalogue.TemplatedAnswer, SignalWeight.High),
        ]);

        ai.Level.Should().Be(nameof(AiAssistanceLevel.SomeIndicators));
        ai.AppliedRules.Should().Contain(IntegrityRuleset.TopLevelNeedsTwoNonTelemetryCategories);
        ai.ReasonCodes.Should().BeEquivalentTo(
            [SignalCatalogue.PasteDominant, SignalCatalogue.FocusSwitching, SignalCatalogue.NearDuplicateAnswer, SignalCatalogue.TemplatedAnswer]);
    }

    [Fact]
    public void Two_independent_categories_raise_to_review()
    {
        var (authenticity, _) = Assess([Signal(SignalCatalogue.DatacenterIp), Signal(SignalCatalogue.DisposableEmail)]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.ReviewSuggested));
        authenticity.ReasonCodes.Should().BeEquivalentTo([SignalCatalogue.DatacenterIp, SignalCatalogue.DisposableEmail]);
        authenticity.Summary.Should().StartWith("2 observations from network and identity checks back each other up: ")
            .And.Contain("sent from a hosting provider's network").And.Contain("throwaway email address");
        authenticity.NextStep.Should().StartWith("Read the evidence before the first screen.");
    }

    [Fact]
    public void Two_categories_with_enough_weight_recommend_verification()
    {
        var (authenticity, _) = Assess(
        [
            Signal(SignalCatalogue.DatacenterIp),
            Signal(SignalCatalogue.DisposableEmail),
            Signal(SignalCatalogue.Honeypot),
        ]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.VerificationRecommended));
        authenticity.AppliedRules.Should().BeEmpty();
        authenticity.NextStep.Should().Contain("not a reason to reject");
    }

    [Fact]
    public void A_category_needs_enough_weight_to_count()
    {
        // A VoIP number (Low) is not enough on its own to make identity a second category.
        var (authenticity, _) = Assess([Signal(SignalCatalogue.DatacenterIp), Signal(SignalCatalogue.VoipPhone)]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns));
        authenticity.AppliedRules.Should().Equal(IntegrityRuleset.TwoIndependentCategories);
    }

    [Fact]
    public void A_context_only_signal_never_makes_its_category_count()
    {
        // A VPN and a failed bot challenge are context: network and automation do not count, so nothing is raised.
        var (authenticity, _) = Assess(
        [
            Signal(SignalCatalogue.AnonymousProxy, SignalWeight.High),
            Signal(SignalCatalogue.TurnstileFailed, SignalWeight.High),
            Signal(SignalCatalogue.DisposableEmail),
        ]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns));
    }

    [Fact]
    public void A_context_only_signal_adds_weight_next_to_a_direct_one()
    {
        // A datacenter IP (2) plus a VPN (1) makes network 3; a disposable email (2) plus a VoIP number (1) makes
        // identity 3: two categories, 6 together, so the top level, which the VPN's weight was needed for.
        var (authenticity, _) = Assess(
        [
            Signal(SignalCatalogue.DatacenterIp),
            Signal(SignalCatalogue.AnonymousProxy),
            Signal(SignalCatalogue.DisposableEmail),
            Signal(SignalCatalogue.VoipPhone),
        ]);

        authenticity.Level.Should().Be(nameof(AuthenticityLevel.VerificationRecommended));
        authenticity.ReasonCodes.Should().Contain(SignalCatalogue.AnonymousProxy);
    }

    [Fact]
    public void An_accommodation_drops_accessibility_sensitive_signals()
    {
        IntegritySignal[] signals = [Signal(SignalCatalogue.SubmitVelocity), Signal(SignalCatalogue.DisposableEmail)];

        var (without, _) = Assess(signals);
        var (with, _) = Assess(signals, accommodation: true);

        without.Level.Should().Be(nameof(AuthenticityLevel.ReviewSuggested));
        with.Level.Should().Be(nameof(AuthenticityLevel.NoConcerns));
        with.AppliedRules.Should().Contain(IntegrityRuleset.AccommodationSuppression);
        with.SignalIds.Should().Equal(signals[1].Id);
        with.Summary.Should().NotContainEquivalentOf("accommodation").And.NotContainEquivalentOf("assistive");
    }

    [Fact]
    public void An_accommodation_drops_telemetry_from_ai_assistance_too()
    {
        var (_, ai) = Assess(
            [Signal(SignalCatalogue.PasteDominant, SignalWeight.High), Signal(SignalCatalogue.NearDuplicateAnswer, SignalWeight.High),
                Signal(SignalCatalogue.ResumeGenerator, SignalWeight.High)],
            accommodation: true);

        ai.SignalIds.Should().HaveCount(2);
        ai.ReasonCodes.Should().NotContain(SignalCatalogue.PasteDominant);
    }

    [Theory]
    [InlineData(AiUsePolicy.Allowed, AiUseDeclaration.NotProvided, AiPolicyContext.AllowedByPolicy)]
    [InlineData(AiUsePolicy.Allowed, AiUseDeclaration.Used, AiPolicyContext.AllowedByPolicy)]
    [InlineData(AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.Used, AiPolicyContext.Disclosed)]
    [InlineData(AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.DidNotUse, AiPolicyContext.NotDisclosed)]
    [InlineData(AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.NotProvided, AiPolicyContext.NotDisclosed)]
    [InlineData(AiUsePolicy.NotAllowed, AiUseDeclaration.Used, AiPolicyContext.DeclaredUseNotAllowed)]
    [InlineData(AiUsePolicy.NotAllowed, AiUseDeclaration.DidNotUse, AiPolicyContext.NotAllowed)]
    public void Ai_use_context_comes_from_the_policy_and_the_declaration(AiUsePolicy policy, AiUseDeclaration declaration, AiPolicyContext expected) =>
        IntegrityRuleset.ContextFor(policy, declaration).Should().Be(expected);

    [Theory]
    [InlineData(AiUsePolicy.Allowed, AiUseDeclaration.NotProvided, nameof(AiAssistanceLevel.SomeIndicators))]
    [InlineData(AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.Used, nameof(AiAssistanceLevel.SomeIndicators))]
    [InlineData(AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.DidNotUse, nameof(AiAssistanceLevel.StrongIndicators))]
    [InlineData(AiUsePolicy.NotAllowed, AiUseDeclaration.NotProvided, nameof(AiAssistanceLevel.StrongIndicators))]
    public void Ai_assistance_within_the_jobs_policy_is_capped_at_the_middle_level(AiUsePolicy policy, AiUseDeclaration declaration, string expected)
    {
        var (_, ai) = Assess(StrongAiSignals(), policy, declaration);

        ai.Level.Should().Be(expected);
        if (expected == nameof(AiAssistanceLevel.SomeIndicators))
        {
            ai.AppliedRules.Should().Contain(IntegrityRuleset.AiWithinPolicyCap);
            ai.NextStep.Should().Be("No action needed. The AI use fits this job's policy.");
        }
    }

    [Theory]
    [InlineData(AiUsePolicy.Allowed)]
    [InlineData(AiUsePolicy.AllowedWithDisclosure)]
    [InlineData(AiUsePolicy.NotAllowed)]
    public void A_declaration_of_ai_use_never_raises_a_level(AiUsePolicy policy)
    {
        foreach (var signals in new[] { [], StrongAiSignals(), [Signal(SignalCatalogue.TemplatedAnswer)] })
        {
            var (usedAuthenticity, used) = Assess(signals, policy, AiUseDeclaration.Used);
            var (silentAuthenticity, silent) = Assess(signals, policy, AiUseDeclaration.DidNotUse);

            Rank(used.Level).Should().BeLessThanOrEqualTo(Rank(silent.Level));
            used.ReasonCodes.Should().BeSubsetOf(silent.ReasonCodes);
            usedAuthenticity.Level.Should().Be(silentAuthenticity.Level);
        }
    }

    [Fact]
    public void Ai_assistance_summaries_say_where_the_candidate_stands_against_the_policy()
    {
        Assess([], AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.Used).Ai.Summary
            .Should().Be("No signs that the content was generated or templated. The candidate said they used AI tools, which this job allows when disclosed.");
        // A declaration changes neither the next step nor its tone: honest disclosure draws no extra prompt.
        Assess([], AiUsePolicy.NotAllowed, AiUseDeclaration.Used).Ai.NextStep.Should().Be("No action needed.");
        Assess(StrongAiSignals(), AiUsePolicy.NotAllowed, AiUseDeclaration.Used).Ai.NextStep
            .Should().Be(Assess(StrongAiSignals(), AiUsePolicy.NotAllowed, AiUseDeclaration.DidNotUse).Ai.NextStep);
        Assess(StrongAiSignals(), AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.DidNotUse).Ai.NextStep
            .Should().Be(Assess(StrongAiSignals(), AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.NotProvided).Ai.NextStep);
        Assess(StrongAiSignals(), AiUsePolicy.NotAllowed, AiUseDeclaration.NotProvided).Ai.NextStep
            .Should().Contain("indicators are not proof");
    }

    [Fact]
    public void Each_assessment_reads_only_its_own_signals()
    {
        var (authenticity, ai) = Assess([Signal(SignalCatalogue.DatacenterIp), Signal(SignalCatalogue.NearDuplicateAnswer)]);

        authenticity.SignalIds.Should().ContainSingle();
        ai.SignalIds.Should().ContainSingle();
    }

    [Fact]
    public void The_fingerprint_is_stable_for_the_same_inputs_and_changes_with_them()
    {
        IntegritySignal[] signals = [Signal(SignalCatalogue.DatacenterIp), Signal(SignalCatalogue.DisposableEmail)];
        var first = Assess(signals).Authenticity.Fingerprint;

        Assess([.. signals.Reverse()]).Authenticity.Fingerprint.Should().Be(first);
        Assess([.. signals, Signal(SignalCatalogue.VoipPhone)]).Authenticity.Fingerprint.Should().NotBe(first);
        Assess(signals, declaration: AiUseDeclaration.Used).Authenticity.Fingerprint.Should().NotBe(first);
    }

    [Fact]
    public void Nothing_in_an_assessment_is_a_score()
    {
        var results = IntegrityRuleset.Assess(new IntegrityInputs(
            [Signal(SignalCatalogue.DatacenterIp), Signal(SignalCatalogue.DisposableEmail), Signal(SignalCatalogue.Honeypot)],
            AiUsePolicy.NotAllowed, AiUseDeclaration.NotProvided, false));

        foreach (var result in results)
        {
            result.Summary.Should().NotContain("%").And.NotContainEquivalentOf("score").And.NotContainEquivalentOf("probab");
            result.NextStep.Should().NotContainEquivalentOf("reject the").And.NotContainEquivalentOf("score");
        }
    }

    [Fact]
    public void The_row_records_the_ruleset_and_the_ai_context()
    {
        var inputs = new IntegrityInputs([], AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.Used, false);
        var organization = Guid.CreateVersion7();
        var application = Guid.CreateVersion7();

        var rows = IntegrityRuleset.Assess(inputs).Select(r => r.ToAssessment(organization, application, inputs, Now)).ToList();

        rows.Should().AllSatisfy(r => r.RulesetVersion.Should().Be(IntegrityRuleset.Version));
        rows.Single(r => r.Kind == IntegrityAssessmentKind.AiAssistance).AiPolicyContext.Should().Be(AiPolicyContext.Disclosed);
        rows.Single(r => r.Kind == IntegrityAssessmentKind.Authenticity).AiUseDeclaration.Should().BeNull();
        rows.Should().AllSatisfy(r => r.IsRaised.Should().BeFalse());
    }

    private static IntegritySignal[] StrongAiSignals() =>
    [
        Signal(SignalCatalogue.NearDuplicateAnswer, SignalWeight.High),
        Signal(SignalCatalogue.ResumeGenerator, SignalWeight.High),
    ];

    private static int Rank(string level) =>
        Enum.TryParse<AiAssistanceLevel>(level, out var ai) ? (int)ai : (int)Enum.Parse<AuthenticityLevel>(level);

    private static (RulesetResult Authenticity, RulesetResult Ai) Assess(
        IReadOnlyList<IntegritySignal> signals,
        AiUsePolicy policy = AiUsePolicy.AllowedWithDisclosure,
        AiUseDeclaration declaration = AiUseDeclaration.NotProvided,
        bool accommodation = false)
    {
        var results = IntegrityRuleset.Assess(new IntegrityInputs(signals, policy, declaration, accommodation));
        return (results.Single(r => r.Kind == IntegrityAssessmentKind.Authenticity), results.Single(r => r.Kind == IntegrityAssessmentKind.AiAssistance));
    }

    /// <summary>A stored signal of <paramref name="code"/> as the catalogue defines it, optionally heavier or lighter.</summary>
    internal static IntegritySignal Signal(string code, SignalWeight? weight = null)
    {
        var definition = SignalCatalogue.Get(code);
        return new IntegritySignal
        {
            Code = code,
            Category = definition.Category,
            Assessment = definition.Assessment,
            Weight = weight ?? definition.Weight,
            Confidence = SignalConfidence.High,
            Source = "test",
            SourceVersion = 1,
            ObservedAt = Now,
            AccessibilitySensitive = definition.AccessibilitySensitive,
            ContextOnly = definition.ContextOnly,
            Informational = definition.Informational,
            Explanation = definition.ExplanationTemplate,
        };
    }
}
