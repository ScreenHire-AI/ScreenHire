using System.Net;

namespace ScreenHire.E2E;

/// <summary>
/// A workflow's stages on a template's page (design system section 11, Stage flow, S8, S9, F1). The choice between a
/// row of columns and the vertical list is made in CSS from the container's width and the number of stages, which
/// jsdom cannot evaluate, so it is checked here in a real browser, on the built-in Software engineer template (five
/// stages before the ones that end a candidacy).
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
