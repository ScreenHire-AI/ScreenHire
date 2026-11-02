using System.Text.Json.Nodes;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// Kangentic runs <c>stopScript</c> from <c>kangentic.json</c> by itself when a task pauses idle or low on memory, and
/// before it reaps a finished task. It must stop only this clone's stack: <c>--all</c> stops other clones' stacks, and
/// <c>--force</c> cleans up persistent resources. Nobody is watching when it runs, so a later edit that adds either
/// flag, or drops the AppHost path, would go unnoticed until it stopped the wrong stack.
/// </summary>
public sealed class ProjectStopScriptTests
{
    [Fact]
    public void Stop_script_stops_only_this_clones_apphost()
    {
        var script = JsonNode.Parse(File.ReadAllText(RepositoryFiles.PathTo("kangentic.json")))!["stopScript"]?.GetValue<string>();

        script.Should().NotBeNullOrWhiteSpace("kangentic.json must name the Stop script that ends a parked stack");
        script.Should().Contain("dotnet aspire stop ");
        script.Should().Contain("--non-interactive", "an unattended run must never prompt");
        script.Should().Contain("--apphost apps/apphost/ScreenHire.AppHost.csproj", "the stop must name this clone's AppHost");
        script.Should().NotContain("--all", "it stops other clones' stacks");
        script.Should().NotContain("--force", "it cleans up persistent resources");
    }
}
