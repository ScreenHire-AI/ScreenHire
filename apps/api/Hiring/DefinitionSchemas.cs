using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Hiring;

/// <summary>
/// The JSON Schemas of the hiring definitions, published next to the OpenAPI document (docs/architecture/workflow-engine.md
/// §6) so that the dashboard, API clients and the workflow engine share one contract. Generated at runtime by .NET's
/// <see cref="JsonSchemaExporter"/> from the same records and the same serializer options the API and the JSONB columns
/// use, so they cannot drift from what is stored or sent.
/// </summary>
internal static class DefinitionSchemas
{
    public static readonly DefinitionSchema HiringTemplateDefinition = new(
        typeof(HiringTemplateDefinition),
        "/v1/schemas/hiring-template-definition.json",
        "Hiring template definition",
        "A hiring template version's content: the workflow a job copies (stages, questionnaire, verification policy, automations) and posting defaults.");

    public static readonly DefinitionSchema HiringWorkflow = new(
        typeof(HiringWorkflow),
        "/v1/schemas/hiring-workflow.json",
        "Hiring workflow",
        "A job's own copy of a hiring workflow: stages, questionnaire, verification policy and automations.");

    public static readonly DefinitionSchema Questionnaire = new(
        typeof(Questionnaire),
        "/v1/schemas/questionnaire.json",
        "Questionnaire",
        "Screening questions: short text, long text, single or multiple choice, yes/no (optionally a knockout), number, URL and file.");

    public static IReadOnlyList<DefinitionSchema> All { get; } = [HiringTemplateDefinition, HiringWorkflow, Questionnaire];

    // Nullability as declared: a member the records do not mark nullable is non-nullable in the schema.
    private static readonly JsonSerializerOptions ExportOptions = new(JsonColumns.SerializerOptions)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>
    /// OpenAPI schema transformer: points each definition's component at its published JSON Schema with
    /// <c>externalDocs</c>. Not a <c>$ref</c>, which Orval would try to fetch while generating the client. The URL is
    /// relative to the API's host, which is configuration (ADR-0003).
    /// </summary>
    public static Task LinkFromOpenApi(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (All.FirstOrDefault(s => s.Type == context.JsonTypeInfo.Type) is { } published)
        {
            schema.ExternalDocs = new OpenApiExternalDocs
            {
                Description = $"JSON Schema: {published.Title}",
                Url = new Uri(published.Path, UriKind.Relative),
            };
        }

        return Task.CompletedTask;
    }

    internal static string Export(DefinitionSchema schema)
    {
        var exported = JsonSchemaExporter.GetJsonSchemaAsNode(
            ExportOptions, schema.Type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject();
        var document = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = schema.Title,
            ["description"] = schema.Description,
        };
        foreach (var (key, value) in exported.ToList())
        {
            exported.Remove(key);
            document[key] = value;
        }

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>One published JSON Schema: the record it describes and where the API serves it.</summary>
internal sealed class DefinitionSchema(Type type, string path, string title, string description)
{
    private readonly Lazy<string> json = new(() => DefinitionSchemas.Export(new(type, path, title, description)));

    public Type Type { get; } = type;

    public string Path { get; } = path;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public string Json => json.Value;
}
