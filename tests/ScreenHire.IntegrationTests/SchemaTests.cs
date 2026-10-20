using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The hiring definitions' JSON Schemas are served by the API without a sign-in, and the OpenAPI document points each
/// definition's component at its schema (<c>externalDocs.url</c>).
/// </summary>
public sealed class SchemaTests(AppHostFixture fixture)
{
    private const string TemplateDefinitionPath = "/v1/schemas/hiring-template-definition.json";
    private const string WorkflowPath = "/v1/schemas/hiring-workflow.json";
    private const string QuestionnairePath = "/v1/schemas/questionnaire.json";

    [Fact]
    public async Task The_hiring_template_definition_schema_describes_a_workflow_and_posting_defaults_with_string_enums()
    {
        var schema = await GetSchemaAsync(TemplateDefinitionPath);

        Properties(schema).Should().Contain(["workflow", "postingDefaults"]);
        EnumValues(schema).Should().Contain(values => values.Contains("Applied") && values.Contains("Hired"), "stage kinds are strings, not numbers");
        EnumValues(schema).Should().Contain(values => values.Contains("FullTime"), "posting defaults use the job enums as strings");
    }

    [Fact]
    public async Task The_hiring_workflow_schema_describes_stages_questionnaire_integrity_policy_and_automations()
    {
        var schema = await GetSchemaAsync(WorkflowPath);

        Properties(schema).Should().Contain(["stages", "questionnaire", "integrityPolicy", "automations"]);
        EnumValues(schema).Should().Contain(values => values.Contains("Applied") && values.Contains("Custom"));
        EnumValues(schema).Should().Contain(values => values.Contains("Reject") && values.Contains("QueueForRejectionReview"));
    }

    [Fact]
    public async Task The_questionnaire_schema_describes_questions_with_string_types()
    {
        var schema = await GetSchemaAsync(QuestionnairePath);

        Properties(schema).Should().Contain("questions");
        EnumValues(schema).Should().Contain(values => values.Contains("YesNo") && values.Contains("SingleChoice"));
    }

    [Fact]
    public async Task The_openapi_document_links_each_definition_component_to_its_schema_and_the_link_resolves()
    {
        using var client = fixture.CreateHttpClient("api");
        var cancellationToken = TestContext.Current.CancellationToken;

        var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", cancellationToken);

        var components = openApi.GetProperty("components").GetProperty("schemas");
        var expected = new Dictionary<string, string>
        {
            ["HiringTemplateDefinition"] = TemplateDefinitionPath,
            ["HiringWorkflow"] = WorkflowPath,
            ["Questionnaire"] = QuestionnairePath,
        };
        foreach (var (component, path) in expected)
        {
            var url = components.GetProperty(component).GetProperty("externalDocs").GetProperty("url").GetString();
            url.Should().Be(path, "{0} points at its JSON Schema", component);
            using var response = await client.GetAsync(url, cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "the link from {0} resolves", component);
        }
    }

    [Fact]
    public async Task The_template_version_contract_uses_the_linked_definition_component()
    {
        using var client = fixture.CreateHttpClient("api");

        var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);

        var definition = openApi.GetProperty("components").GetProperty("schemas").GetProperty("HiringTemplateVersionDetail")
            .GetProperty("properties").GetProperty("definition");
        definition.GetRawText().Should().Contain("#/components/schemas/HiringTemplateDefinition");
    }

    private async Task<JsonElement> GetSchemaAsync(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient("api"); // no cookies: the schemas are public
        using var response = await client.GetAsync(path, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/schema+json");
        var schema = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        schema.GetProperty("$schema").GetString().Should().Contain("2020-12");
        schema.GetProperty("type").GetString().Should().Be("object");
        return schema;
    }

    private static IEnumerable<string> Properties(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    // Every "enum" array anywhere in the schema, as sets of strings.
    private static List<HashSet<string>> EnumValues(JsonElement schema)
    {
        var found = new List<HashSet<string>>();
        Walk(schema);
        return found;

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "enum" && property.Value.ValueKind == JsonValueKind.Array)
                        {
                            found.Add([.. property.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]);
                        }
                        else
                        {
                            Walk(property.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }
}
