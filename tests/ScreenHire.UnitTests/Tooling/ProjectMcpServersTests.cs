using System.Text.Json.Nodes;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// Every MCP server in <c>.mcp.json</c> must be approved in the committed <c>.claude/settings.json</c>. Each Kangentic
/// task runs in a fresh worktree without the git-ignored <c>settings.local.json</c>, so an unapproved server stops the
/// session at Claude Code's "New MCP server found" prompt, and an unattended task waits there until a person answers.
/// </summary>
public sealed class ProjectMcpServersTests
{
    [Fact]
    public void Every_project_mcp_server_is_approved_with_its_tools()
    {
        var servers = ReadJson(".mcp.json")["mcpServers"]!.AsObject().Select(server => server.Key).ToList();
        var settings = ReadJson(".claude", "settings.json");
        var approved = Strings(settings["enabledMcpjsonServers"]);
        var allowed = Strings(settings["permissions"]?["allow"]);

        servers.Should().NotBeEmpty();
        servers.Should().AllSatisfy(server =>
        {
            approved.Should().Contain(server, $"\"{server}\" from .mcp.json must be in enabledMcpjsonServers in .claude/settings.json");
            allowed.Should().Contain($"mcp__{server}", $"its tools must be allowed with \"mcp__{server}\" in permissions.allow");
        });
    }

    private static JsonNode ReadJson(params string[] parts) => JsonNode.Parse(File.ReadAllText(RepositoryFiles.PathTo(parts)))!;

    private static List<string> Strings(JsonNode? array) =>
        array?.AsArray().Select(item => item!.GetValue<string>()).ToList() ?? [];
}
