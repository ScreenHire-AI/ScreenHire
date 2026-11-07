using Microsoft.Extensions.Options;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// What an organization controls (verification-engine.md §10): the groups of checks, the domains and networks it trusts and
/// the device check basis. The pipeline's skipping of a group needs the database, so the integration tests cover it; these
/// cover the rules the pipeline and the analyzers apply.
/// </summary>
public sealed class OrganizationControlsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_analyzer_the_pipeline_runs_belongs_to_one_check_group_and_no_group_names_a_missing_analyzer()
    {
        var names = new VerificationHarness().Analyzers().Select(a => a.Name).ToList();

        names.Should().OnlyContain(name => CheckGroups.Of(name) != null, "an analyzer outside the map could not be turned off");
        CheckGroups.ByAnalyzer.Keys.Should().BeEquivalentTo(names, "a name in the map that no analyzer has is a switch that does nothing");
        Enum.GetValues<CheckGroup>().Should().OnlyContain(group => CheckGroups.ByAnalyzer.Values.Contains(group));
    }

    [Fact]
    public void A_group_that_is_off_says_so_in_words_a_reviewer_can_read()
    {
        CheckGroups.TurnedOffNote(CheckGroup.FormUse).Should().Be("Form use checks are turned off in this organization's settings.");
        RunNotes.KindOf("behavioural", CheckGroups.TurnedOffNote(CheckGroup.FormUse)).Should().Be(CheckNotRunKind.TurnedOff);
        RunNotes.KindOf("network", RunNotes.TrustedNetwork).Should().Be(CheckNotRunKind.Trusted);
        RunNotes.KindOf("duplicates", RunNotes.DeviceNotCompared).Should().Be(CheckNotRunKind.TurnedOff);
        RunNotes.KindOf("duplicates", RunNotes.TrustedEmailAndDeviceNotCompared).Should().Be(CheckNotRunKind.Trusted);
        RunNotes.KindOf("network", "No IP intelligence source is configured.").Should().BeNull("only the organization's own settings are listed as not checked");
        RunNotes.KindOf("network", null).Should().BeNull();
    }

    [Fact]
    public void The_defaults_run_every_group_and_trust_nothing()
    {
        var config = VerificationConfig.From(null);

        Enum.GetValues<CheckGroup>().Should().OnlyContain(group => config.Runs(group));
        config.Trusted.IsEmpty.Should().BeTrue();
        new VerificationSettings().ToConfig().Should().BeEquivalentTo(config);
    }

    [Fact]
    public void A_group_turned_off_in_the_settings_does_not_run()
    {
        var config = new VerificationSettings { CheckDuplicates = false, CheckFormUse = false }.ToConfig();

        config.Runs(CheckGroup.Duplicates).Should().BeFalse();
        config.Runs(CheckGroup.FormUse).Should().BeFalse();
        config.Runs(CheckGroup.Network).Should().BeTrue();
    }

    [Theory]
    [InlineData("Partner.Example.COM", "partner.example.com")]
    [InlineData("@partner.example.com", "partner.example.com")]
    [InlineData(" partner.example.com. ", "partner.example.com")]
    public void A_trusted_domain_is_stored_lower_case_without_an_at_sign_or_dots_around_it(string typed, string stored) =>
        TrustedSources.NormalizeDomain(typed).Should().Be(stored);

    [Theory]
    [InlineData("localhost")]
    [InlineData("not a domain.com")]
    [InlineData("-bad.example.com")]
    [InlineData("a..b.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Something_that_is_not_a_domain_is_refused(string? typed) => TrustedSources.NormalizeDomain(typed).Should().BeNull();

    [Fact]
    public void A_trusted_domain_covers_its_subdomains_and_nothing_that_only_ends_the_same()
    {
        var trust = TrustedSources.From(["partner.example.com"], []);

        trust.TrustsEmail("recruiter@partner.example.com").Should().BeTrue();
        trust.TrustsEmail("recruiter@mail.partner.example.com").Should().BeTrue();
        trust.TrustsEmail("recruiter@notpartner.example.com").Should().BeFalse("a name that only ends with the domain is another domain");
        trust.TrustsEmail("recruiter@example.com").Should().BeFalse();
        trust.TrustsEmail("no-at-sign").Should().BeFalse();
        trust.TrustsEmail(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("203.0.113.0/24", "203.0.113.0/24")]
    [InlineData("203.0.113.7", "203.0.113.7/32")]
    [InlineData("2001:db8:abcd::/48", "2001:db8:abcd::/48")]
    public void A_trusted_network_is_stored_in_its_canonical_form(string typed, string stored) =>
        TrustedSources.NormalizeNetwork(typed).Should().Be(stored);

    [Theory]
    [InlineData("10.0.0.0/8", "too wide")]
    [InlineData("0.0.0.0/0", "too wide")]
    [InlineData("2001:db8::/32", "too wide")]
    [InlineData("not-a-network", "Enter a network")]
    [InlineData("203.0.113.0/33", "Enter a network")]
    [InlineData("", "Enter a network")]
    public void A_network_that_would_trust_a_large_part_of_the_internet_or_is_not_a_network_is_refused(string typed, string problem)
    {
        TrustedSources.TryParseNetwork(typed, out _, out var message).Should().BeFalse();

        message.Should().Contain(problem);
        TrustedSources.NormalizeNetwork(typed).Should().BeNull();
    }

    [Fact]
    public void A_trusted_network_matches_the_addresses_inside_it_including_an_ipv4_address_in_ipv6_form()
    {
        var trust = TrustedSources.From([], ["203.0.113.0/24"]);

        trust.TrustsAddress("203.0.113.200").Should().BeTrue();
        trust.TrustsAddress("::ffff:203.0.113.5").Should().BeTrue();
        trust.TrustsAddress("203.0.114.1").Should().BeFalse();
        trust.TrustsAddress("not an address").Should().BeFalse();
        trust.TrustsAddress(null).Should().BeFalse();
    }

    [Fact]
    public void A_stored_entry_that_no_longer_reads_is_ignored_never_trusted()
    {
        var trust = TrustedSources.From(["", "single"], ["garbage", "10.0.0.0/8"]);

        trust.IsEmpty.Should().BeTrue();
        trust.TrustsAddress("10.1.2.3").Should().BeFalse("the /8 is wider than a network may be");
    }

    [Theory]
    [InlineData(null, "Consent", "Consent")] // follows the region
    [InlineData("Notice", "Consent", "Consent")] // the region's floor
    [InlineData("Consent", "Notice", "Consent")] // asks for more protection than a Notice region needs
    [InlineData("Off", "Notice", "Off")]
    [InlineData("Notice", "Notice", "Notice")]
    public void The_organization_can_ask_for_more_protection_than_its_region(string? choice, string region, string expected)
    {
        var options = new DeviceCheckOptions { Default = Enum.Parse<DeviceCheckBasis>(region) };

        options.Resolve([], choice is null ? null : Enum.Parse<DeviceCheckBasis>(choice)).Should().Be(Enum.Parse<DeviceCheckBasis>(expected));
    }

    [Fact]
    public void An_organization_can_never_lower_what_its_region_needs()
    {
        var options = new DeviceCheckOptions
        {
            Default = DeviceCheckBasis.Notice,
            Countries = new Dictionary<string, DeviceCheckBasis>(StringComparer.OrdinalIgnoreCase)
            {
                ["DE"] = DeviceCheckBasis.Consent,
                ["CH"] = DeviceCheckBasis.Off,
            },
        };

        options.Resolve(["DE"], DeviceCheckBasis.Notice).Should().Be(DeviceCheckBasis.Consent, "the region needs consent");
        options.Resolve(["DE"], null).Should().Be(DeviceCheckBasis.Consent);
        options.Resolve(["CH"], DeviceCheckBasis.Consent).Should().Be(DeviceCheckBasis.Off, "no fingerprint at all is more protective than asking");
        options.Resolve(["US"], DeviceCheckBasis.Consent).Should().Be(DeviceCheckBasis.Consent);
        options.Resolve(["US"], DeviceCheckBasis.Off).Should().Be(DeviceCheckBasis.Off);
        options.Resolve(["US", "DE"], DeviceCheckBasis.Notice).Should().Be(DeviceCheckBasis.Consent, "a job in several countries takes the most protective");
    }

    [Fact]
    public async Task A_shared_email_from_a_trusted_domain_does_not_raise_a_duplicate_and_the_run_says_why()
    {
        var other = Guid.CreateVersion7();
        var analyzer = new DuplicateAnalyzer(new Directory(new SharedIdentifierMatch(SharedIdentifier.Email, [other])));

        var trusted = await analyzer.AnalyzeAsync(Context(trustedDomains: ["example.com"]), Ct);
        var untrusted = await analyzer.AnalyzeAsync(Context(), Ct);

        trusted.Signals.Should().BeEmpty();
        trusted.Note.Should().Be(RunNotes.TrustedEmail);
        trusted.RelatedApplicationIds.Should().BeEquivalentTo([other], "the earlier application is still re-checked under its own settings");
        untrusted.Signals.Should().ContainSingle();
        untrusted.Note.Should().BeNull();
    }

    [Fact]
    public async Task A_trusted_domain_stops_the_email_repeating_but_a_shared_phone_still_counts()
    {
        var other = Guid.CreateVersion7();
        var analyzer = new DuplicateAnalyzer(new Directory(
            new SharedIdentifierMatch(SharedIdentifier.Email, [other]), new SharedIdentifierMatch(SharedIdentifier.Phone, [other])));

        var result = await analyzer.AnalyzeAsync(Context(trustedDomains: ["example.com"]), Ct);

        var signal = result.Signals.Should().ContainSingle().Subject;
        signal.Values!["shared"].Should().Be("a phone number");
        result.Note.Should().Be(RunNotes.TrustedEmail);
    }

    [Fact]
    public async Task A_shared_device_from_a_trusted_network_is_not_counted_but_a_shared_resume_file_is()
    {
        var other = Guid.CreateVersion7();
        var analyzer = new DuplicateAnalyzer(new Directory(
            new SharedIdentifierMatch(SharedIdentifier.Device, [other]), new SharedIdentifierMatch(SharedIdentifier.ResumeFile, [other])));

        var result = await analyzer.AnalyzeAsync(Context(ip: "203.0.113.20", trustedNetworks: ["203.0.113.0/24"]), Ct);
        var elsewhere = await analyzer.AnalyzeAsync(Context(ip: "203.0.114.20", trustedNetworks: ["203.0.113.0/24"]), Ct);

        result.Signals.Single().Values!["shared"].Should().Be("an identical resume file");
        result.Note.Should().Be(RunNotes.TrustedDevice);
        elsewhere.Signals.Single().Values!["shared"].Should().Be("a device and an identical resume file");
        elsewhere.Note.Should().BeNull();
    }

    [Fact]
    public async Task A_trusted_network_raises_no_network_signal_and_the_run_says_why()
    {
        var analyzer = new NetworkAnalyzer(new IpIntelligence(Options.Create(VerificationHarness.DevelopmentOptions())));

        // 198.51.100.0/24 is a hosting provider in the Development ranges: a datacenter signal unless the organization trusts it.
        var raised = await analyzer.AnalyzeAsync(Context(ip: "198.51.100.23"), Ct);
        var trusted = await analyzer.AnalyzeAsync(Context(ip: "198.51.100.23", trustedNetworks: ["198.51.100.0/24"]), Ct);

        raised.Signals.Should().ContainSingle().Which.Code.Should().Be(SignalCatalogue.DatacenterIp);
        trusted.Signals.Should().BeEmpty();
        trusted.Note.Should().Be(RunNotes.TrustedNetwork);
    }

    private static VerificationContext Context(string? ip = "192.0.2.10", string[]? trustedDomains = null, string[]? trustedNetworks = null)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var candidate = Candidate.Create(organization, "Casey", "Morgan", "casey@example.com", null, Now);
        var (application, _) = JobApplication.Submit(
            job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [new ApplicationAnswer("a", "yes")], "2026-11-07.1", false, BotCheckResult.NotChecked));
        var observation = new SubmissionObservation
        {
            OrganizationId = organization,
            ApplicationId = application.Id,
            IpAddress = ip,
            ObservedAt = Now,
            ExpiresAt = Now.AddDays(30),
        };
        var settings = new VerificationSettings { OrganizationId = organization, TrustedDomains = trustedDomains ?? [], TrustedNetworks = trustedNetworks ?? [] };
        return new VerificationContext(application, candidate, job, observation, [], Now, settings.ToConfig());
    }

    private sealed class Directory(params SharedIdentifierMatch[] matches) : IApplicantDirectory
    {
        public Task<IReadOnlyList<SharedIdentifierMatch>> FindSharedAsync(VerificationContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SharedIdentifierMatch>>(matches);
    }
}
