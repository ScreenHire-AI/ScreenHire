using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// The talent pool's edges (docs/architecture/talent-pool.md): one organization never sees or acts on another's pool or
/// its period, and the pool is for people only, so an organization API key reaches none of its routes. Each test makes
/// its own organizations.
/// </summary>
public sealed class TalentPoolBoundaryTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Another_organization_sees_none_of_the_pool_and_cannot_act_on_its_people_or_its_period()
    {
        using var a = await PoolWorld.CreateAsync(fixture);
        using var b = await PoolWorld.CreateAsync(fixture);
        var (aJob, _) = await a.OpenJobAsync("Warehouse Lead");
        var (bJob, _) = await b.OpenJobAsync("Night Supervisor");
        var kept = await a.KeptPersonAsync(aJob, "across");

        (await b.PoolAsync()).Should().BeEmpty("a pool belongs to its organization");
        (await b.Owner.PostAsync($"/v1/talent-pool/{kept.CandidateId}/remove", new JsonObject())).Expect(HttpStatusCode.NotFound);
        var invited = (await b.InviteAsync([kept.CandidateId], bJob)).Expect(HttpStatusCode.Accepted);
        invited.At("queued").GetInt32().Should().Be(0);
        invited.At("skipped").GetInt32().Should().Be(1);
        var added = (await b.Owner.PostAsync("/v1/talent-pool/applications", new JsonObject
        {
            ["candidateIds"] = PoolWorld.Ids([kept.CandidateId]),
            ["jobId"] = bJob,
        })).Expect(HttpStatusCode.OK);
        added.Count("applicationIds").Should().Be(0);
        (await b.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 6 })).Expect(HttpStatusCode.OK);

        (await a.PoolCandidateIdsAsync()).Should().Equal(kept.CandidateId);
        (await a.Owner.GetAsync("/v1/organization/talent-pool-settings")).Expect(HttpStatusCode.OK)
            .At("permissionMonths").GetInt32().Should().Be(12, "another organization's period is its own");
    }

    [Fact]
    public async Task An_api_key_reaches_none_of_the_pools_routes()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Dispatch Planner");
        var application = await world.ApplyAsync(job, ApiSession.NewEmail("key"), keep: true);
        using var key = new KeyClient(fixture, (await world.Owner.CreateKeyAsync("ATS sync", ["Read", "Write"])).Text("token"));

        var reads = await key.GetAsync("/v1/talent-pool");
        var keeps = await key.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = PoolWorld.Ids([application]) });
        var settings = await key.GetAsync("/v1/organization/talent-pool-settings");

        foreach (var result in new[] { reads, keeps, settings })
        {
            result.Status.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        (await world.DetailAsync(application)).At("keptInMindAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null,
            "keeping someone in mind is a person's decision, never an integration's (ADR-0004)");
    }
}
