using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.E2E;

/// <summary>
/// A workflow's stages where they read only (design system section 11, Stage flow, S8, S9, F1). The choice between a
/// row of columns and the vertical list is made in CSS from the container's width and the number of stages, which
/// jsdom cannot evaluate, so it is checked here in a real browser: on the built-in Software engineer template (five
/// stages before the ones that end a candidacy) and on a closed job's workflow page with twenty.
/// </summary>
public sealed class StageFlowTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string SoftwareEngineerTemplate = "0199e8a0-0000-7000-8000-000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1440, 1000, true)]
    [InlineData(1024, 800, false)]
    [InlineData(390, 844, false)]
    public async Task The_stages_sit_in_a_row_where_every_stage_fits_and_stack_where_they_do_not(int width, int height, bool inARow)
    {
        await Page.SetViewportSizeAsync(width, height);
        await SignInAsync(await NewMemberAsync());
        await Page.GotoAsync(DashboardUrl($"/templates/{SoftwareEngineerTemplate}"));

        var stages = Page.GetByRole(AriaRole.List, new() { Name = "Stages in order" });
        await Expect(stages).ToBeVisibleAsync();
        var items = stages.Locator(":scope > li");
        await Expect(items).ToHaveCountAsync(5);

        var boxes = new List<(double X, double Y)>();
        for (var i = 0; i < 5; i++)
        {
            var box = await items.Nth(i).BoundingBoxAsync();
            box.Should().NotBeNull();
            boxes.Add((box!.X, box.Y));
        }

        // Reading order is the stages' order in both layouts: left to right in a row, top to bottom in a list.
        if (inARow)
        {
            boxes.Select(box => box.Y).Distinct().Should().ContainSingle("every stage shares one row at {0}px", width);
            boxes.Select(box => box.X).Should().BeInAscendingOrder();
        }
        else
        {
            boxes.Select(box => box.X).Distinct().Should().ContainSingle("the stages stack at {0}px", width);
            boxes.Select(box => box.Y).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        }

        // Never scrolls sideways, and never squeezes a stage narrower than its 12rem column (F1, S9).
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page at {0}px scrolls sideways", width);
        var narrowest = await items.EvaluateAllAsync<double>("lis => Math.min(...lis.map(li => li.getBoundingClientRect().width))");
        narrowest.Should().BeGreaterThanOrEqualTo(inARow ? 192 : 0, "a stage keeps its 12rem column");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_closed_jobs_workflow_page_draws_the_same_view_and_twenty_stages_stack_on_a_wide_screen()
    {
        await Page.SetViewportSizeAsync(1440, 1000);
        var email = await NewMemberAsync();
        var jobId = await NewClosedJobAsync(email, stages: 20);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}/workflow"));

        var stages = Page.GetByRole(AriaRole.List, new() { Name = "Stages in order" });
        await Expect(stages).ToBeVisibleAsync();
        var items = stages.Locator(":scope > li");
        await Expect(items).ToHaveCountAsync(20);

        var boxes = await items.EvaluateAllAsync<double[][]>("lis => lis.map(li => { const box = li.getBoundingClientRect(); return [box.x, box.y]; })");
        boxes.Select(box => box[0]).Distinct().Should().ContainSingle("twenty stages need 240rem, so they stack");
        boxes.Select(box => box[1]).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("twenty stages never scroll sideways");
        await ExpectNoAccessibilityViolationsAsync();
    }

    // A closed job reads its workflow as content, with the stages that lead up to the ones that end a candidacy.
    private async Task<Guid> NewClosedJobAsync(string email, int stages)
    {
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var job = await ReadAsync(await session.PostAsync("/v1/jobs", new { title = "Long pipeline" }, Ct), HttpStatusCode.Created);
        var jobId = job.GetProperty("id").GetGuid();

        var workflow = JsonNode.Parse(job.GetProperty("workflow").GetRawText())!.AsObject();
        var list = workflow["stages"]!.AsArray();
        var firstEnd = list.ToList().FindIndex(stage => (string?)stage!["kind"] is "Hired" or "Rejected" or "Withdrawn");
        for (var number = firstEnd + 1; number <= stages; number++)
        {
            list.Insert(number - 1, new JsonObject { ["id"] = $"round-{number}", ["name"] = $"Round {number}", ["kind"] = "Interview" });
        }

        await ReadAsync(await session.PutAsync($"/v1/jobs/{jobId}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 }, Ct), HttpStatusCode.OK);
        await ReadAsync(await session.PostAsync($"/v1/jobs/{jobId}/close", new { }, Ct), HttpStatusCode.OK);
        return jobId;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private async Task<string> NewMemberAsync()
    {
        var email = ApiSession.NewEmail("e2e-stage-flow");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct);
        onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        return email;
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
