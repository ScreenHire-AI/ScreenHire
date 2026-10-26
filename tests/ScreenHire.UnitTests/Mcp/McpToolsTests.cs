using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using ScreenHire.Api.Mcp;

namespace ScreenHire.UnitTests.Mcp;

/// <summary>
/// The MCP tools (docs/architecture/mcp-server.md): every tool that changes data is behind the Write scope, so a Read key
/// neither sees nor calls it, and every read-only tool is open to any key. Tool names are snake_case verbs.
/// </summary>
public sealed partial class McpToolsTests
{
    private static readonly MethodInfo[] Tools =
        [.. typeof(HiringTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    [Fact]
    public void Every_tool_that_changes_data_needs_the_write_scope_and_no_read_needs_it()
    {
        Tools.Should().NotBeEmpty();
        foreach (var tool in Tools)
        {
            var readOnly = tool.GetCustomAttribute<McpServerToolAttribute>()!.ReadOnly;
            var policies = tool.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToList();
            if (readOnly)
            {
                policies.Should().BeEmpty($"{tool.Name} only reads, which every key may do");
            }
            else
            {
                policies.Should().Equal([McpSetup.WritePolicy], $"{tool.Name} changes data");
            }
        }
    }

    [Fact]
    public void Tool_names_are_snake_case_and_unique()
    {
        var names = Tools.Select(t => t.GetCustomAttribute<McpServerToolAttribute>()!.Name).ToList();
        names.Should().OnlyContain(name => name != null && SnakeCase().IsMatch(name)).And.OnlyHaveUniqueItems();
    }

    [GeneratedRegex("^[a-z]+(_[a-z]+)*$")]
    private static partial Regex SnakeCase();
}
