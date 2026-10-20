using System.Net;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// The local adapters and helpers behind the analyzers (IP intelligence, disposable domains, MX, phone numbers, keyed
/// hashing), the Signal Network's off switch, the submission's raw data, and integrity levels as automation facts.
/// </summary>
public sealed class IntegrityAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Key = "unit-test-key-unit-test-key-0123456789"u8.ToArray();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.10.10")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void Private_addresses_say_nothing(string address) =>
        new IpIntelligence(Options.Create(IntegrityHarness.DevelopmentOptions())).Lookup(IPAddress.Parse(address)).Should().BeNull();

    [Fact]
    public void Configured_ranges_and_hosting_asns_describe_an_address()
    {
        var intelligence = new IpIntelligence(Options.Create(IntegrityHarness.DevelopmentOptions()));

        var hosting = intelligence.Lookup(IPAddress.Parse("198.51.100.9"))!;
        var home = intelligence.Lookup(IPAddress.Parse("::ffff:192.0.2.10"))!;
        var tor = intelligence.Lookup(IPAddress.Parse("203.0.113.200"))!;

        hosting.Should().BeEquivalentTo(new IpLookup(64497, "Example Cloud Hosting", "US", true, null, ["configured ranges"]));
        home.Hosting.Should().BeFalse();
        home.Country.Should().Be("GB");
        tor.Anonymizer.Should().Be(AnonymizerKind.Tor);
        intelligence.Lookup(IPAddress.Parse("8.8.8.8")).Should().BeNull("no source knows it");
    }

    [Fact]
    public void An_iptoasn_table_finds_the_range_and_marks_known_cloud_asns_as_hosting()
    {
        var table = IpIntelligence.Ip2AsnTable.Parse(
        [
            "1.0.0.0\t1.0.0.255\t13335\tUS\tCLOUDFLARENET",
            "2.0.0.0\t2.0.0.255\t0\tNone\tNot routed",
            "5.0.0.0\t5.0.0.255\t64496\tGB\tEXAMPLE-ISP",
        ]);

        table.Find(IPAddress.Parse("1.0.0.7")).Should().Be((13335, "US", "CLOUDFLARENET"));
        table.Find(IPAddress.Parse("2.0.0.7")).Should().BeNull("unrouted rows are skipped");
        table.Find(IPAddress.Parse("5.0.0.255")).Should().Be((64496, "GB", "EXAMPLE-ISP"));
        table.Find(IPAddress.Parse("6.0.0.1")).Should().BeNull();
    }

    [Fact]
    public void Disposable_domains_come_from_the_embedded_list_and_configuration_and_match_subdomains()
    {
        var domains = new DisposableEmailDomains(Options.Create(IntegrityHarness.DevelopmentOptions()));

        domains.Match("mailinator.com").Should().Be("mailinator.com");
        domains.Match("inbox.mailinator.com").Should().Be("mailinator.com");
        domains.Match("TempInbox.Example.Net").Should().Be("tempinbox.example.net");
        domains.Match("example.com").Should().BeNull();
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("mail.example.org", true)]
    [InlineData("tempinbox.example.net", true)]
    [InlineData("anything.test", true)]
    [InlineData("site.invalid", true)]
    [InlineData("brightwater.studio", false)]
    [InlineData("examples.com", false)]
    public void Reserved_test_domains_are_never_looked_up(string domain, bool reserved) =>
        DnsMailDomainChecker.IsReserved(domain).Should().Be(reserved);

    [Fact]
    public async Task Mx_lookups_can_be_switched_off()
    {
        var options = Options.Create(new IntegrityOptions { Identity = new IdentityCheckOptions { MxLookup = false } });
        var checker = new DnsMailDomainChecker(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<DnsMailDomainChecker>.Instance);

        (await checker.CheckAsync("brightwater.studio", TestContext.Current.CancellationToken)).Should().Be(MailDomainStatus.Unknown);
    }

    [Fact]
    public void Phone_numbers_are_read_with_their_country_and_line_type()
    {
        PhoneNumbersCheck.Inspect("+44 20 7946 0123", null).Should().BeEquivalentTo(
            new PhoneInspection(true, true, "GB", PhoneLineType.FixedLine, "+442079460123"));
        PhoneNumbersCheck.Inspect("+44 56 0123 4567", null).LineType.Should().Be(PhoneLineType.Voip);
        PhoneNumbersCheck.Inspect("020 7946 0123", "GB").E164.Should().Be("+442079460123");
        PhoneNumbersCheck.Inspect("020 7946 0123", null).Parsed.Should().BeFalse();
        // Ofcom's drama mobile range is not a valid number; its country code still says where it claims to be.
        PhoneNumbersCheck.Inspect("+44 7700 900123", null).Should().BeEquivalentTo(
            new PhoneInspection(true, false, "GB", PhoneLineType.Unknown, null));
        PhoneNumbersCheck.Inspect("not a number", "GB").Parsed.Should().BeFalse();
    }

    [Fact]
    public void Emails_normalise_without_their_plus_tag()
    {
        Identifiers.NormalizeEmail(" Sam+Jobs@Example.COM ").Should().Be("sam@example.com");
        Identifiers.NormalizeEmail("talentdesk+reed@example.net").Should().Be(Identifiers.NormalizeEmail("talentdesk+hale@example.net"));
        Identifiers.NormalizeEmail("not-an-email").Should().BeNull();
        Identifiers.EmailDomain("sam@Example.org").Should().Be("example.org");
    }

    [Fact]
    public void Device_hashes_match_within_an_organization_and_never_across()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var hash = KeyedHashing.DeviceHash(Key, first, "device-1");

        KeyedHashing.DeviceHash(Key, first, "device-1").Should().Be(hash).And.HaveLength(64).And.NotContain("device");
        KeyedHashing.DeviceHash(Key, second, "device-1").Should().NotBe(hash);
        KeyedHashing.DeviceHash(Key, first, " ").Should().BeNull();
    }

    [Fact]
    public void Network_hashes_normalise_identifiers_and_need_a_long_key()
    {
        KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Email, "Sam+x@Example.com")
            .Should().Be(KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Email, "sam@example.com"));
        KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Phone, "+44 20 7946 0123")
            .Should().Be(KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Phone, "+442079460123"));
        KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Email, "sam@example.com")
            .Should().NotBe(KeyedHashing.NetworkHash(Key, SignalNetworkIdentifierKind.Device, "sam@example.com"));
        KeyedHashing.ParseKey("too short").Should().BeNull();
        KeyedHashing.ParseKey(new string('k', 32)).Should().HaveCount(32);
    }

    [Fact]
    public void The_signal_network_is_off_by_default_and_a_host_refuses_to_start_with_it_on()
    {
        new IntegrityOptions().SignalNetwork.Enabled.Should().BeFalse();

        var gate = new SignalNetworkGate();

        gate.Validate(null, new IntegrityOptions()).Succeeded.Should().BeTrue();
        gate.Validate(null, new IntegrityOptions { SignalNetwork = new SignalNetworkOptions { Enabled = true } })
            .Failed.Should().BeTrue();
    }

    [Fact]
    public void A_host_refuses_to_keep_raw_network_data_longer_than_the_privacy_notice_promises()
    {
        var limit = new RetentionLimit();

        limit.Validate(null, new IntegrityOptions()).Succeeded.Should().BeTrue();
        limit.Validate(null, Keeping(1)).Succeeded.Should().BeTrue();
        limit.Validate(null, Keeping(30)).Succeeded.Should().BeTrue();
        limit.Validate(null, Keeping(31)).Failed.Should().BeTrue("the careers notice says within 30 days");
        limit.Validate(null, Keeping(0)).Failed.Should().BeTrue();

        static IntegrityOptions Keeping(int days) => new() { Retention = new RetentionOptions { RawNetworkDays = days } };
    }

    [Fact]
    public void The_worker_refuses_to_start_with_an_ip_data_file_that_does_not_exist()
    {
        var files = new NetworkSourceFiles();
        var existing = Path.GetTempFileName();
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tsv");
        try
        {
            files.Validate(null, new IntegrityOptions()).Succeeded.Should().BeTrue("an unset file is a source that is off");
            files.Validate(null, Reading(existing, existing)).Succeeded.Should().BeTrue();

            var result = files.Validate(null, Reading(missing, existing));

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Ip2AsnFile").And.NotContain("TorExitListFile");
        }
        finally
        {
            File.Delete(existing);
        }

        static IntegrityOptions Reading(string ip2Asn, string torExits) =>
            new() { Network = new NetworkIntelligenceOptions { Ip2AsnFile = ip2Asn, TorExitListFile = torExits } };
    }

    [Fact]
    public void The_synthetic_applicants_saw_the_notice_version_the_careers_site_shows()
    {
        var apply = File.ReadAllText(RepositoryFiles.PathTo("apps", "careers", "src", "lib", "apply.ts"));
        var careers = System.Text.RegularExpressions.Regex.Match(apply, @"export const NOTICE_VERSION = '([^']+)'");

        careers.Success.Should().BeTrue("apps/careers/src/lib/apply.ts exports NOTICE_VERSION");
        SyntheticApplicantsDemo.PrivacyNoticeVersion.Should().Be(careers.Groups[1].Value);
    }

    [Fact]
    public void A_submission_stores_a_salted_device_hash_clean_markers_and_a_plausible_start_only()
    {
        var application = Application();
        var options = new IntegrityOptions { DeviceHashKey = new string('k', 40) };

        var observation = new SubmissionSession(
            "192.0.2.10", "Mozilla/5.0", "device-1", Now.AddMinutes(-5), ["WebDriver", "webdriver", "bad marker!", ""])
            .ToObservation(application, options, Now);
        var future = new SubmissionSession(null, null, "device-1", Now.AddMinutes(5), null).ToObservation(application, new IntegrityOptions(), Now);

        observation.DeviceHash.Should().Be(KeyedHashing.DeviceHash(KeyedHashing.ParseKey(options.DeviceHashKey)!, application.OrganizationId, "device-1"));
        observation.AutomationMarkers.Should().Equal("webdriver");
        observation.FormStartedAt.Should().Be(Now.AddMinutes(-5));
        observation.ExpiresAt.Should().Be(Now.AddDays(30));
        future.FormStartedAt.Should().BeNull("a start in the future is not believable");
        future.DeviceHash.Should().BeNull("without a key, no fingerprint is stored");
    }

    [Fact]
    public void A_submission_with_an_accommodation_stores_no_timing()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var candidate = Candidate.Create(organization, "Casey Morgan", "casey@example.com", null, Now);
        var application = JobApplication.Submit(job, candidate, true, AiUseDeclaration.NotProvided, null, Now, null).Application;

        var observation = new SubmissionSession("192.0.2.10", "Mozilla/5.0", null, Now.AddMinutes(-5), null)
            .ToObservation(application, new IntegrityOptions(), Now);

        observation.FormStartedAt.Should().BeNull();
    }

    [Fact]
    public void Automation_facts_read_the_latest_integrity_levels_before_the_trigger()
    {
        var application = Application();
        var submitted = Event(application, ApplicationEventType.Submitted, new ApplicationEventData(ToStageId: "applied"), Now);
        var first = Event(application, ApplicationEventType.IntegrityAssessed,
            new ApplicationEventData(AuthenticityLevel: "NoConcerns", AiAssistanceLevel: "NotIndicated"), Now.AddMinutes(1));
        var second = Event(application, ApplicationEventType.IntegrityAssessed,
            new ApplicationEventData(AuthenticityLevel: "ReviewSuggested", AiAssistanceLevel: "SomeIndicators"), Now.AddMinutes(2));
        ApplicationEvent[] timeline = [submitted, first, second];

        var atFirst = AutomationFacts.FromTimeline(timeline, first.Id, TriggerType.IntegrityAssessed, first.OccurredAt);
        var atSecond = AutomationFacts.FromTimeline(timeline, second.Id, TriggerType.IntegrityAssessed, second.OccurredAt);
        var beforeAny = AutomationFacts.FromTimeline(timeline, submitted.Id, TriggerType.ApplicationSubmitted, submitted.OccurredAt);

        atFirst.ValuesOf(FactSchema.IntegrityAuthenticity).Should().Equal("NoConcerns");
        atSecond.ValuesOf(FactSchema.IntegrityAuthenticity).Should().Equal("ReviewSuggested");
        atSecond.ValuesOf(FactSchema.IntegrityAiAssistance).Should().Equal("SomeIndicators");
        beforeAny.ValuesOf(FactSchema.IntegrityAuthenticity).Should().BeEmpty();
    }

    private static JobApplication Application()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var candidate = Candidate.Create(organization, "Casey Morgan", "casey@example.com", null, Now);
        return JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null).Application;
    }

    private static ApplicationEvent Event(JobApplication application, ApplicationEventType type, ApplicationEventData data, DateTimeOffset at) =>
        new() { OrganizationId = application.OrganizationId, ApplicationId = application.Id, Type = type, Data = data, OccurredAt = at };
}
