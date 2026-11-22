using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using ScreenHire.Api.Mcp;
using ScreenHire.Domain.Views;

namespace ScreenHire.UnitTests.Mcp;

/// <summary>
/// The MCP tools (docs/architecture/mcp-server.md): every tool that changes data is behind the Write scope, so a Read key
/// neither sees nor calls it, and every read-only hiring tool is open to any key. The verification tools are behind the
/// Verification read scope an Owner gives (ADR-0021), only read, and say a level is never a reason to reject on its own.
/// The tools that reach outside the team or end a candidacy ask the person first and carry the destructive hint. Tool
/// names are snake_case verbs.
/// </summary>
public sealed partial class McpToolsTests
{
    // Every tool class the server registers, the verification tools apart.
    private static readonly MethodInfo[] HiringToolMethods = [.. McpToolCatalog.Methods.Where(m => m.DeclaringType != typeof(VerificationTools))];

    [Fact]
    public void The_server_registers_every_tool_class_in_the_assembly()
    {
        var declared = typeof(McpSetup).Assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        McpToolCatalog.ToolTypes.Should().BeEquivalentTo(declared, "a tool class left out of ToolTypes is neither served nor documented");
    }

    [Fact]
    public void Exactly_the_consequential_tools_ask_the_person_and_carry_the_destructive_hint()
    {
        var confirmed = HiringToolMethods.Where(m => m.GetParameters().Any(p => p.Name == McpConfirmation.Argument)).ToList();

        confirmed.Select(NameOf).Should().BeEquivalentTo(["send_email", "invite_to_schedule", "publish_job", "close_job", "move_to_stage"]);
        foreach (var tool in confirmed)
        {
            tool.GetCustomAttribute<McpServerToolAttribute>()!.Destructive.Should().BeTrue($"{NameOf(tool)} asks the person, so clients that prompt on the hint prompt too");
            tool.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Equal([McpSetup.WritePolicy]);
            tool.GetCustomAttribute<DescriptionAttribute>()!.Description.Should().Contain("confirm: true", NameOf(tool));
        }

        // Every call of the four always asks, so Claude Code prompts on each; a move asks only for a Rejected stage.
        foreach (var tool in confirmed.Where(t => NameOf(t) != "move_to_stage"))
        {
            tool.GetParameters().Single(p => p.Name == McpConfirmation.Argument).ParameterType.Should().Be<bool>($"{NameOf(tool)} requires confirm");
            tool.GetCustomAttributes<McpMetaAttribute>().Should().Contain(m => m.Name == "anthropic/requiresUserInteraction", NameOf(tool));
        }

        confirmed.Single(t => NameOf(t) == "move_to_stage").GetParameters().Single(p => p.Name == McpConfirmation.Argument)
            .ParameterType.Should().Be<bool?>("only a Rejected stage needs it");
    }

    [Theory]
    [InlineData(null, null, null, true, null)]
    [InlineData("ReviewSuggested", null, null, false, "verification")]
    [InlineData(null, "SomeIndicators", null, false, "verification")]
    [InlineData(null, null, "needs-review", false, "verification")]
    [InlineData(null, null, "verification-recommended", false, "verification")]
    [InlineData(null, null, "overdue", false, "built-in view")]
    public void A_view_that_filters_on_verification_or_a_built_in_view_is_refused_with_the_reason(
        string? authenticity, string? aiAssistance, string? baseView, bool applies, string? reason)
    {
        var refusal = TemplateTools.Refusal(new ViewDefinition { Authenticity = authenticity, AiAssistance = aiAssistance, BaseView = baseView });

        (refusal is null).Should().Be(applies);
        if (reason is not null)
        {
            refusal.Should().Contain(reason);
        }
    }

    [Fact]
    public void No_hiring_tool_is_named_for_verification() =>
        HiringToolMethods.Select(NameOf).Should().NotContain(name => name.Contains("verification", StringComparison.Ordinal));

    private static readonly MethodInfo[] VerificationToolMethods = ToolsOf(typeof(VerificationTools));

    private static MethodInfo[] ToolsOf(Type type) =>
        [.. type.GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    private static string NameOf(MethodInfo tool) => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!;

    [Fact]
    public void Every_tool_that_changes_data_needs_the_write_scope_and_no_read_needs_it()
    {
        HiringToolMethods.Should().NotBeEmpty();
        foreach (var tool in HiringToolMethods)
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
    public void Reports_and_saved_candidates_are_read_tools_every_key_may_call()
    {
        HiringToolMethods.Select(NameOf).Should().Contain(["get_hiring_report", "list_saved_candidates"]);
        foreach (var name in new[] { "get_hiring_report", "list_saved_candidates" })
        {
            var tool = HiringToolMethods.Single(t => NameOf(t) == name);
            tool.GetCustomAttribute<McpServerToolAttribute>()!.ReadOnly.Should().BeTrue(name);
            tool.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty($"{name} needs only the Read scope");
        }
    }

    [Fact]
    public void Every_verification_tool_needs_the_verification_scope_and_only_reads()
    {
        VerificationToolMethods.Select(NameOf).Should().BeEquivalentTo(["get_verification", "find_candidates_by_verification_level"]);
        foreach (var tool in VerificationToolMethods)
        {
            tool.GetCustomAttribute<McpServerToolAttribute>()!.ReadOnly.Should().BeTrue($"{tool.Name} only reads");
            tool.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Equal(
                [McpSetup.VerificationPolicy], $"{tool.Name} reads verification, which only a key an Owner gave the scope may");
        }
    }

    [Fact]
    public void Every_verification_tool_says_a_level_is_never_a_reason_to_reject_on_its_own()
    {
        foreach (var tool in VerificationToolMethods)
        {
            var description = tool.GetCustomAttribute<DescriptionAttribute>()!.Description;
            description.Should().Contain("never a reason to reject", $"{tool.Name} must carry ADR-0004's rule");
            description.Should().Contain("Rejected stage");
        }

        McpVerificationMappings.Guidance.Should().Contain("never a reason to reject").And.Contain("Rejected stage");
    }

    [Fact]
    public void No_verification_tool_can_reject_move_or_change_anything()
    {
        // Their parameters name what to read, never a stage or an action, and their names are reads (CLAUDE.md non-negotiable 2).
        foreach (var tool in VerificationToolMethods)
        {
            NameOf(tool).Should().NotMatchRegex("reject|move|stage|dismiss|note|tag|task|add|remove|create");
            // includeDismissed is a filter on what to read, not an act of dismissing.
            tool.GetParameters().Select(p => p.Name!).Where(name => name != "includeDismissed")
                .Should().NotContain(name => Regex.IsMatch(name, "(?i)reject|stage|action|note|dismiss"));
        }
    }

    [Fact]
    public void Tool_names_are_snake_case_and_unique()
    {
        var names = HiringToolMethods.Concat(VerificationToolMethods).Select(NameOf).ToList();
        names.Should().OnlyContain(name => name != null && SnakeCase().IsMatch(name)).And.OnlyHaveUniqueItems();
    }

    [GeneratedRegex("^[a-z]+(_[a-z]+)*$")]
    private static partial Regex SnakeCase();
}
