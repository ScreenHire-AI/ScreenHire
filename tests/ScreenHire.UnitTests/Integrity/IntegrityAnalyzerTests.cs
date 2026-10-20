using Microsoft.Extensions.Options;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>Each analyzer on its own: what it reports, what it never reports, and what it does when it cannot look.</summary>
public sealed class IntegrityAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly string EmDash = new((char)0x2014, 1);

    private static readonly string EnDash = new((char)0x2013, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Network_reports_a_hosting_provider_by_operator_and_country_never_the_address()
    {
        var result = await Network().AnalyzeAsync(Context(ip: "198.51.100.23", phone: null), Ct);

        var signal = result.Signals.Should().ContainSingle().Subject;
        signal.Code.Should().Be(SignalCatalogue.DatacenterIp);
        signal.Evidence.Select(e => e.Value).Should().Contain("Example Cloud Hosting (AS64497)").And.Contain("United States");
        signal.Evidence.Should().NotContain(e => (e.Value ?? "").Contains("198.51.100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Network_reports_a_vpn_as_context_with_counter_evidence_when_it_exits_in_the_claimed_country()
    {
        var result = await Network().AnalyzeAsync(Context(ip: "203.0.113.20", phone: "+44 113 496 0456"), Ct);

        var signal = result.Signals.Should().ContainSingle("a VPN on a hosting network is reported once, as the VPN").Subject;
        signal.Code.Should().Be(SignalCatalogue.AnonymousProxy);
        signal.CounterEvidence.Should().ContainSingle().Which.Explanation.Should().Contain("in the United Kingdom");
    }

    [Fact]
    public async Task Network_reports_a_location_mismatch_against_the_phone_numbers_country()
    {
        var result = await Network().AnalyzeAsync(Context(ip: "198.51.100.40", phone: "+44 161 496 0789"), Ct);

        result.Signals.Select(s => s.Code).Should().Equal(SignalCatalogue.DatacenterIp, SignalCatalogue.GeoClaimMismatch);
        result.Signals[1].Values!["claimed"].Should().Be("the United Kingdom");
        result.Signals[1].Values!["observed"].Should().Be("the United States");
    }

    [Fact]
    public async Task Network_says_why_it_saw_nothing()
    {
        (await Network().AnalyzeAsync(Context(ip: null), Ct)).Note.Should().Be("No network address was recorded for this application.");
        (await Network().AnalyzeAsync(Context(ip: "10.0.0.7"), Ct)).Note.Should().Be("Nothing is known about the network address.");
        var unconfigured = new NetworkAnalyzer(new IpIntelligence(Options.Create(new IntegrityOptions())));
        (await unconfigured.AnalyzeAsync(Context(ip: "198.51.100.23"), Ct)).Note.Should().Be("No IP intelligence source is configured.");
    }

    [Fact]
    public async Task Identity_reports_a_disposable_domain_without_looking_up_mail()
    {
        var mail = new FakeMailDomainChecker();

        var result = await Identity(mail).AnalyzeAsync(Context(email: "x@tempinbox.example.net"), Ct);

        result.Signals.Should().ContainSingle().Which.Code.Should().Be(SignalCatalogue.DisposableEmail);
        mail.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Identity_reports_a_domain_with_no_mail_server_and_skips_an_unknown_one()
    {
        var mail = new FakeMailDomainChecker { Answers = { ["nomail.example.com"] = MailDomainStatus.NoMailServer } };

        var flagged = await Identity(mail).AnalyzeAsync(Context(email: "sam@nomail.example.com"), Ct);
        var unknown = await Identity(mail).AnalyzeAsync(Context(email: "sam@unreachable.example.com"), Ct);

        flagged.Signals.Should().ContainSingle().Which.Code.Should().Be(SignalCatalogue.EmailWithoutMx);
        unknown.Signals.Should().BeEmpty("a lookup that fails says nothing about the candidate");
        unknown.Note.Should().Contain("not checked");
    }

    [Fact]
    public async Task Identity_reports_internet_and_invalid_phone_numbers_and_reads_nothing_into_a_bare_one()
    {
        var voip = await Identity().AnalyzeAsync(Context(phone: "+44 56 0123 4567"), Ct);
        var invalid = await Identity().AnalyzeAsync(Context(phone: "+1 555 0100"), Ct);
        var bare = await Identity().AnalyzeAsync(Context(phone: "020 7946 0123"), Ct);
        var landline = await Identity().AnalyzeAsync(Context(phone: "+44 20 7946 0123"), Ct);

        voip.Signals.Should().ContainSingle().Which.Code.Should().Be(SignalCatalogue.VoipPhone);
        invalid.Signals.Should().ContainSingle().Which.Values!["region"].Should().Be("the United States");
        bare.Signals.Should().BeEmpty("a number without a country code, for a job in two countries, cannot be checked");
        bare.Note.Should().Contain("no country code");
        landline.Signals.Should().BeEmpty();
    }

    [Fact]
    public async Task Automation_reports_the_honeypot_and_markers_with_a_passed_challenge_as_counter_evidence()
    {
        var result = await new AutomationAnalyzer().AnalyzeAsync(
            Context(honeypot: true, botCheck: BotCheckResult.Passed, markers: ["webdriver"]), Ct);

        result.Signals.Select(s => s.Code).Should().Equal(SignalCatalogue.Honeypot, SignalCatalogue.Webdriver);
        result.Signals.Should().AllSatisfy(s => s.CounterEvidence.Should().ContainSingle().Which.Explanation.Should().Be("The browser passed the bot challenge."));
    }

    [Fact]
    public async Task Automation_measures_submit_velocity_against_a_plausible_minimum()
    {
        var fast = await new AutomationAnalyzer().AnalyzeAsync(Context(taken: TimeSpan.FromSeconds(8)), Ct);
        var slow = await new AutomationAnalyzer().AnalyzeAsync(Context(taken: TimeSpan.FromMinutes(5)), Ct);

        var velocity = fast.Signals.Should().ContainSingle().Subject;
        velocity.Code.Should().Be(SignalCatalogue.SubmitVelocity);
        var metric = velocity.Evidence.Should().ContainSingle().Subject;
        metric.Kind.Should().Be(EvidenceKind.Metric);
        metric.Amount.Should().Be(8);
        metric.Unit.Should().Be("seconds");
        metric.Reference.Should().Be(AutomationAnalyzer.PlausibleMinimum([new ApplicationAnswer("a", "yes")]));
        slow.Signals.Should().BeEmpty();
    }

    [Fact]
    public async Task Automation_never_measures_timing_with_an_accommodation()
    {
        var result = await new AutomationAnalyzer().AnalyzeAsync(Context(taken: TimeSpan.FromSeconds(3), accommodation: true), Ct);

        result.Signals.Should().BeEmpty();
    }

    [Fact]
    public void A_filled_honeypot_with_an_accommodation_never_raises_a_level()
    {
        // Autofill and assistive tools can fill a hidden field, so the honeypot is dropped with an accommodation.
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var candidate = Candidate.Create(organization, "Casey Morgan", "casey@example.com", null, Now);
        var (application, _) = JobApplication.Submit(
            job, candidate, true, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [], "2026-10-20.2", HoneypotFilled: true));
        var observed = new ObservedSignal(SignalCatalogue.Honeypot, SignalConfidence.High, [SignalEvidence.Fact("Hidden field", "Filled in")]);

        var stored = IntegrityPipeline.ToSignals([observed], application, Guid.CreateVersion7(), new AutomationAnalyzer(), Now);
        var results = IntegrityRuleset.Assess(new IntegrityInputs(
            [IntegrityRulesetTests.Signal(SignalCatalogue.Honeypot), IntegrityRulesetTests.Signal(SignalCatalogue.DatacenterIp),
                IntegrityRulesetTests.Signal(SignalCatalogue.Webdriver)],
            Domain.Hiring.AiUsePolicy.AllowedWithDisclosure, AiUseDeclaration.NotProvided, AccommodationRequested: true));

        stored.Should().BeEmpty("accessibility-sensitive signals are never stored with an accommodation");
        results.Single(r => r.Kind == IntegrityAssessmentKind.Authenticity).ReasonCodes.Should().NotContain(SignalCatalogue.Honeypot);
    }

    [Fact]
    public async Task A_failed_bot_challenge_is_context_only()
    {
        var result = await new AutomationAnalyzer().AnalyzeAsync(Context(botCheck: BotCheckResult.Failed), Ct);

        result.Signals.Should().ContainSingle().Which.Code.Should().Be(SignalCatalogue.TurnstileFailed);
        SignalCatalogue.Get(SignalCatalogue.TurnstileFailed).ContextOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Behavioural_has_no_source_yet_and_never_asks_for_telemetry_with_an_accommodation()
    {
        var source = new CountingTelemetrySource();
        var analyzer = new BehaviouralAnalyzer(source);

        var with = await analyzer.AnalyzeAsync(Context(accommodation: true), Ct);
        var without = await analyzer.AnalyzeAsync(Context(), Ct);

        with.Signals.Should().BeEmpty();
        without.Signals.Should().BeEmpty();
        with.Note.Should().Be(without.Note, "the run record never reveals the accommodation");
        source.Calls.Should().Be(1);
    }

    [Fact]
    public void Every_catalogue_code_has_a_title_and_an_explanation_and_behavioural_codes_are_accessibility_sensitive()
    {
        SignalCatalogue.All.Values.Should().AllSatisfy(d =>
        {
            d.Title.Should().NotBeNullOrWhiteSpace();
            d.ExplanationTemplate.Should().NotBeNullOrWhiteSpace();
            // No em or en dashes in recruiter-facing copy (.claude/rules/writing-style.md).
            d.Title.Should().NotContain(EmDash).And.NotContain(EnDash);
            d.ExplanationTemplate.Should().NotContain(EmDash).And.NotContain(EnDash);
        });
        SignalCatalogue.All.Values.Where(d => d.Category == SignalCategory.Behavioural)
            .Should().OnlyContain(d => d.AccessibilitySensitive);
        SignalCatalogue.Get(SignalCatalogue.AnonymousProxy).ContextOnly.Should().BeTrue();
    }

    private static NetworkAnalyzer Network() =>
        new(new IpIntelligence(Options.Create(IntegrityHarness.DevelopmentOptions())));

    private static IdentityAnalyzer Identity(FakeMailDomainChecker? mail = null) =>
        new(new DisposableEmailDomains(Options.Create(IntegrityHarness.DevelopmentOptions())), mail ?? new FakeMailDomainChecker());

    private static IntegrityContext Context(
        string? ip = "192.0.2.10",
        string? phone = null,
        string email = "casey@example.com",
        bool honeypot = false,
        BotCheckResult botCheck = BotCheckResult.NotChecked,
        IReadOnlyList<string>? markers = null,
        TimeSpan? taken = null,
        bool accommodation = false)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer", ApplicantLocationRequirements = ["GB", "IE"] };
        var candidate = Candidate.Create(organization, "Casey Morgan", email, phone, Now);
        var (application, _) = JobApplication.Submit(
            job, candidate, accommodation, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [new ApplicationAnswer("a", "yes")], "2026-10-20.2", honeypot, botCheck));
        var observation = new SubmissionObservation
        {
            OrganizationId = organization,
            ApplicationId = application.Id,
            IpAddress = ip,
            FormStartedAt = taken is { } t ? Now - t : null,
            AutomationMarkers = [.. markers ?? []],
            ObservedAt = Now,
            ExpiresAt = Now.AddDays(30),
        };
        return new IntegrityContext(application, candidate, job, observation, [], Now);
    }

    private sealed class CountingTelemetrySource : ITelemetrySource
    {
        public int Calls { get; private set; }

        public Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<TelemetrySessionAggregates?>(null);
        }

        public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, IntegrityContext context) => [];

        public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
