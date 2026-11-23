using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScreenHire.Application.Verification;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// An application's decision record (docs/architecture/data-requests.md, Decision record), which the test stack turns on
/// with <c>ScreenHire:Compliance:DecisionRecord:Enabled</c>: the team's one copy of what the checks found, what people did
/// about it and why, and how the application moved, for answering a candidate's access or review request. It carries no
/// score, stays inside the organization, and never goes to an API key, which reads verification only through the routes
/// that log each read (ADR-0021).
/// </summary>
public sealed class DecisionRecordTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_record_holds_the_verification_the_reviewer_actions_and_the_moves_with_their_reasons()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot, accommodation: true);
        var verification = await VerificationSupport.AssessedAsync(owner, applicationId);
        (await owner.PostAsync(
                $"/v1/applications/{applicationId}/verification/assessments/{verification.Id("authenticity.id")}/dismiss",
                new JsonObject { ["reason"] = "VerifiedOutsideScreenHire", ["note"] = "Checked their ID on a call." }))
            .Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{applicationId}/move", new JsonObject { ["toStageId"] = "screening", ["note"] = "Clear after the call." }))
            .Expect(HttpStatusCode.OK);

        var record = (await owner.GetAsync($"/v1/applications/{applicationId}/decision-record")).Expect(HttpStatusCode.OK);
        var me = (await owner.GetAsync("/v1/auth/me")).Expect(HttpStatusCode.OK);

        me.At("organization.decisionRecordEnabled").GetBoolean().Should().BeTrue("the dashboard offers the download only when the route answers");

        record.Id("applicationId").Should().Be(applicationId);
        record.Id("jobId").Should().Be(jobId);
        record.Text("candidateName").Should().NotBeNullOrEmpty();
        record.At("accommodationRequested").GetBoolean().Should().BeTrue("the candidate's own choice is part of the record");
        record.Text("privacyNoticeVersion").Should().NotBeNullOrEmpty("an application from the careers page records the notice it saw");
        record.Text("currentStage.id").Should().Be("screening");
        record.Text("verification.authenticity.level").Should().Be(verification.Text("authenticity.level"));
        record.At("verification.authenticity.signals.0.evidence").GetArrayLength().Should().BeGreaterThan(0, "the evidence comes with the level");
        record.Text("verification.authenticity.dismissal.reason").Should().Be("VerifiedOutsideScreenHire");
        record.At("verification.reviewLog").EnumerateArray()
            .Should().Contain(e => e.GetProperty("type").GetString() == "Dismissed" && e.GetProperty("note").GetString() == "Checked their ID on a call.");
        record.At("activity").EnumerateArray()
            .Should().Contain(e => e.GetProperty("type").GetString() == "StageChanged" && e.GetProperty("note").GetString() == "Clear after the call.");
        record.At("stages").EnumerateArray().Select(s => s.GetProperty("id").GetString()).Should().Contain(["applied", "screening"]);
        record.At("stages").EnumerateArray().Should().OnlyContain(s => !string.IsNullOrEmpty(s.GetProperty("name").GetString()));
        // A scorecard is the interview form, not a score.
        PropertyNames(record.Body).Should().NotContain(name => Regex.IsMatch(name, "score(?!card)|percent", RegexOptions.IgnoreCase),
            "a decision record states evidence, never a score (ADR-0004)");
    }

    [Fact]
    public async Task Another_organization_finds_no_record_and_no_api_key_reads_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var applicationId = await VerificationSupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.GenuineHuman);
        await VerificationSupport.AssessedAsync(owner, applicationId);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Auditor", ["Read", "VerificationRead"])).Text("token"));
        var path = $"/v1/applications/{applicationId}/decision-record";

        (await other.GetAsync(path)).Status.Should().Be(HttpStatusCode.NotFound, "another organization's application does not exist for it");
        (await key.GetAsync(path)).Status.Should().Be(HttpStatusCode.Unauthorized, "even a key that may read verification gets it only through the logged routes");
        (await owner.GetAsync($"/v1/applications/{applicationId}/verification")).At("reviewLog").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).Should().NotContain("ReadByKey", "the refused key read nothing");
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
