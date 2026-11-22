using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Forms;

/// <summary>Library forms for the form tests (workflow-engine.md section 7, Forms).</summary>
internal static class FormSupport
{
    public static JsonObject Question(string id, string label, string type = "ShortText", bool required = false, params (string Id, string Label)[] options)
    {
        var question = new JsonObject { ["id"] = id, ["type"] = type, ["label"] = label, ["required"] = required };
        if (options.Length > 0)
        {
            question["options"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["label"] = o.Label })]);
        }

        return question;
    }

    public static JsonObject Content(string name, params JsonObject[] questions) =>
        new() { ["name"] = name, ["questions"] = new JsonArray([.. questions]) };

    /// <summary>A new library form with <paramref name="questions"/>, version 1 published.</summary>
    public static async Task<Guid> CreatePublishedFormAsync(Tenant tenant, string name, string kind, params JsonObject[] questions)
    {
        var created = (await tenant.PostAsync("/v1/form-templates", new JsonObject { ["kind"] = kind, ["content"] = Content(name, questions) }))
            .Expect(HttpStatusCode.Created);
        var id = created.Id();
        (await tenant.PostAsync($"/v1/form-templates/{id}/versions/1/publish")).Expect(HttpStatusCode.OK);
        return id;
    }

    /// <summary>
    /// A hiring template definition that copies the library form <paramref name="formId"/> at <paramref name="version"/>
    /// under the follow-up id <paramref name="followUpId"/>. The questions it sends are left empty on purpose: the API takes
    /// them from the library.
    /// </summary>
    public static JsonObject DefinitionUsing(string followUpId, Guid formId, int version, params JsonObject[] automations)
    {
        var definition = Samples.DefinitionWith(automations);
        definition["workflow"]!["followUpQuestionnaires"] = new JsonArray(LinkedFollowUp(followUpId, formId, version));
        return definition;
    }

    public static JsonObject LinkedFollowUp(string followUpId, Guid formId, int version) => new()
    {
        ["id"] = followUpId,
        ["name"] = "Whatever the client says",
        ["questions"] = new JsonArray(Question("placeholder", "A question the library does not have")),
        ["formTemplateId"] = formId,
        ["formVersion"] = version,
    };

    /// <summary>An automation that emails the follow-up <paramref name="followUpId"/> when a candidate enters <paramref name="stageId"/>.</summary>
    public static JsonObject SendOnEntering(string stageId, string followUpId) => new()
    {
        ["id"] = $"send-form-{followUpId}",
        ["name"] = $"Send the form when they enter {stageId}",
        ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = stageId },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendQuestionnaire", ["questionnaireId"] = followUpId }),
    };

    /// <summary>The follow-up questionnaires of a template's version.</summary>
    public static async Task<JsonArray> FollowUpsOfAsync(Tenant tenant, Guid templateId, int version) =>
        (JsonArray)(await Samples.DefinitionOfAsync(tenant, templateId, version))["workflow"]!["followUpQuestionnaires"]!;
}
