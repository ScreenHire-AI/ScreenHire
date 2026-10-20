using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Cross-tenant denial for the hiring endpoints. Organization A, signed in, reaches nothing of organization B's: every
/// direct request for B's job, template, application or candidate is a 404 (never a 403, which would confirm the row
/// exists), B's rows stay as they were, and A's lists never include them. System templates are shared on purpose.
/// Row-level security behind the same rule is in <see cref="HiringRowLevelSecurityTests"/>.
/// </summary>
public sealed class HiringTenantIsolationTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Another_organizations_job_cannot_be_read_changed_published_or_closed()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var (a, b) = (scene.A, scene.B);
        var before = await b.GetAsync($"/v1/jobs/{scene.BJobId}");

        var responses = new Dictionary<string, ApiResult>
        {
            ["get"] = await a.GetAsync($"/v1/jobs/{scene.BJobId}"),
            ["put"] = await a.PutAsync($"/v1/jobs/{scene.BJobId}", Samples.RemoteJob("Taken over")),
            ["publish"] = await a.PostAsync($"/v1/jobs/{scene.BJobId}/publish"),
            ["close"] = await a.PostAsync($"/v1/jobs/{scene.BJobId}/close"),
            ["versions"] = await a.GetAsync($"/v1/jobs/{scene.BJobId}/versions"),
            ["version 1"] = await a.GetAsync($"/v1/jobs/{scene.BJobId}/versions/1"),
            ["template diff"] = await a.GetAsync($"/v1/jobs/{scene.BJobId}/workflow/template-diff"),
            ["applications"] = await a.GetAsync($"/v1/jobs/{scene.BJobId}/applications"),
        };

        responses.Should().OnlyContain(r => r.Value.Status == HttpStatusCode.NotFound, "another tenant's row looks exactly like a missing one");
        var after = await b.GetAsync($"/v1/jobs/{scene.BJobId}");
        after.Expect(HttpStatusCode.OK).Body.GetRawText().Should().Be(before.Body.GetRawText(), "nothing of B's job changed");
        after.Text("status").Should().Be("Open");
        (await b.GetAsync($"/v1/jobs/{scene.BJobId}/versions")).Count().Should().Be(1);
    }

    [Fact]
    public async Task Another_organizations_template_cannot_be_read_or_changed()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var (a, b) = (scene.A, scene.B);
        var before = await b.GetAsync($"/v1/templates/{scene.BTemplateId}");
        var edit = new JsonObject { ["definition"] = Samples.Definition() };

        var responses = new Dictionary<string, ApiResult>
        {
            ["get"] = await a.GetAsync($"/v1/templates/{scene.BTemplateId}"),
            ["rename"] = await a.PutAsync($"/v1/templates/{scene.BTemplateId}", new JsonObject { ["name"] = "Taken over" }),
            ["new version"] = await a.PostAsync($"/v1/templates/{scene.BTemplateId}/versions"),
            ["get version"] = await a.GetAsync($"/v1/templates/{scene.BTemplateId}/versions/1"),
            ["edit version"] = await a.PutAsync($"/v1/templates/{scene.BTemplateId}/versions/1", edit),
            ["publish version"] = await a.PostAsync($"/v1/templates/{scene.BTemplateId}/versions/1/publish"),
        };

        responses.Should().OnlyContain(r => r.Value.Status == HttpStatusCode.NotFound, "another tenant's row looks exactly like a missing one");
        var after = await b.GetAsync($"/v1/templates/{scene.BTemplateId}");
        after.Expect(HttpStatusCode.OK).Body.GetRawText().Should().Be(before.Body.GetRawText(), "nothing of B's template changed");
        after.Text("name").Should().Be("B pipeline");
        after.Count("versions").Should().Be(1);
    }

    [Fact]
    public async Task Another_organizations_application_timeline_and_candidate_cannot_be_read_or_moved()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var (a, b) = (scene.A, scene.B);
        var timelineBefore = await b.GetAsync($"/v1/applications/{scene.BApplicationId}/timeline");

        var responses = new Dictionary<string, ApiResult>
        {
            ["application"] = await a.GetAsync($"/v1/applications/{scene.BApplicationId}"),
            ["timeline"] = await a.GetAsync($"/v1/applications/{scene.BApplicationId}/timeline"),
            ["move"] = await a.PostAsync($"/v1/applications/{scene.BApplicationId}/move", new JsonObject { ["toStageId"] = "screening" }),
            ["candidate"] = await a.GetAsync($"/v1/candidates/{scene.BCandidateId}"),
        };

        responses.Should().OnlyContain(r => r.Value.Status == HttpStatusCode.NotFound, "another tenant's row looks exactly like a missing one");
        (await b.GetAsync($"/v1/applications/{scene.BApplicationId}")).Text("stage.id").Should().Be("applied");
        var timelineAfter = await b.GetAsync($"/v1/applications/{scene.BApplicationId}/timeline");
        timelineAfter.Body.GetRawText().Should().Be(timelineBefore.Body.GetRawText(), "the refused move left no timeline entry");
        (await b.GetAsync($"/v1/candidates/{scene.BCandidateId}")).Expect(HttpStatusCode.OK).Count("applications").Should().Be(1);
    }

    [Fact]
    public async Task Another_organizations_template_version_cannot_start_a_job_or_be_copied()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var a = scene.A;

        var job = await a.PostAsync("/v1/jobs", new JsonObject { ["title"] = "Borrowed", ["templateVersionId"] = scene.BTemplateVersionId });
        var copy = await a.PostAsync("/v1/templates", new JsonObject { ["name"] = "Borrowed copy", ["copyFromVersionId"] = scene.BTemplateVersionId });

        job.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("templateVersionId");
        copy.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("copyFromVersionId");
        (await a.GetAsync("/v1/jobs")).Count().Should().Be(0);
        (await a.GetAsync("/v1/templates")).Count().Should().Be(2, "only the system templates");
    }

    [Fact]
    public async Task An_application_cannot_be_added_to_another_organizations_job()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var (a, b) = (scene.A, scene.B);

        var result = await a.PostAsync(
            $"/v1/jobs/{scene.BJobId}/applications",
            new JsonObject { ["candidate"] = new JsonObject { ["fullName"] = "Riley Patel", ["email"] = "riley.patel@example.com" } });

        result.Status.Should().Be(HttpStatusCode.NotFound);
        (await b.GetAsync($"/v1/jobs/{scene.BJobId}/applications")).Count().Should().Be(1, "B still has only its own application");
        (await b.GetAsync("/v1/candidates")).Count().Should().Be(1, "no candidate was created in B");
        (await a.GetAsync("/v1/candidates")).Count().Should().Be(0, "none was created in A either");
    }

    [Fact]
    public async Task Lists_show_only_the_signed_in_organizations_rows_plus_the_shared_system_templates()
    {
        using var scene = await Scene.CreateAsync(fixture);
        var (a, b) = (scene.A, scene.B);
        var aJob = await a.CreateJobAsync(Samples.RemoteJob("A job"));
        var aTemplate = await a.CreateTemplateAsync("A pipeline");
        await a.CreateApplicationAsync(aJob.Id(), "a.candidate@example.com", "Jordan Lee");

        var jobsA = await a.GetAsync("/v1/jobs");
        var jobsB = await b.GetAsync("/v1/jobs");
        var templatesA = await a.GetAsync("/v1/templates");
        var templatesB = await b.GetAsync("/v1/templates");
        var candidatesA = await a.GetAsync("/v1/candidates");
        var candidatesB = await b.GetAsync("/v1/candidates");

        Ids(jobsA).Should().Equal(aJob.Id());
        Ids(jobsB).Should().Equal(scene.BJobId);
        Ids(templatesA).Should().BeEquivalentTo([SystemTemplates.SoftwareEngineer, SystemTemplates.HourlyRole, aTemplate.Id()]);
        Ids(templatesB).Should().BeEquivalentTo([SystemTemplates.SoftwareEngineer, SystemTemplates.HourlyRole, scene.BTemplateId]);
        candidatesA.Count().Should().Be(1);
        candidatesA.Text("0.fullName").Should().Be("Jordan Lee");
        Ids(candidatesB).Should().Equal(scene.BCandidateId);
    }

    private static List<Guid> Ids(ApiResult list) =>
        [.. list.Expect(HttpStatusCode.OK).Body.EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

    /// <summary>Organization A with nothing, and organization B with a published job, a published template and one application.</summary>
    private sealed class Scene : IDisposable
    {
        public required Tenant A { get; init; }

        public required Tenant B { get; init; }

        public Guid BJobId { get; init; }

        public Guid BTemplateId { get; init; }

        public Guid BTemplateVersionId { get; init; }

        public Guid BApplicationId { get; init; }

        public Guid BCandidateId { get; init; }

        public static async Task<Scene> CreateAsync(AppHostFixture fixture)
        {
            var a = await fixture.SignedInWithOrganizationAsync();
            var b = await fixture.SignedInWithOrganizationAsync();
            var (templateId, versionId) = await b.CreatePublishedTemplateAsync("B pipeline");
            var job = await b.CreateJobAsync(new JsonObject
            {
                ["title"] = "B engineer",
                ["description"] = "<p>Work at B.</p>",
                ["employmentType"] = "FullTime",
                ["workplaceType"] = "Remote",
                ["applicantLocationRequirements"] = new JsonArray("GB"),
                ["templateVersionId"] = versionId,
            });
            await b.PublishJobAsync(job.Id());
            var application = await b.CreateApplicationAsync(job.Id(), "b.candidate@example.com", "Sam Taylor");
            return new Scene
            {
                A = a,
                B = b,
                BJobId = job.Id(),
                BTemplateId = templateId,
                BTemplateVersionId = versionId,
                BApplicationId = application.Id(),
                BCandidateId = application.Id("candidate.id"),
            };
        }

        public void Dispose()
        {
            A.Dispose();
            B.Dispose();
        }
    }
}
