using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Integrity;
using ScreenHire.Application.Integrity.Content;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Integrity.Documents;
using ScreenHire.Infrastructure.Integrity.Llm;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// Runs the real analyzers and ruleset in memory, with the Development IP ranges, a fake DNS and an in-memory directory
/// of the organization's earlier applicants: the pipeline without the database. Applicants are submitted one after
/// another to one job, as the careers form would. Content forensics run too: each resume is extracted for real, and the
/// consistency check asks <see cref="Llm"/> (the local fake by default), as the worker's stages would before the
/// pipeline reads their results.
/// </summary>
internal sealed class IntegrityHarness
{
    /// <summary>The "Software engineer" system template's questions (migration <c>AddHiringModel</c>), which the synthetic answers fit.</summary>
    public static readonly IReadOnlyList<Question> SoftwareEngineerQuestions =
    [
        new("resume", QuestionType.File, "Your CV or resume", Required: true, AcceptedFileTypes: [".pdf", ".docx"]),
        new("work_authorization", QuestionType.YesNo, "Do you have the right to work in the country this job is in?", Required: true),
        new("experience", QuestionType.SingleChoice, "How many years have you worked as a software engineer?", Required: true,
            Options: [new("less_than_3", "Less than 3"), new("three_to_five", "3 to 5"), new("six_to_nine", "6 to 9"), new("ten_plus", "10 or more")]),
        new("work_sample", QuestionType.Url, "A link to code or a project you are proud of"),
        new("notice_period", QuestionType.ShortText, "What is your notice period?", MaxLength: 100),
    ];

    public static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] DeviceKey = "unit-test-device-hash-key-0123456789"u8.ToArray();

    private readonly List<Submitted> earlier = [];
    private readonly Guid organizationId = Guid.CreateVersion7();
    private DateTimeOffset clock = Start;

    public IntegrityHarness(IReadOnlyList<Question>? questions = null)
    {
        // Brightwater's demo job: remote, hiring in the UK and Ireland, AI help allowed with disclosure, with the
        // "Software engineer" questions.
        Job = new Job { OrganizationId = organizationId, Title = "Senior .NET Engineer", Slug = "senior-dotnet-engineer", WorkplaceType = WorkplaceType.Remote };
        var workflow = HiringWorkflow.Default with { Questionnaire = new Questionnaire(questions ?? SoftwareEngineerQuestions) };
        var template = HiringTemplate.Create(organizationId, "Software engineer", null, new HiringTemplateDefinition(workflow, PostingDefaults.None), Start, null);
        template.Publish(template.Draft!, Start, null);
        Job.UseTemplate(template.Versions[0]);
        Job.ApplicantLocationRequirements = ["GB", "IE"];
        Options = Microsoft.Extensions.Options.Options.Create(DevelopmentOptions());
    }

    public Job Job { get; }

    /// <summary>The model the consistency check asks: the local fake unless a test sets another.</summary>
    public LlmAccess Llm { get; set; } = LlmAccess.Create(new LlmOptions { Provider = LlmProvider.Fake });

    /// <summary>Whether the organization has the consistency check on.</summary>
    public bool ConsistencyCheckEnabled { get; set; } = true;

    public InMemoryContentRecords Records { get; } = new();

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

    /// <summary>The analyzers, with <paramref name="telemetry"/> as what the careers page's module sent.</summary>
    public IReadOnlyList<IIntegrityAnalyzer> Analyzers(SyntheticTelemetry? telemetry = null) =>
    [
        new NetworkAnalyzer(new IpIntelligence(Options)),
        new IdentityAnalyzer(new DisposableEmailDomains(Options), Mail),
        new AutomationAnalyzer(),
        new DuplicateAnalyzer(new InMemoryDirectory(earlier)),
        new BehaviouralAnalyzer(new InMemoryTelemetrySource(telemetry, clock)),
        new DocumentAnalyzer(Records),
        new AnswerAnalyzer(Records),
        new ConsistencyAnalyzer(Records),
    ];

    /// <summary>Submits <paramref name="applicant"/> a minute after the previous one and runs the pipeline over it.</summary>
    public Task<HarnessResult> SubmitAsync(SyntheticApplicant applicant, bool accommodation = false) =>
        SubmitAsync(applicant, applicant.Answers, accommodation);

    /// <summary>Submits <paramref name="applicant"/> with <paramref name="answers"/> in place of their own.</summary>
    public async Task<HarnessResult> SubmitAsync(SyntheticApplicant applicant, IReadOnlyList<ApplicationAnswer> answers, bool accommodation = false)
    {
        clock = clock.AddMinutes(1);
        var candidate = Candidate.Create(organizationId, applicant.FirstName, applicant.LastName, applicant.Email, applicant.Phone, clock);
        var resume = applicant.ResumeBytes();
        var uploadId = Guid.CreateVersion7();
        var intake = new ApplicationIntake(
            ApplicationSource.CareersSite,
            [new ApplicationAnswer("resume", uploadId.ToString()), .. answers],
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
        await PrepareContentAsync(application, file, resume);

        var signals = new List<IntegritySignal>();
        var notes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var analyzer in Analyzers(applicant.Telemetry))
        {
            var result = await analyzer.AnalyzeAsync(context, TestContext.Current.CancellationToken);
            result.Ready.Should().BeTrue($"the harness prepares every input before {analyzer.Name} runs");
            notes[analyzer.Name] = result.Note;
            signals.AddRange(IntegrityPipeline.ToSignals(result.Signals, application, Guid.CreateVersion7(), analyzer, clock));
        }

        var inputs = new IntegrityInputs(signals, Job.Workflow.IntegrityPolicy.AiUse, application.AiUseDeclaration, application.AccommodationRequested);
        earlier.Add(new Submitted(application, candidate, observation, file));
        Records.Earlier.Add(new EarlierApplication(application.Id, Job.Title, application.SubmittedAt, application.Answers));
        return new HarnessResult(signals, IntegrityRuleset.Assess(inputs), notes);
    }

    // What ExtractApplicationDocument and CheckApplicationConsistency store before the pipeline reads them.
    private async Task PrepareContentAsync(JobApplication application, ApplicationFile file, byte[] resume)
    {
        var extracted = DocumentExtraction.Extract(resume, ".pdf", TestContext.Current.CancellationToken);
        var document = new ApplicationDocument
        {
            OrganizationId = organizationId,
            ApplicationId = application.Id,
            FileId = file.Id,
            Kind = ApplicationText.KindOf(file.QuestionId),
            Status = extracted.Status,
            ExtractorVersion = DocumentExtraction.Version,
            Text = extracted.Text,
            PageCount = extracted.PageCount,
            Metadata = extracted.Metadata,
            HiddenText = extracted.HiddenText,
            Note = extracted.Note,
            ExtractedAt = clock,
        };
        Records.Documents.Add(document);

        var check = ConsistencyCheck.Claim(organizationId, application.Id, ConsistencyPrompts.Version, clock);
        var sources = CheckApplicationConsistencyHandler.Sources([document], ApplicationText.AnswersSource(Job, application.Answers));
        var outcome = ConsistencyCheckEnabled
            ? await new ConsistencyChecker(Llm, NullLogger<ConsistencyChecker>.Instance).CheckAsync(sources, TestContext.Current.CancellationToken)
            : new ConsistencyCheckOutcome(ConsistencyCheckStatus.Disabled, "The organization has the consistency check turned off.");
        check.Complete(outcome, clock);
        Records.Checks.Add(check);
    }

    private sealed record Submitted(JobApplication Application, Candidate Candidate, SubmissionObservation Observation, ApplicationFile File);

    /// <summary>The database source's rules over telemetry held in memory.</summary>
    private sealed class InMemoryTelemetrySource(SyntheticTelemetry? telemetry, DateTimeOffset now) : ITelemetrySource
    {
        public Task<TelemetrySessionAggregates?> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
            Task.FromResult(telemetry is null ? null : new TelemetrySessionAggregates(applicationId, telemetry.Fields, telemetry.Page, now));

        public IReadOnlyList<ObservedSignal> Evaluate(TelemetrySessionAggregates session, IntegrityContext context) =>
            TelemetryRules.Evaluate(session, context);

        public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.FromResult(0);
    }

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

/// <summary>The content analyzers' records, in memory: what the worker's content stages would have stored.</summary>
internal sealed class InMemoryContentRecords : IContentRecords
{
    public List<ApplicationDocument> Documents { get; } = [];

    public List<ConsistencyCheck> Checks { get; } = [];

    public List<AnswerFingerprint> Fingerprints { get; } = [];

    public List<EarlierApplication> Earlier { get; } = [];

    public Task<IReadOnlyList<ApplicationDocument>> GetDocumentsAsync(Guid applicationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApplicationDocument>>([.. Documents.Where(d => d.ApplicationId == applicationId)]);

    public Task<ConsistencyCheck?> GetConsistencyCheckAsync(Guid applicationId, string promptVersion, CancellationToken cancellationToken) =>
        Task.FromResult(Checks.SingleOrDefault(c => c.ApplicationId == applicationId && c.PromptVersion == promptVersion));

    public Task<IReadOnlyList<AnswerFingerprint>> GetOtherCandidatesFingerprintsAsync(IntegrityContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AnswerFingerprint>>([.. Fingerprints.Where(f => f.CandidateId != context.Candidate.Id).OrderByDescending(f => f.CreatedAt)]);

    public Task<IReadOnlyList<EarlierApplication>> GetApplicationsAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EarlierApplication>>([.. Earlier.Where(e => applicationIds.Contains(e.ApplicationId))]);

    public Task KeepFingerprintsAsync(IReadOnlyList<AnswerFingerprint> fingerprints, CancellationToken cancellationToken)
    {
        Fingerprints.AddRange(fingerprints.Where(f => !Fingerprints.Any(k => k.ApplicationId == f.ApplicationId && k.QuestionId == f.QuestionId)));
        return Task.CompletedTask;
    }
}

internal sealed record HarnessResult(
    IReadOnlyList<IntegritySignal> Signals, IReadOnlyList<RulesetResult> Assessments, IReadOnlyDictionary<string, string?> Notes)
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
