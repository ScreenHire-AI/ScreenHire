using Xunit.Sdk;

namespace ScreenHire.E2E;

/// <summary>Guards the harness itself: a check that cannot fail would let every page pass.</summary>
public sealed class BrowserTestTests(E2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Accessibility_check_reports_violations()
    {
        await Page.SetContentAsync("<!doctype html><html><body><main><img src='data:,'></main></body></html>");

        var check = ExpectNoAccessibilityViolationsAsync;

        (await check.Should().ThrowAsync<XunitException>()).WithMessage("*image-alt*");
    }
}
