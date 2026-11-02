using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// The saved candidates' edges (docs/architecture/talent-pool.md): one organization never sees or acts on another's
/// saved candidates, and the list is for people only, so an organization API key reaches none of its routes. Each test
/// makes its own organizations.
/// </summary>
public sealed class TalentPoolBoundaryTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Another_organization_sees_none_of_the_saved_candidates_and_cannot_act_on_them()
    {
        using var a = await PoolWorld.CreateAsync(fixture);
        using var b = await PoolWorld.CreateAsync(fixture);
        var (aJob, _) = await a.OpenJobAsync("Warehouse Lead");
        var (bJob, _) = await b.OpenJobAsync("Night Supervisor");
        var kept = await a.KeptPersonAsync(aJob, "across", note: "Ask again in spring");

        (await b.PoolAsync()).Should().BeEmpty("a list of saved candidates belongs to its organization");
        (await b.Owner.PostAsync($"/v1/talent-pool/{kept.CandidateId}/remove", new JsonObject())).Expect(HttpStatusCode.NotFound);
        (await b.Owner.PutAsync($"/v1/talent-pool/{kept.CandidateId}/note", new JsonObject { ["note"] = "Not yours" })).Expect(HttpStatusCode.NotFound);
        (await b.Owner.GetAsync($"/v1/talent-pool/{kept.CandidateId}/history")).Expect(HttpStatusCode.NotFound);
        var invited = (await b.InviteAsync([kept.CandidateId], bJob)).Expect(HttpStatusCode.Accepted);
        invited.At("queued").GetInt32().Should().Be(0);
        invited.At("skipped").GetInt32().Should().Be(1);
        var added = (await b.Owner.PostAsync("/v1/talent-pool/applications", new JsonObject
        {
            ["candidateIds"] = PoolWorld.Ids([kept.CandidateId]),
            ["jobId"] = bJob,
        })).Expect(HttpStatusCode.OK);
        added.Count("applicationIds").Should().Be(0);

        var stillThere = (await a.PoolAsync()).Should().ContainSingle().Subject;
        stillThere.GetProperty("candidateId").GetGuid().Should().Be(kept.CandidateId);
        stillThere.GetProperty("note").GetString().Should().Be("Ask again in spring", "the other organization's edit changed nothing");
    }

    [Fact]
    public async Task The_settings_routes_of_the_permission_period_are_gone()
    {
        using var world = await PoolWorld.CreateAsync(fixture);

        (await world.Owner.GetAsync("/v1/organization/talent-pool-settings")).Status.Should().Be(HttpStatusCode.NotFound);
        (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 18 })).Status
            .Should().Be(HttpStatusCode.NotFound);
        (await world.Owner.GetAsync("/v1/organization/talent-pool-settings/history")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_api_key_reaches_none_of_the_saved_candidates_routes()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Dispatch Planner");
        var person = await world.KeptPersonAsync(job, "keyed");
        var application = await world.ApplyAsync(job, ApiSession.NewEmail("key"), keep: true);
        using var key = new KeyClient(fixture, (await world.Owner.CreateKeyAsync("ATS sync", ["Read", "Write"])).Text("token"));

        var reads = await key.GetAsync("/v1/talent-pool");
        var saves = await key.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = PoolWorld.Ids([application]) });
        var edits = await key.PutAsync($"/v1/talent-pool/{person.CandidateId}/note", new JsonObject { ["note"] = "Edited by a key" });
        var history = await key.GetAsync($"/v1/talent-pool/{person.CandidateId}/history");

        foreach (var result in new[] { reads, saves, edits, history })
        {
            result.Status.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        (await world.DetailAsync(application)).At("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null,
            "saving someone is a person's decision, never an integration's (ADR-0004)");
        (await world.PoolAsync()).Single().GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "the key's edit changed nothing");
    }
}
