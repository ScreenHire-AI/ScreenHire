using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// One tool as the reference describes it: what it does, what it takes (<c>inputSchema</c>, as <c>tools/list</c> gives
/// it), the access it needs and an example. <c>access</c> is Read, Write or Verification read, the key scope that shows the
/// tool; <c>confirms</c> says the person confirms each call first; the three hints are the tool's MCP annotations.
/// </summary>
public sealed record McpToolReference(
    string Name,
    string Title,
    string Description,
    string Access,
    bool Confirms,
    bool ReadOnly,
    bool Destructive,
    bool OpenWorld,
    JsonElement InputSchema,
    McpToolExample Example);

/// <summary>A call and what it returns, with made-up ids.</summary>
public sealed record McpToolExample(JsonNode Arguments, JsonNode Result);

/// <summary>The MCP server's tools as integrators read about them: the server's address and instructions, and each tool.</summary>
public sealed record McpToolCatalogue(string Path, string Instructions, IReadOnlyList<McpToolReference> Tools);

/// <summary>
/// Every MCP tool class, and the reference built from them (docs/architecture/api-reference.md). <see cref="McpSetup"/>
/// registers <see cref="ToolTypes"/>, so the reference cannot list a tool the server lacks. Each tool's inputs come from
/// the SDK's own tool definition, exactly as <c>tools/list</c> returns them; the access from its <c>[Authorize]</c>
/// policy; the example from <see cref="McpToolExamples"/>. <c>McpToolCatalogTests</c> writes it to
/// <c>packages/api-client/mcp-tools.json</c> for the dashboard and the developer docs, and the API serves it as a page at
/// <c>/mcp-tools</c>.
/// </summary>
public static class McpToolCatalog
{
    public const string ReadAccess = "Read";

    public const string WriteAccess = "Write";

    public const string VerificationAccess = "Verification read";

    internal static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(HiringTools), typeof(JobTools), typeof(SchedulingTools), typeof(ScorecardTools), typeof(EmailTools), typeof(TaskTools),
        typeof(TemplateTools), typeof(TeamTools), typeof(VerificationTools),
    ];

    /// <summary>The tool methods of <see cref="ToolTypes"/>, in declaration order.</summary>
    internal static IEnumerable<MethodInfo> Methods =>
        ToolTypes.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(m => m.MetadataToken));

    /// <summary>The JSON conventions the server uses for tool arguments and results: enums as names (<see cref="McpSetup"/>).</summary>
    internal static JsonSerializerOptions Json { get; } = CreateJson();

    public static McpToolCatalogue Describe()
    {
        var tools = Methods.Select(method =>
        {
            var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("The catalogue never calls a tool."),
                new McpServerToolCreateOptions { SerializerOptions = Json }).ProtocolTool;
            var policy = method.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
            var access = policy switch
            {
                McpSetup.WritePolicy => WriteAccess,
                McpSetup.VerificationPolicy => VerificationAccess,
                null => ReadAccess,
                _ => throw new InvalidOperationException($"{tool.Name} needs the policy {policy}, which the reference does not describe."),
            };
            var example = McpToolExamples.For(tool.Name);
            return new McpToolReference(
                tool.Name,
                tool.Title ?? tool.Name,
                tool.Description ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                access,
                method.GetParameters().Any(p => p.Name == McpConfirmation.Argument),
                tool.Annotations?.ReadOnlyHint == true,
                tool.Annotations?.DestructiveHint == true,
                tool.Annotations?.OpenWorldHint == true,
                tool.InputSchema,
                example);
        });
        return new(McpSetup.Path, McpSetup.Instructions, [.. tools]);
    }

    /// <summary>The catalogue as the committed file holds it: indented, with <c>\n</c> line endings on every machine.</summary>
    public static string ToJson(McpToolCatalogue catalogue) => JsonSerializer.Serialize(catalogue, Indented).ReplaceLineEndings("\n") + "\n";

    private static readonly JsonSerializerOptions Indented = new(Json) { WriteIndented = true };

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        json.Converters.Add(new JsonStringEnumConverter());
        json.MakeReadOnly();
        return json;
    }
}
