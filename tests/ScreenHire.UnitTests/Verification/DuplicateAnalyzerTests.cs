using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Verification;

namespace ScreenHire.UnitTests.Verification;

/// <summary>
/// The within-organization duplicate check over a directory it is handed: how strong the signal is for each kind of shared
/// detail, and which other applications the pipeline is told to re-check. The database directory's queries are covered by
/// the integration tests, because they need Postgres.
/// </summary>
public sealed class DuplicateAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_shared_detail_reports_nothing_and_asks_for_no_rechecks()
    {
        var result = await Analyzer().AnalyzeAsync(Context(), Ct);

        result.Ready.Should().BeTrue();
        result.Signals.Should().BeEmpty();
        result.RelatedApplicationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task The_related_applications_are_every_match_across_kinds_once()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var matches = new[]
        {
            new SharedIdentifierMatch(SharedIdentifier.Email, [a, b]),
            new SharedIdentifierMatch(SharedIdentifier.Device, [b, c]),
            new SharedIdentifierMatch(SharedIdentifier.ResumeFile, [a]),
        };

        var result = await Analyzer(matches).AnalyzeAsync(Context(), Ct);

        result.RelatedApplicationIds.Should().Equal(a, b, c);
        result.Signals.Should().ContainSingle().Which.Values!["others"].Should().Be("3 other applications");
    }

    [Theory]
    [InlineData(SharedIdentifier.Device, SignalConfidence.High, SignalWeight.High)]
    [InlineData(SharedIdentifier.ResumeFile, SignalConfidence.High, SignalWeight.High)]
    [InlineData(SharedIdentifier.Email, SignalConfidence.Medium, SignalWeight.Medium)]
    [InlineData(SharedIdentifier.Phone, SignalConfidence.Medium, SignalWeight.Medium)]
    public async Task A_device_or_file_match_is_strong_and_an_email_or_phone_alone_is_medium(
        SharedIdentifier kind, SignalConfidence confidence, SignalWeight weight)
    {
        var result = await Analyzer(new SharedIdentifierMatch(kind, [Guid.CreateVersion7()])).AnalyzeAsync(Context(), Ct);

        var signal = result.Signals.Should().ContainSingle().Subject;
        signal.Code.Should().Be(SignalCatalogue.DuplicateInOrganization);
        signal.Confidence.Should().Be(confidence);
        signal.Weight.Should().Be(weight);
        signal.Values!["others"].Should().Be("1 other application");
    }

    [Fact]
    public async Task Email_and_phone_together_stay_medium_and_one_strong_match_lifts_the_signal()
    {
        var other = Guid.CreateVersion7();
        var weak = await Analyzer(new SharedIdentifierMatch(SharedIdentifier.Email, [other]), new SharedIdentifierMatch(SharedIdentifier.Phone, [other]))
            .AnalyzeAsync(Context(), Ct);
        var strong = await Analyzer(
                new SharedIdentifierMatch(SharedIdentifier.Email, [other]),
                new SharedIdentifierMatch(SharedIdentifier.Phone, [other]),
                new SharedIdentifierMatch(SharedIdentifier.Device, [other]))
            .AnalyzeAsync(Context(), Ct);

        weak.Signals.Single().Weight.Should().Be(SignalWeight.Medium);
        weak.Signals.Single().Values!["shared"].Should().Be("an email address and a phone number");
        weak.Signals.Single().Values!["others"].Should().Be("1 other application");
        strong.Signals.Single().Weight.Should().Be(SignalWeight.High);
        strong.Signals.Single().Values!["shared"].Should().Be("an email address, a phone number and a device");
        strong.RelatedApplicationIds.Should().Equal(other);
    }

    private static DuplicateAnalyzer Analyzer(params SharedIdentifierMatch[] matches) => new(new StubDirectory(matches));

    private static VerificationContext Context()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Engineer", Slug = "engineer" };
        var candidate = Candidate.Create(organization, "Casey", "Morgan", "casey@example.com", null, Now);
        var (application, _) = JobApplication.Submit(
            job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [new ApplicationAnswer("a", "yes")], "2026-10-20.2", false, BotCheckResult.NotChecked));
        return new VerificationContext(application, candidate, job, null, [], Now);
    }

    private sealed class StubDirectory(IReadOnlyList<SharedIdentifierMatch> matches) : IApplicantDirectory
    {
        public Task<IReadOnlyList<SharedIdentifierMatch>> FindSharedAsync(VerificationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(matches);
    }
}
