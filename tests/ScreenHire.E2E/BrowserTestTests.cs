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

    [Theory]
    // What Svelte's development mode writes for a component: its source file's path, in whichever quotes the minifier chose.
    [InlineData("H[a]=`/work/packages/ui/src/questions/DropArea.svelte`;var F=S(D(`<span> </span>`),H[a],[[220,1]]);", true)]
    [InlineData("H[a]=\"/work/packages/ui/src/questions/DropArea.svelte\";", true)]
    [InlineData("H[a]='/work/packages/ui/src/questions/DropArea.svelte';", true)]
    // A production build: no source path, and a chunk named after Svelte's client entry is not one.
    [InlineData("import{t as e}from\"./client.svelte.CDphaPW8.js\";var F=S(D(`<span> </span>`));", false)]
    public void A_frontend_built_in_development_mode_is_told_from_a_production_build(string script, bool development)
    {
        var folder = Directory.CreateTempSubdirectory("e2e-build-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "apply.js"), "addEventListener('submit',()=>{});");
            File.WriteAllText(Path.Combine(folder, "FileField.js"), script);

            var found = E2EFixture.DevelopmentBuildFile(folder);

            (found is null ? null : Path.GetFileName(found)).Should().Be(development ? "FileField.js" : null);
            E2EFixture.DevelopmentBuildFile(Path.Combine(folder, "missing")).Should().BeNull("a site with no client scripts has nothing to read");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
