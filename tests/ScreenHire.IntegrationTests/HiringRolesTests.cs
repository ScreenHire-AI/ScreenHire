using System.Text.Json.Nodes;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Roles on the hiring routes (<c>RequireHiringRole</c>): a Viewer of an organization sees its jobs, templates and
/// applications but changes none of them, and gets 403 with a problem body on every write.
/// </summary>
public sealed class HiringRolesTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_viewer_reads_hiring_data_but_every_write_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        var (templateId, _) = await owner.CreatePublishedTemplateAsync("Data roles");
        using var viewer = await ViewerOfAsync(owner.OrganizationId, cancellationToken);

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
            await viewer.PostAsync("/v1/templates", new JsonObject { ["name"] = "Mine" }),
            await viewer.PostAsync($"/v1/templates/{templateId}/versions"),
            await viewer.PutAsync($"/v1/templates/{templateId}", new JsonObject { ["name"] = "Renamed" }),
            await viewer.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = Samples.Definition() }),
            await viewer.PostAsync($"/v1/templates/{templateId}/versions/1/publish"),
        ];

        writes.Select(w => w.Status).Should().AllBeEquivalentTo(HttpStatusCode.Forbidden);
        writes[0].Text("title").Should().Be("Read-only role");
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

    // Nobody can join with another role through the API yet, so the membership is written as the application role with
    // the organization as tenant (TestData's pattern), for a user who signed in but never onboarded.
    private async Task<Tenant> ViewerOfAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        try
        {
            await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("viewer"), cancellationToken);
            var userId = (await session.CurrentUserAsync(cancellationToken))!.Value.GetProperty("id").GetGuid();
            var tenant = new TenantContext();
            tenant.SetOrganization(organizationId);
            await using (var db = await fixture.CreateDbContextAsync(tenant, cancellationToken))
            {
                db.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = organizationId,
                    UserId = userId,
                    Role = OrganizationRole.Viewer,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return new Tenant(session, organizationId, userId);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}
