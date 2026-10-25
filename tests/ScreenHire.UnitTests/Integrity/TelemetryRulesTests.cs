using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// The behavioural rules (<see cref="TelemetryRules"/>) and how their signals reach an assessment: only free-text
/// answers count, autofill is left out, evidence is counts only, and a behavioural signal is a reason only next to a
/// category other than telemetry. Also the device-check basis per country.
/// </summary>
public sealed class TelemetryRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_mostly_pasted_free_text_answer_is_paste_dominant_with_counts_as_evidence()
    {
        var signals = Evaluate([Field("motivation", pasted: 412, typed: 18, pastes: 2)]);

        var signal = signals.Should().ContainSingle().Which;
        signal.Code.Should().Be(SignalCatalogue.PasteDominant);
        signal.Values!["field"].Should().Be("the answer to \"Why do you want this job?\"");
        signal.Evidence.Should().BeEquivalentTo(
        [
            SignalEvidence.Metric("Characters pasted", 412, "characters", 430, "Characters entered"),
            SignalEvidence.Fact("Pastes", "2"),
        ]);
    }

    [Theory]
    [InlineData("motivation", 30, 0, false)] // shorter than a sentence
    [InlineData("motivation", 70, 30, false)] // 70% pasted
    [InlineData("motivation", 400, 0, true)] // the browser filled it
    [InlineData("portfolio", 400, 0, false)] // a link, not free text
    [InlineData("email", 400, 0, false)] // the form's own fields are not answers
    [InlineData("unknown", 400, 0, false)] // a field the job does not ask
    public void Pasting_is_normal_outside_long_free_text_answers(string fieldId, int pasted, int typed, bool autofilled)
    {
        Evaluate([Field(fieldId, pasted: pasted, typed: typed, autofilled: autofilled)]).Should().BeEmpty();
    }

    [Fact]
    public void Focus_switching_needs_both_leaving_the_page_and_returning_to_one_answer()
    {
        Evaluate([Field("motivation", typed: 300, focus: 6)], visibilityChanges: 4).Should().BeEmpty();
        Evaluate([Field("motivation", typed: 300, focus: 4)], visibilityChanges: 9).Should().BeEmpty();

        var signal = Evaluate([Field("notice", typed: 9, focus: 5), Field("motivation", typed: 300, focus: 8)], visibilityChanges: 7)
            .Should().ContainSingle().Which;
        signal.Code.Should().Be(SignalCatalogue.FocusSwitching);
        signal.Values!["field"].Should().Be("\"Why do you want this job?\"", "the answer returned to most");
        signal.Evidence.Select(e => e.Amount).Should().Equal(7m, 8m);
    }

    [Fact]
    public void A_session_adds_up_its_page_views()
    {
        var first = Snapshot([Field("motivation", pasted: 100, typed: 10, pastes: 1, focus: 2), Field("email", autofilled: true)], 2, ["webdriver"]);
        var second = Snapshot([Field("motivation", pasted: 50, typed: 5, pastes: 1, focus: 1)], 3, ["headless", "webdriver"]);

        var session = TelemetryRules.Combine(Guid.CreateVersion7(), [first, second]);

        session.Fields.Should().BeEquivalentTo(
        [
            new FieldTelemetry("email", 0, 0, 0, 1, 1, 1000, true),
            new FieldTelemetry("motivation", 2, 150, 15, 3, 3, 2000, false),
        ]);
        session.Page.Should().BeEquivalentTo(new PageTelemetry(5, ["headless", "webdriver"]));
    }

    [Fact]
    public async Task With_an_accommodation_the_analyzer_never_reads_telemetry_and_says_the_same_as_without_any()
    {
        var source = new FixedSource([Field("motivation", pasted: 400)]);
        var analyzer = new BehaviouralAnalyzer(source);

        var accommodated = await analyzer.AnalyzeAsync(Context(accommodation: true), TestContext.Current.CancellationToken);
        var none = await new BehaviouralAnalyzer(new NoTelemetrySource()).AnalyzeAsync(Context(), TestContext.Current.CancellationToken);

        source.Reads.Should().Be(0);
        accommodated.Should().BeEquivalentTo(none);
        accommodated.Signals.Should().BeEmpty();
    }

    [Fact]
    public async Task The_analyzer_reports_what_the_rules_find()
    {
        var result = await new BehaviouralAnalyzer(new FixedSource([Field("motivation", pasted: 400)]))
            .AnalyzeAsync(Context(), TestContext.Current.CancellationToken);

        result.Signals.Select(s => s.Code).Should().Equal(SignalCatalogue.PasteDominant);
        result.Note.Should().BeNull();
    }

    /// <summary>
    /// Every mix of behavioural signals with one signal from each other AI-assistance category, at every weight:
    /// behavioural signals appear on the assessment only next to another category, and are reasons only when a category
    /// other than telemetry corroborates.
    /// </summary>
    [Fact]
    public void Behavioural_signals_appear_only_alongside_another_category()
    {
        string[] behavioural = [SignalCatalogue.PasteDominant, SignalCatalogue.FocusSwitching];
        string[] others = [SignalCatalogue.NearDuplicateAnswer, SignalCatalogue.ResumeGenerator, SignalCatalogue.PromptInjection];
        SignalWeight?[] weights = [null, SignalWeight.Low, SignalWeight.Medium, SignalWeight.High];
        var checkedCases = 0;
        foreach (var behaviouralWeight in weights.Where(w => w is not null))
        {
            for (var mask = 1; mask < 1 << behavioural.Length; mask++)
            {
                var beh = behavioural.Where((_, i) => (mask & (1 << i)) != 0).Select(code => IntegrityRulesetTests.Signal(code, behaviouralWeight)).ToList();
                foreach (var other in others)
                {
                    foreach (var weight in weights)
                    {
                        var otherSignals = weight is null ? [] : new[] { IntegrityRulesetTests.Signal(other, weight) };
                        var ai = IntegrityRuleset.Assess(new IntegrityInputs([.. beh, .. otherSignals], AiUsePolicy.NotAllowed, AiUseDeclaration.DidNotUse, false))
                            .Single(r => r.Kind == IntegrityAssessmentKind.AiAssistance);
                        var otherCorroborates = otherSignals.Sum(s => s.Weight switch { SignalWeight.High => 3, SignalWeight.Medium => 2, _ => 1 }) >= IntegrityRuleset.CorroboratingWeight;
                        var behaviouralReasons = ai.ReasonCodes.Where(c => c.StartsWith("BEH_", StringComparison.Ordinal)).ToList();

                        if (!otherCorroborates)
                        {
                            ai.Level.Should().Be(nameof(AiAssistanceLevel.NotIndicated));
                            behaviouralReasons.Should().BeEmpty();
                            ai.CountedSignalIds.Should().NotIntersectWith(beh.Select(s => s.Id));
                        }
                        else
                        {
                            ai.Level.Should().NotBe(nameof(AiAssistanceLevel.StrongIndicators), "telemetry is never the second category for the top level");
                        }

                        if (otherSignals.Length == 0)
                        {
                            ai.SignalIds.Should().BeEmpty("telemetry alone is never shown");
                            ai.AppliedRules.Should().Contain(IntegrityRuleset.TelemetryOnlyAlongsideAnotherCategory);
                        }
                        else
                        {
                            ai.SignalIds.Should().Contain(beh.Select(s => s.Id), "next to another category they are shown, as context at least");
                        }

                        checkedCases++;
                    }
                }
            }
        }

        checkedCases.Should().Be(3 * 3 * 3 * 4);
    }

    [Theory]
    [InlineData(new string[0], DeviceCheckBasis.Notice)]
    [InlineData(new[] { "US" }, DeviceCheckBasis.Notice)]
    [InlineData(new[] { "de" }, DeviceCheckBasis.Consent)]
    [InlineData(new[] { "GB", "IE" }, DeviceCheckBasis.Consent)]
    [InlineData(new[] { "IE", "CH" }, DeviceCheckBasis.Off)]
    [InlineData(new[] { "Remote" }, DeviceCheckBasis.Notice)]
    public void The_device_check_takes_the_most_protective_basis_of_the_jobs_countries(string[] countries, DeviceCheckBasis expected)
    {
        var options = new DeviceCheckOptions
        {
            Countries = new(StringComparer.OrdinalIgnoreCase) { ["DE"] = DeviceCheckBasis.Consent, ["IE"] = DeviceCheckBasis.Consent, ["CH"] = DeviceCheckBasis.Off },
        };

        options.BasisFor(countries).Should().Be(expected);
    }

    private static IReadOnlyList<ObservedSignal> Evaluate(IReadOnlyList<FieldTelemetry> fields, int visibilityChanges = 0) =>
        TelemetryRules.Evaluate(new TelemetrySessionAggregates(Guid.CreateVersion7(), fields, new PageTelemetry(visibilityChanges, []), Now), Context());

    private static FieldTelemetry Field(string id, int pasted = 0, int typed = 0, int pastes = 0, int focus = 1, bool autofilled = false) =>
        new(id, pastes, pasted, typed, focus, focus, 1000, autofilled);

    private static TelemetrySnapshot Snapshot(List<FieldTelemetry> fields, int visibilityChanges, string[] markers) => new()
    {
        SessionId = Guid.CreateVersion7(),
        PageViewId = Guid.CreateVersion7(),
        Sequence = 1,
        Fields = fields,
        Page = new PageTelemetry(visibilityChanges, markers),
        ReceivedAt = Now,
        ExpiresAt = Now.AddDays(1),
    };

    private static IntegrityContext Context(bool accommodation = false)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var workflow = HiringWorkflow.Default with
        {
            Questionnaire = new Questionnaire(
            [
                new Question("motivation", QuestionType.LongText, "Why do you want this job?"),
                new Question("notice", QuestionType.ShortText, "What is your notice period?"),
                new Question("portfolio", QuestionType.Url, "A link to your work"),
            ]),
        };
        var template = HiringTemplate.Create(organization, "Engineer", null, new HiringTemplateDefinition(workflow, PostingDefaults.None), Now, null);
        template.Publish(template.Versions[0], Now, null);
        job.UseTemplate(template.Versions[0]);
        var candidate = Candidate.Create(organization, "Casey", "Morgan", "casey@example.com", null, Now);
        var (application, _) = JobApplication.Submit(
            job, candidate, accommodation, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [], "2026-10-20.3", false, BotCheckResult.NotChecked));
        return new IntegrityContext(application, candidate, job, null, [], Now);
    }

    private sealed class FixedSource(IReadOnlyList<FieldTelemetry> fields) : ITelemetrySource
    {
        public int Reads { get; private set; }

        public Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<TelemetrySessionAggregates?>(new(applicationId, fields, new PageTelemetry(0, []), Now));
        }

        public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, IntegrityContext context) =>
            TelemetryRules.Evaluate(session, context);

        public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
