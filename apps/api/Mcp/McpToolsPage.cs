using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// The MCP tools reference at <c>/mcp-tools</c>, beside the API reference at <c>/scalar</c> (docs/architecture/api-reference.md):
/// one section per tool, anchored by its name, from <see cref="McpToolCatalog.Describe"/>. The dashboard's MCP server page
/// links each tool to its anchor. It is public like <c>/scalar</c>: it documents the server and holds no data. Every value
/// is HTML-encoded, and the page loads nothing from anywhere: system fonts and the browser's own colours.
/// </summary>
internal static class McpToolsPage
{
    public const string Path = "/mcp-tools";

    private static readonly Lazy<string> Html = new(() => Render(McpToolCatalog.Describe()));

    public static IEndpointRouteBuilder MapMcpToolsPage(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, () => TypedResults.Content(Html.Value, "text/html; charset=utf-8"))
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }

    internal static string Render(McpToolCatalogue catalogue)
    {
        var e = HtmlEncoder.Default;
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>ScreenHire MCP tools</title>
            <style>
            :root { color-scheme: light dark; font-family: system-ui, sans-serif; line-height: 1.5; }
            body { margin: 0 auto; max-width: 52rem; padding: 2rem 1rem 4rem; background: Canvas; color: CanvasText; }
            nav ul { columns: 2; padding-left: 1.25rem; }
            section { border-top: 1px solid GrayText; padding-top: 1rem; margin-top: 2rem; }
            h2 { font-size: 1.25rem; margin: 0; }
            code, pre { font-family: ui-monospace, monospace; font-size: 0.875rem; }
            pre { overflow-x: auto; padding: 0.75rem; border: 1px solid GrayText; border-radius: 0.375rem; }
            table { border-collapse: collapse; width: 100%; }
            th, td { text-align: left; vertical-align: top; padding: 0.25rem 0.5rem 0.25rem 0; }
            .facts { color: GrayText; }
            </style>
            </head>
            <body>
            <h1>ScreenHire MCP tools</h1>
            """);
        html.Append("<p>An AI assistant connects to <code>").Append(e.Encode(catalogue.Path))
            .Append("</code> with an organization API key or an OAuth connection. Each tool below says what it does, what it takes, the access it needs, ")
            .Append("and shows a call with a shortened result. The ids are made up. For the HTTP API, see <a href=\"/scalar\">the API reference</a>.</p>");
        html.Append("<h2>What the server tells every assistant</h2><pre>").Append(e.Encode(catalogue.Instructions.Trim())).Append("</pre>");
        html.Append("<nav><ul>");
        foreach (var tool in catalogue.Tools)
        {
            html.Append("<li><a href=\"#").Append(e.Encode(tool.Name)).Append("\"><code>").Append(e.Encode(tool.Name)).Append("</code></a></li>");
        }

        html.Append("</ul></nav>");
        foreach (var tool in catalogue.Tools)
        {
            html.Append("<section id=\"").Append(e.Encode(tool.Name)).Append("\"><h2><code>").Append(e.Encode(tool.Name)).Append("</code>: ")
                .Append(e.Encode(tool.Title)).Append("</h2>");
            html.Append("<p class=\"facts\">Needs ").Append(e.Encode(tool.Access)).Append(tool.Access == McpToolCatalog.ReadAccess ? ", which every key has" : " access")
                .Append(tool.ReadOnly ? ". Reads only." : ". Changes data.")
                .Append(tool.Confirms ? " The person confirms each call first." : "").Append("</p>");
            html.Append("<p>").Append(e.Encode(tool.Description)).Append("</p>");
            html.Append(Inputs(tool.InputSchema, e));
            html.Append("<h3>Example</h3><pre>").Append(e.Encode(Pretty(tool.Example.Arguments))).Append("</pre>");
            html.Append("<pre>").Append(e.Encode(Pretty(tool.Example.Result))).Append("</pre></section>");
        }

        html.Append("</body></html>\n");
        return html.ToString().ReplaceLineEndings("\n");
    }

    private static string Inputs(JsonElement schema, HtmlEncoder e)
    {
        if (!schema.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            return "<p>Takes no inputs.</p>";
        }

        var required = schema.TryGetProperty("required", out var names) ? names.EnumerateArray().Select(n => n.GetString()).ToHashSet() : [];
        var rows = new StringBuilder("<table><thead><tr><th>Input</th><th>Type</th><th></th><th>What it is</th></tr></thead><tbody>");
        foreach (var property in properties.EnumerateObject())
        {
            rows.Append("<tr><td><code>").Append(e.Encode(property.Name)).Append("</code></td><td>").Append(e.Encode(TypeOf(property.Value)))
                .Append("</td><td>").Append(required.Contains(property.Name) ? "required" : "optional").Append("</td><td>")
                .Append(e.Encode(property.Value.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "")).Append("</td></tr>");
        }

        return rows.Append("</tbody></table>").ToString();
    }

    // The JSON Schema type in a word: "string", "integer", "array of object", an enum's names, or "string or null".
    internal static string TypeOf(JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var values))
        {
            return string.Join(", ", values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()));
        }

        var types = schema.TryGetProperty("type", out var type)
            ? type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString() ?? "").ToList() : [type.GetString() ?? ""]
            : ["object"];
        var named = types.Where(t => t != "null").Select(t => t == "array" && schema.TryGetProperty("items", out var items) ? $"array of {TypeOf(items)}" : t);
        var text = string.Join(" or ", named);
        return types.Contains("null") ? $"{text} or null" : text;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Pretty(JsonNode node) => node.ToJsonString(Indented);
}
