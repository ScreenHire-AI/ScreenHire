using System.Text.Json;
using ScreenHire.Api.Mcp;

namespace ScreenHire.UnitTests.Mcp;

/// <summary>
/// The MCP tools reference (docs/architecture/api-reference.md): <c>packages/api-client/mcp-tools.json</c> holds every
/// tool as the SDK describes it to clients, with its access and an example, for the dashboard's tool chips and the
/// developer docs. Changing a tool fails here first; rewrite the file with
/// <c>UPDATE_MCP_TOOLS=1 dotnet test --project tests/ScreenHire.UnitTests --filter-class "*McpToolCatalogTests"</c>.
/// </summary>
public sealed class McpToolCatalogTests
{
    private const string UpdateVariable = "UPDATE_MCP_TOOLS";

    [Fact]
    public void The_committed_catalogue_matches_the_tools()
    {
        var path = RepositoryFiles.PathTo("packages", "api-client", "mcp-tools.json");
        var expected = McpToolCatalog.ToJson(McpToolCatalog.Describe());

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, expected);
        }

        File.Exists(path).Should().BeTrue($"{path} holds the catalogue; create it with {UpdateVariable}=1");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(expected, $"a tool changed: rewrite the file with {UpdateVariable}=1 and review the diff");
    }

    [Fact]
    public void Every_tool_has_an_example_whose_arguments_it_takes()
    {
        var catalogue = McpToolCatalog.Describe();

        McpToolExamples.Names.Should().BeEquivalentTo(catalogue.Tools.Select(t => t.Name), "an example for a tool that is gone is dead text");
        foreach (var tool in catalogue.Tools)
        {
            var properties = tool.InputSchema.TryGetProperty("properties", out var p) ? p.EnumerateObject().Select(x => x.Name).ToHashSet() : [];
            var required = tool.InputSchema.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            var given = tool.Example.Arguments.AsObject().Select(a => a.Key).ToList();

            given.Should().BeSubsetOf(properties, $"{tool.Name}'s example passes only inputs it takes");
            required.Should().BeSubsetOf(given, $"{tool.Name}'s example passes every required input");
            tool.Description.Should().NotBeNullOrWhiteSpace(tool.Name);
            tool.Title.Should().NotBe(tool.Name, $"{tool.Name} has a title in words");
        }
    }

    [Fact]
    public void The_catalogue_says_which_access_each_tool_needs_and_never_lists_the_bound_parameters()
    {
        var tools = McpToolCatalog.Describe().Tools.ToDictionary(t => t.Name);

        tools["find_jobs"].Access.Should().Be(McpToolCatalog.ReadAccess);
        tools["send_email"].Access.Should().Be(McpToolCatalog.WriteAccess);
        tools["send_email"].Confirms.Should().BeTrue();
        tools["get_verification"].Access.Should().Be(McpToolCatalog.VerificationAccess);
        foreach (var tool in tools.Values)
        {
            var inputs = tool.InputSchema.TryGetProperty("properties", out var p) ? p.EnumerateObject().Select(x => x.Name).ToList() : [];
            inputs.Should().NotContain(["user", "server", "context", "cancellationToken"], $"{tool.Name}'s schema holds only what an assistant passes");
        }
    }

    [Fact]
    public void The_reference_page_anchors_every_tool_and_encodes_what_it_shows()
    {
        var catalogue = McpToolCatalog.Describe();
        var page = McpToolsPage.Render(catalogue with { Instructions = "<script>alert(1)</script>" });

        foreach (var tool in catalogue.Tools)
        {
            page.Should().Contain($"id=\"{tool.Name}\"").And.Contain($"href=\"#{tool.Name}\"");
        }

        page.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        page.Should().Contain("href=\"/scalar\"");
    }

    [Theory]
    [InlineData("""{"type":"string"}""", "string")]
    [InlineData("""{"type":["string","null"]}""", "string or null")]
    [InlineData("""{"type":"array","items":{"type":"string","format":"uuid"}}""", "array of string")]
    [InlineData("""{"type":["string","null"],"enum":["Open","Closed",null]}""", "Open, Closed")]
    public void An_input_type_reads_in_words(string schema, string words)
    {
        using var document = JsonDocument.Parse(schema);

        McpToolsPage.TypeOf(document.RootElement).Should().Be(words);
    }
}
