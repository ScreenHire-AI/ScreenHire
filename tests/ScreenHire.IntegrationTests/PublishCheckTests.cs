using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// <c>GET /v1/jobs/{jobId}/publish-check</c>: what would embarrass the team or be refused if the job went live now. It warns
/// and never blocks, it reads the built-in templates' prompts from their rows, and it is the same list for a person, an API
/// key and an assistant.
/// </summary>
public sealed class PublishCheckTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_job_made_from_a_starting_template_warns_about_the_prompt_text_and_the_missing_city()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(new JsonObject { ["title"] = "Office coordinator", ["templateVersionId"] = SystemTemplates.OfficeRoleVersion1 });

        var check = (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK);

        var items = check.Body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("kind").GetString()).Should().Equal("TemplatePrompt", "NoCity");
        items[0].GetProperty("field").GetString().Should().Be("Description");
        items[0].GetProperty("text").GetString().Should().Be(
            "The description still has the template's fill-in text, \"Describe the work, the office and the team.\" and 1 more.");
        items[1].GetProperty("field").GetString().Should().Be("City");
        items[1].GetProperty("boards").EnumerateArray().Select(b => b.GetString()).Should().Equal("Talent.com");
    }

    [Fact]
    public async Task A_job_written_in_the_teams_own_words_has_nothing_to_check_and_a_fixed_prompt_goes_away()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Written("Platform Engineer"));
        (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK).Body.GetProperty("items").GetArrayLength().Should().Be(0);

        var templated = await owner.CreateJobAsync(new JsonObject { ["title"] = "Office coordinator", ["templateVersionId"] = SystemTemplates.OfficeRoleVersion1 });
        var update = Written("Office coordinator");
        update["workplaceType"] = "Onsite";
        update["locations"] = new JsonArray(new JsonObject { ["city"] = "Leeds", ["country"] = "GB" });
        update.Remove("applicantLocationRequirements");
        (await owner.PutAsync($"/v1/jobs/{templated.Id()}", update)).Expect(HttpStatusCode.OK);

        (await owner.GetAsync($"/v1/jobs/{templated.Id()}/publish-check")).Expect(HttpStatusCode.OK)
            .Body.GetProperty("items").GetArrayLength().Should().Be(0, "the prompt, the short text and the missing city are all fixed");
    }

    [Fact]
    public async Task A_short_description_names_the_board_that_would_refuse_it_and_publishing_still_works()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));

        var check = (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK);

        var item = check.Body.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("kind").GetString().Should().Be("ShortDescription");
        item.GetProperty("text").GetString().Should().Contain("Adzuna");
        (await owner.PostAsync($"/v1/jobs/{job.Id()}/publish")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_closed_job_has_nothing_to_check()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        await owner.CloseJobAsync(job.Id());

        (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK).Body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Another_organizations_job_is_a_404_and_an_api_key_reads_the_same_list()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(new JsonObject { ["title"] = "Office coordinator", ["templateVersionId"] = SystemTemplates.OfficeRoleVersion1 });
        var path = $"/v1/jobs/{job.Id()}/publish-check";
        var person = (await owner.GetAsync(path)).Expect(HttpStatusCode.OK).Body.GetRawText();

        (await other.GetAsync(path)).Status.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/v1/jobs/{Guid.NewGuid()}/publish-check")).Status.Should().Be(HttpStatusCode.NotFound);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Reader", ["Read"])).Text("token"));
        (await key.GetAsync(path)).Expect(HttpStatusCode.OK).Body.GetRawText().Should().Be(person);
        using var theirKey = new KeyClient(fixture, (await other.CreateKeyAsync("Reader", ["Read"])).Text("token"));
        (await theirKey.GetAsync(path)).Status.Should().Be(HttpStatusCode.NotFound);
        using var anonymous = new HttpClient { BaseAddress = fixture.GetEndpoint("api") };
        (await anonymous.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_check_job_tool_gives_an_assistant_the_same_list()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(new JsonObject { ["title"] = "Office coordinator", ["templateVersionId"] = SystemTemplates.OfficeRoleVersion1 });
        var expected = (await owner.GetAsync($"/v1/jobs/{job.Id()}/publish-check")).Expect(HttpStatusCode.OK).Body;
        await using var mcp = await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Reader", ["Read"])).Text("token"));

        var answer = await mcp.CallAsync("check_job", new() { ["jobId"] = job.Id() });

        // The tool's JSON escapes quotes its own way, so compare what it says rather than the bytes.
        string[] Say(JsonElement body) => [.. body.GetProperty("items").EnumerateArray().Select(i => $"{i.GetProperty("kind").GetString()}: {i.GetProperty("text").GetString()}")];
        Say(answer).Should().Equal(Say(expected)).And.HaveCount(2);
        (await mcp.CallFailingAsync("check_job", new() { ["jobId"] = Guid.NewGuid() })).Should().Contain("No job with that id");
    }

    // A job in the team's own words: long enough for every board, with a salary, so it has nothing to say.
    private static JsonObject Written(string title)
    {
        var job = Samples.RemoteJob(title);
        job["description"] = "<h2>About the role</h2><p>You keep our platform running for customers who rely on it every single day, with a small team that owns its work from first idea to production.</p>";
        return job;
    }
}
