using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Roles on the hiring routes (<c>RequireHiringRole</c>) and organization settings and webhooks (<c>RequireOrganizationAdmin</c>): a
/// Viewer of an organization sees its jobs, templates and applications but changes none of them, and gets 403 with a
/// problem body on every write.
/// </summary>
public sealed class HiringRolesTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_viewer_reads_hiring_data_but_every_write_is_forbidden()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var (templateId, _) = await owner.CreatePublishedTemplateAsync("Data roles");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        (await viewer.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        (await viewer.GetAsync($"/v1/applications/{application.Id()}")).Expect(HttpStatusCode.OK);
        (await viewer.GetAsync($"/v1/templates/{templateId}")).Expect(HttpStatusCode.OK);

        ApiResult[] writes =
        [
            await viewer.PostAsync("/v1/jobs", Samples.RemoteJob("Sneaky")),
            await viewer.PutAsync($"/v1/jobs/{job.Id()}", Samples.RemoteJob("Renamed")),
            await viewer.PostAsync($"/v1/jobs/{job.Id()}/publish"),
            await viewer.PostAsync($"/v1/jobs/{job.Id()}/close"),
            await viewer.PostAsync($"/v1/jobs/{job.Id()}/applications", new JsonObject
            {
                ["candidate"] = new JsonObject { ["fullName"] = "Robin Lake", ["email"] = ApiSession.NewEmail("other") },
            }),
            await viewer.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" }),
            await viewer.PostAsync($"/v1/applications/{application.Id()}/scorecards", new JsonObject
            {
                ["stageId"] = "applied", ["ratings"] = new JsonArray(), ["recommendation"] = "Yes",
            }),
            await viewer.PostAsync("/v1/templates", new JsonObject { ["name"] = "Mine" }),
            await viewer.PostAsync($"/v1/templates/{templateId}/versions"),
            await viewer.PutAsync($"/v1/templates/{templateId}", new JsonObject { ["name"] = "Renamed" }),
            await viewer.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = Samples.Definition() }),
            await viewer.PostAsync($"/v1/templates/{templateId}/versions/1/publish"),
            await viewer.PostAsync("/v1/webhooks", new JsonObject
            {
                ["url"] = "https://hooks.example.com/in",
                ["events"] = new JsonArray("job.published"),
            }),
            await viewer.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#000000" }),
        ];

        writes.Select(w => w.Status).Should().AllBeEquivalentTo(HttpStatusCode.Forbidden);
        writes[0].Text("title").Should().Be("Read-only role");
        writes[^1].Text("title").Should().Be("Owners and admins only");
        var after = (await owner.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        after.Text("title").Should().Be("Data Engineer");
        after.Text("status").Should().Be("Open");
        (await owner.GetAsync($"/v1/applications/{application.Id()}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task A_user_without_an_organization_cannot_create_hiring_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("loner"), cancellationToken);

        using var response = await session.PostAsync("/v1/jobs", Samples.RemoteJob("Nowhere"), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
