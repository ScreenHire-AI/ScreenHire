using Microsoft.Extensions.Options;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// Runs the real analyzers and ruleset in memory, with the Development IP ranges, a fake DNS and an in-memory directory
/// of the organization's earlier applicants: the pipeline without the database. Applicants are submitted one after
/// another to one job, as the careers form would.
/// </summary>
internal sealed class IntegrityHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] DeviceKey = "unit-test-device-hash-key-0123456789"u8.ToArray();

    private readonly List<Submitted> earlier = [];
    private readonly Guid organizationId = Guid.CreateVersion7();
    private DateTimeOffset clock = Start;

    public IntegrityHarness()
    {
        // Brightwater's demo job: remote, hiring in the UK and Ireland, AI help allowed with disclosure.
        Job = new Job { OrganizationId = organizationId, Title = "Senior .NET Engineer", Slug = "senior-dotnet-engineer", WorkplaceType = WorkplaceType.Remote };
        Job.ApplicantLocationRequirements = ["GB", "IE"];
        Options = Microsoft.Extensions.Options.Options.Create(DevelopmentOptions());
    }

    public Job Job { get; }

    public IOptions<IntegrityOptions> Options { get; }

    public FakeMailDomainChecker Mail { get; } = new();

    /// <summary>The options Development runs with (apps/worker/appsettings.Development.json).</summary>
    public static IntegrityOptions DevelopmentOptions() => new()
    {
        Network = new NetworkIntelligenceOptions
        {
            Ranges =
            [
                new IpRangeOptions { Cidr = "192.0.2.0/24", Asn = 64496, Organization = "Example Home Broadband", Country = "GB" },
                new IpRangeOptions { Cidr = "198.51.100.0/24", Asn = 64497, Organization = "Example Cloud Hosting", Country = "US", Hosting = true },
                new IpRangeOptions { Cidr = "203.0.113.0/25", Asn = 64498, Organization = "Example VPN", Country = "GB", Hosting = true, Anonymizer = AnonymizerKind.Vpn },
                new IpRangeOptions { Cidr = "203.0.113.128/25", Asn = 64499, Organization = "Example Relay", Country = "DE", Anonymizer = AnonymizerKind.Tor },
            ],
        },
        Identity = new IdentityCheckOptions { ExtraDisposableDomains = ["tempinbox.example.net"] },
    };

    public IReadOnlyList<IIntegrityAnalyzer> Analyzers() =>
    [
        new NetworkAnalyzer(new IpIntelligence(Options)),
        new IdentityAnalyzer(new DisposableEmailDomains(Options), Mail),
        new AutomationAnalyzer(),
        new DuplicateAnalyzer(new InMemoryDirectory(earlier)),
        new BehaviouralAnalyzer(new NoTelemetrySource()),
    ];

    /// <summary>Submits <paramref name="applicant"/> a minute after the previous one and runs the pipeline over it.</summary>
    public async Task<HarnessResult> SubmitAsync(SyntheticApplicant applicant, bool accommodation = false)
    {
        clock = clock.AddMinutes(1);
        var candidate = Candidate.Create(organizationId, applicant.FullName, applicant.Email, applicant.Phone, clock);
        var resume = applicant.ResumeBytes();
        var uploadId = Guid.CreateVersion7();
        var intake = new ApplicationIntake(
            ApplicationSource.CareersSite,
            [new ApplicationAnswer("resume", uploadId.ToString()), .. applicant.Answers],
            "2026-10-20.2",
            applicant.HoneypotFilled,
            BotCheckResult.NotChecked);
        var (application, _) = JobApplication.Submit(Job, candidate, accommodation, applicant.AiUseDeclaration, applicant.AiUseDetails, clock, null, intake);
        var observation = new SubmissionObservation
        {
            OrganizationId = organizationId,
            ApplicationId = application.Id,
            IpAddress = applicant.IpAddress,
            UserAgent = applicant.UserAgent,
            DeviceHash = KeyedHashing.DeviceHash(DeviceKey, organizationId, applicant.DeviceFingerprint),
            FormStartedAt = clock - applicant.TimeToComplete,
            AutomationMarkers = [.. applicant.AutomationMarkers],
            ObservedAt = clock,
            ExpiresAt = clock.AddDays(30),
        };
        var file = new ApplicationFile
        {
            OrganizationId = organizationId,
            ApplicationId = application.Id,
            QuestionId = "resume",
            UploadId = uploadId,
            BlobName = $"{organizationId}/{Job.Id}/resume/{uploadId}/{applicant.ResumeFileName}",
            FileName = applicant.ResumeFileName,
            ContentType = "application/pdf",
            SizeBytes = resume.Length,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(resume)),
            UploadedAt = clock,
        };

        var context = new IntegrityContext(application, candidate, Job, observation, [file], clock);
        var signals = new List<IntegritySignal>();
        foreach (var analyzer in Analyzers())
        {
            var result = await analyzer.AnalyzeAsync(context, TestContext.Current.CancellationToken);
            signals.AddRange(IntegrityPipeline.ToSignals(result.Signals, application, Guid.CreateVersion7(), analyzer, clock));
        }

        var inputs = new IntegrityInputs(signals, Job.Workflow.IntegrityPolicy.AiUse, application.AiUseDeclaration, application.AccommodationRequested);
        earlier.Add(new Submitted(application, candidate, observation, file));
        return new HarnessResult(signals, IntegrityRuleset.Assess(inputs));
    }

    private sealed record Submitted(JobApplication Application, Candidate Candidate, SubmissionObservation Observation, ApplicationFile File);

    /// <summary>The database directory's rules over the applicants submitted so far.</summary>
    private sealed class InMemoryDirectory(List<Submitted> submitted) : IApplicantDirectory
    {
        public Task<IReadOnlyList<SharedIdentifierMatch>> FindSharedAsync(IntegrityContext context, CancellationToken cancellationToken)
        {
            var others = submitted.Where(s => s.Candidate.Id != context.Candidate.Id && s.Application.Id != context.Application.Id).ToList();
            var email = Identifiers.NormalizeEmail(context.Candidate.Email);
            var phone = PhoneNumbersCheck.Inspect(context.Candidate.Phone, context.JobCountry).E164;
            var matches = new List<SharedIdentifierMatch>();
            void Add(SharedIdentifier kind, Func<Submitted, bool> shares)
            {
                var ids = others.Where(shares).Select(s => s.Application.Id).ToList();
                if (ids.Count > 0)
                {
                    matches.Add(new SharedIdentifierMatch(kind, ids));
                }
            }

            Add(SharedIdentifier.Email, s => email is not null && Identifiers.NormalizeEmail(s.Candidate.Email) == email);
            Add(SharedIdentifier.Phone, s => phone is not null && PhoneNumbersCheck.Inspect(s.Candidate.Phone, context.JobCountry).E164 == phone);
            Add(SharedIdentifier.Device, s => context.Observation?.DeviceHash is { } device && s.Observation.DeviceHash == device);
            Add(SharedIdentifier.ResumeFile, s => context.Files.Any(f => f.Sha256 == s.File.Sha256));
            return Task.FromResult<IReadOnlyList<SharedIdentifierMatch>>(matches);
        }
    }
}

internal sealed record HarnessResult(IReadOnlyList<IntegritySignal> Signals, IReadOnlyList<RulesetResult> Assessments)
{
    public RulesetResult Authenticity => Assessments.Single(a => a.Kind == IntegrityAssessmentKind.Authenticity);

    public RulesetResult AiAssistance => Assessments.Single(a => a.Kind == IntegrityAssessmentKind.AiAssistance);
}

/// <summary>DNS that answers from a table; anything not in it is <see cref="MailDomainStatus.Unknown"/>, like a failed lookup.</summary>
internal sealed class FakeMailDomainChecker : IMailDomainChecker
{
    public Dictionary<string, MailDomainStatus> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public Task<MailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken)
    {
        Asked.Add(domain);
        return Task.FromResult(Answers.GetValueOrDefault(domain, MailDomainStatus.Unknown));
    }
}
