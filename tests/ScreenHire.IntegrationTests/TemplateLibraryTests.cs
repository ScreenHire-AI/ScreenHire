using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The Templates page's API (design system section 22): archiving an organization's own hiring, scorecard and email
/// templates, which leaves the pickers and reads only until restored, never a built-in; and <c>/v1/template-usage</c>,
/// where each template is used and when it was last used, counted in the organization's own data only.
/// </summary>
public sealed class TemplateLibraryTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_archived_hiring_template_reads_only_starts_no_job_and_comes_back_when_restored()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (templateId, versionId) = await owner.CreatePublishedTemplateAsync("Summer interns");
        var url = $"/v1/templates/{templateId}";

        (await other.PostAsync($"{url}/archive")).Expect(HttpStatusCode.NotFound);
        var archived = (await owner.PostAsync($"{url}/archive")).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"{url}/archive")).Expect(HttpStatusCode.OK);

        archived.At("archived").GetBoolean().Should().BeTrue();
        (await owner.GetAsync("/v1/templates")).Body.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == templateId)
            .GetProperty("archived").GetBoolean().Should().BeTrue("the list keeps archived templates, marked, for screens that name them");
        (await owner.PutAsync(url, new JsonObject { ["name"] = "Winter interns" })).Expect(HttpStatusCode.Conflict).Text("detail").Should().Contain("archived");
        (await owner.PostAsync($"{url}/versions", new JsonObject())).Expect(HttpStatusCode.Conflict);
        var job = await owner.PostAsync("/v1/jobs", WithTemplate(Samples.RemoteJob("Intern"), versionId));
        job.Expect(HttpStatusCode.BadRequest).ProblemMessage("templateVersionId").Should().Contain("archived");

        (await other.PostAsync($"{url}/unarchive")).Expect(HttpStatusCode.NotFound);
        var restored = (await owner.PostAsync($"{url}/unarchive")).Expect(HttpStatusCode.OK);
        restored.At("archived").GetBoolean().Should().BeFalse();
        await owner.CreateJobAsync(WithTemplate(Samples.RemoteJob("Intern"), versionId));
        var history = (await owner.GetAsync($"{url}/history")).Expect(HttpStatusCode.OK);
        history.Text("entries.0.lines.0.text").Should().Be("restored the template from the archive");
        history.Text("entries.1.lines.0.text").Should().Be("archived the template", "archiving twice recorded one change");
        restored.Text("lastUpdated.by").Should().Be("Member");
    }

    [Fact]
    public async Task Built_ins_cannot_be_archived()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var system = (await owner.GetAsync("/v1/templates")).Body.EnumerateArray().First(t => t.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();

        (await owner.PostAsync($"/v1/templates/{system}/archive")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync("/v1/scorecard-templates/interview/archive")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync("/v1/email-templates/stage-change/archive")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/v1/scorecard-templates/{Guid.NewGuid():D}/archive")).Expect(HttpStatusCode.NotFound);
        (await owner.PostAsync($"/v1/email-templates/{Guid.NewGuid():D}/archive")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_own_scorecard_and_an_own_email_template_are_archived_read_only_and_restored()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var scorecard = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));
        var email = await CreateEmailAsync(owner, "Spring open day");

        (await owner.PostAsync($"/v1/scorecard-templates/{scorecard}/archive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeTrue();
        (await owner.PostAsync($"/v1/email-templates/{email}/archive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeTrue();
        (await other.PostAsync($"/v1/scorecard-templates/{scorecard}/unarchive")).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/email-templates/{email}/unarchive")).Expect(HttpStatusCode.NotFound);

        (await owner.GetAsync("/v1/scorecard-templates")).Body.EnumerateArray().Single(s => s.GetProperty("id").GetString() == scorecard)
            .GetProperty("archived").GetBoolean().Should().BeTrue();
        (await owner.GetAsync("/v1/email-templates")).Body.GetProperty("templates").EnumerateArray().Single(t => t.GetProperty("id").GetString() == email)
            .GetProperty("archived").GetBoolean().Should().BeTrue();
        (await owner.PutAsync($"/v1/scorecard-templates/{scorecard}", new JsonObject { ["name"] = "Renamed", ["questions"] = Questions(Rating5("depth")) }))
            .Expect(HttpStatusCode.Conflict);
        (await owner.PutAsync($"/v1/email-templates/{email}", Wording("Changed", "Hi"))).Expect(HttpStatusCode.Conflict);

        (await owner.PostAsync($"/v1/scorecard-templates/{scorecard}/unarchive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeFalse();
        (await owner.PostAsync($"/v1/email-templates/{email}/unarchive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeFalse();
        (await owner.GetAsync($"/v1/scorecard-templates/{scorecard}/history")).Text("entries.1.lines.0.text").Should().Be("archived the scorecard");
        (await owner.GetAsync($"/v1/email-templates/{email}/history")).Text("entries.0.lines.0.text").Should().Be("restored the template from the archive");
    }

    [Fact]
    public async Task Usage_counts_the_jobs_templates_and_automations_that_use_each_template_and_when_it_was_last_used()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var scorecard = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));
        var invitation = await CreateEmailAsync(owner, "Interview invitation");
        var unused = await CreateEmailAsync(owner, "Spring open day");
        var automation = SendEmail("invite", "Invite to interview", invitation);
        var (templateId, versionId) = await owner.CreatePublishedTemplateAsync("Platform engineering", DefinitionNaming("interview", scorecard, (JsonObject)automation.DeepClone()));
        var (archivedId, _) = await owner.CreatePublishedTemplateAsync("Old platform", DefinitionNaming("interview", scorecard, (JsonObject)automation.DeepClone()));
        (await owner.PostAsync($"/v1/templates/{archivedId}/archive")).Expect(HttpStatusCode.OK);
        var job = (await JobFromVersionAsync(owner, "Platform Engineer", versionId)).Id();
        var closed = (await JobFromVersionAsync(owner, "Platform Engineer (closed)", versionId)).Id();
        await owner.PublishJobAsync(closed);
        await owner.CloseJobAsync(closed);

        // An email sent from the invitation makes its Last sent.
        var address = ApiSession.NewEmail("usage");
        var application = (await owner.CreateApplicationAsync(job, address)).Id();
        (await owner.PostAsync("/v1/emails", new JsonObject
        {
            ["templateId"] = invitation,
            ["applicationIds"] = new JsonArray(JsonValue.Create(application)),
        })).Expect(HttpStatusCode.Accepted);
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
                .Any(e => e.GetProperty("type").GetString() == "EmailSent"),
            "the email's timeline entry",
            Ct);

        var usage = (await owner.GetAsync("/v1/template-usage")).Expect(HttpStatusCode.OK);

        var hiring = Entry(usage, "hiring", templateId.ToString("D"));
        Items(hiring).Should().Equal([("Job", job, "Platform Engineer", (string?)null)], "a closed job no longer uses the template");
        hiring.GetProperty("lastUsedAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddHours(-1));
        hiring.GetProperty("notUsedRecently").GetBoolean().Should().BeFalse();
        Items(Entry(usage, "hiring", archivedId.ToString("D"))).Should().BeEmpty();

        var library = Entry(usage, "scorecards", scorecard);
        Items(library).Should().Equal([("HiringTemplate", templateId, "Platform engineering", (string?)null)], "an archived template does not count");
        library.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.String, "jobs took the scorecard");

        var email = Entry(usage, "emails", invitation);
        Items(email).Should().BeEquivalentTo([
            ("JobAutomation", job, "Platform Engineer", (string?)"Invite to interview"),
            ("TemplateAutomation", templateId, "Platform engineering", (string?)"Invite to interview"),
        ]);
        email.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.String, "an email went out from it");
        var spare = Entry(usage, "emails", unused);
        Items(spare).Should().BeEmpty();
        spare.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.Null);
        spare.GetProperty("notUsedRecently").GetBoolean().Should().BeFalse("the team made it today, so it has not had the chance");
        Entry(usage, "emails", "stage-change").GetProperty("notUsedRecently").GetBoolean().Should().BeFalse("the organization is new");

        // Another organization sees its own templates only, and none of our uses.
        var theirs = (await other.GetAsync("/v1/template-usage")).Expect(HttpStatusCode.OK);
        theirs.Body.GetProperty("emails").EnumerateArray().Select(e => e.GetProperty("templateId").GetString()).Should().NotContain([invitation, unused]);
        theirs.Body.GetProperty("scorecards").EnumerateArray().Should().OnlyContain(s => s.GetProperty("usedBy").GetArrayLength() == 0 || IsSystemUse(s));
        theirs.Body.GetProperty("emails").EnumerateArray().Should().OnlyContain(e => e.GetProperty("lastUsedAt").ValueKind == JsonValueKind.Null);
    }

    private static bool IsSystemUse(JsonElement entry) =>
        entry.GetProperty("usedBy").EnumerateArray().All(i => i.GetProperty("kind").GetString() == "HiringTemplate");

    private static JsonElement Entry(ApiResult usage, string kind, string templateId) =>
        usage.Body.GetProperty(kind).EnumerateArray().Single(e => e.GetProperty("templateId").GetString() == templateId);

    private static List<(string Kind, Guid Id, string Name, string? Automation)> Items(JsonElement entry) =>
    [
        .. entry.GetProperty("usedBy").EnumerateArray().Select(i => (
            i.GetProperty("kind").GetString()!,
            i.GetProperty("id").GetGuid(),
            i.GetProperty("name").GetString()!,
            i.TryGetProperty("automationName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)),
    ];

    private static JsonObject WithTemplate(JsonObject job, Guid versionId)
    {
        job["templateVersionId"] = versionId;
        return job;
    }

    private static JsonObject SendEmail(string id, string name, string template) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "interview" },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = template }),
    };

    private static JsonObject Wording(string subject, string body) => new() { ["subject"] = subject, ["body"] = body };

    private static async Task<string> CreateEmailAsync(Tenant owner, string name) =>
        (await owner.PostAsync("/v1/email-templates", new JsonObject
        {
            ["name"] = name, ["subject"] = $"{name} at {{{{ organizationName }}}}", ["body"] = "Hi {{ firstName }}",
        })).Expect(HttpStatusCode.Created).Text("id");
}
