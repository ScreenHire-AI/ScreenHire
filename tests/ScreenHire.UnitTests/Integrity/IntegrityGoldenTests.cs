using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Integrity;

namespace ScreenHire.UnitTests.Integrity;

/// <summary>
/// Golden files: the assessments of every synthetic applicant (<see cref="SyntheticApplicants"/>), submitted in order to
/// one job, compared with <c>tests/ScreenHire.UnitTests/Integrity/Golden/{key}.json</c>. A change to an analyzer, the
/// catalogue's wording or the ruleset shows up as a diff a reviewer can read. Rewrite the files with
/// <c>UPDATE_INTEGRITY_GOLDEN=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*IntegrityGoldenTests"</c>
/// and review the diff before committing it. Ids and fingerprints are left out: they are random per run.
/// </summary>
public sealed class IntegrityGoldenTests
{
    private const string UpdateVariable = "UPDATE_INTEGRITY_GOLDEN";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Readable apostrophes in the files; they are compared as text, never served.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Synthetic_applicants_match_their_golden_files()
    {
        var harness = new IntegrityHarness();
        var mismatches = new List<string>();
        foreach (var applicant in SyntheticApplicants.All)
        {
            var result = await harness.SubmitAsync(applicant);
            var actual = JsonSerializer.Serialize(Golden(applicant, result), Json).ReplaceLineEndings("\n") + "\n";
            var path = GoldenPath(applicant.Key);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            }

            if (!File.Exists(path) || (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") != actual)
            {
                mismatches.Add(applicant.Key);
            }
        }

        mismatches.Should().BeEmpty($"each golden file must match; rerun with {UpdateVariable}=1 to rewrite them, then review the diff");
    }

    [Fact]
    public async Task The_synthetic_applicants_land_where_they_are_meant_to()
    {
        var harness = new IntegrityHarness();
        var levels = new Dictionary<string, (string Authenticity, string Ai)>();
        foreach (var applicant in SyntheticApplicants.All)
        {
            var result = await harness.SubmitAsync(applicant);
            levels[applicant.Key] = (result.Authenticity.Level, result.AiAssistance.Level);
        }

        levels["genuine-human"].Should().Be((nameof(AuthenticityLevel.NoConcerns), nameof(AiAssistanceLevel.NotIndicated)));
        levels["honest-ai-assisted"].Should().Be((nameof(AuthenticityLevel.NoConcerns), nameof(AiAssistanceLevel.NotIndicated)));
        levels["bot"].Authenticity.Should().Be(nameof(AuthenticityLevel.VerificationRecommended));
        levels["application-farm-1"].Authenticity.Should().NotBe(nameof(AuthenticityLevel.NoConcerns));
        levels["application-farm-3"].Authenticity.Should().Be(nameof(AuthenticityLevel.VerificationRecommended));
    }

    [Fact]
    public async Task Later_farm_members_carry_the_duplicate_signal()
    {
        var harness = new IntegrityHarness();
        HarnessResult? last = null;
        foreach (var applicant in SyntheticApplicants.ApplicationFarm)
        {
            last = await harness.SubmitAsync(applicant);
        }

        var duplicate = last!.Signals.Single(s => s.Code == SignalCatalogue.DuplicateInOrganization);
        duplicate.Weight.Should().Be(SignalWeight.High, "a shared device and file are strong");
        duplicate.Explanation.Should().Be(
            "This application and 2 other applications to your organization came from different candidates but share an email address, a phone number, a device and an identical resume file.");
        duplicate.Evidence.Select(e => e.Value).Should().AllBe("2 other applications");
    }

    [Fact]
    public async Task An_accommodation_leaves_no_timing_signal_for_a_fast_form()
    {
        var harness = new IntegrityHarness();

        var result = await harness.SubmitAsync(SyntheticApplicants.ApplicationFarm[0], accommodation: true);

        result.Signals.Should().NotContain(s => s.Code == SignalCatalogue.SubmitVelocity);
        result.Signals.Should().NotContain(s => s.AccessibilitySensitive);
    }

    private static object Golden(SyntheticApplicant applicant, HarnessResult result) => new
    {
        applicant = applicant.Key,
        rulesetVersion = IntegrityRuleset.Version,
        authenticity = View(result.Authenticity, result.Signals),
        aiAssistance = View(result.AiAssistance, result.Signals),
    };

    private static object View(RulesetResult assessment, IReadOnlyList<IntegritySignal> signals) => new
    {
        level = assessment.Level,
        reasonCodes = assessment.ReasonCodes,
        appliedRules = assessment.AppliedRules,
        summary = assessment.Summary,
        nextStep = assessment.NextStep,
        policyContext = assessment.AiPolicyContext,
        signals = assessment.SignalIds.Select(id => signals.Single(s => s.Id == id)).Select(s => new
        {
            code = s.Code,
            category = s.Category,
            weight = s.Weight,
            confidence = s.Confidence,
            countsTowardLevel = assessment.CountedSignalIds.Contains(s.Id),
            contextOnly = s.ContextOnly,
            informational = s.Informational,
            accessibilitySensitive = s.AccessibilitySensitive,
            source = $"{s.Source}@{s.SourceVersion}",
            explanation = s.Explanation,
            evidence = s.Evidence,
            counterEvidence = s.CounterEvidence,
            llm = s.Llm,
        }),
    };

    private static string GoldenPath(string key) =>
        RepositoryFiles.PathTo("tests", "ScreenHire.UnitTests", "Integrity", "Golden", $"{key}.json");
}
