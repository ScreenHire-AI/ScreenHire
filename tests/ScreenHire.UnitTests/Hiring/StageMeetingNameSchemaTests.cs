using System.Text.Json.Nodes;
using ScreenHire.Api.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// The published JSON Schemas describe a stage's optional <c>meetingName</c>, so an API client or the dashboard's editor
/// knows the field exists. The schema is generated from the records, so this fails if the member leaves the stage.
/// </summary>
public sealed class StageMeetingNameSchemaTests
{
    [Fact]
    public void The_workflow_schema_gives_a_stage_an_optional_meeting_name_string()
    {
        AssertOptionalString(DefinitionSchemas.HiringWorkflow.Json);
    }

    [Fact]
    public void The_template_definition_schema_gives_a_stage_an_optional_meeting_name_string()
    {
        AssertOptionalString(DefinitionSchemas.HiringTemplateDefinition.Json);
    }

    private static void AssertOptionalString(string schemaJson)
    {
        var stage = FindStage(JsonNode.Parse(schemaJson)!);

        stage.Should().NotBeNull("the schema describes a stage with an id, a name and a kind");
        var meetingName = stage!["properties"]!["meetingName"]!;
        TypesOf(meetingName).Should().Contain("string");
        (stage["required"] as JsonArray ?? []).Select(n => n!.GetValue<string>()).Should().NotContain("meetingName", "a stage without one is valid");
    }

    // The exporter inlines nested types, so the stage is wherever an object holds both a kind and an id; find it by shape.
    private static JsonObject? FindStage(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties && properties.ContainsKey("kind") && properties.ContainsKey("id") && properties.ContainsKey("slaDays"))
                {
                    return obj;
                }

                return obj.Select(pair => pair.Value).OfType<JsonNode>().Select(FindStage).FirstOrDefault(found => found is not null);
            case JsonArray array:
                return array.OfType<JsonNode>().Select(FindStage).FirstOrDefault(found => found is not null);
            default:
                return null;
        }
    }

    // A nullable string is exported as "string" or as ["string", "null"].
    private static IEnumerable<string> TypesOf(JsonNode schema) => schema["type"] switch
    {
        JsonArray types => types.Select(t => t!.GetValue<string>()),
        JsonValue single => [single.GetValue<string>()],
        _ => [],
    };
}
