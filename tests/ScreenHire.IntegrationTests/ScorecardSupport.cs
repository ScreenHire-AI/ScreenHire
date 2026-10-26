using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Scorecard requests as a client sends them: questions of each answer kind, an organization's own library scorecard,
/// and a job whose interview stage asks for one. Used by the scorecard, rating and library tests.
/// </summary>
internal static class ScorecardSupport
{
    /// <summary>A scorecard question. <paramref name="type"/> is the enum name, such as <c>Rating5</c> or <c>MultiChoice</c>.</summary>
    public static JsonObject Question(string id, string type, string label, bool required = false, params string[] options)
    {
        var question = new JsonObject { ["id"] = id, ["type"] = type, ["label"] = label, ["required"] = required };
        if (options.Length > 0)
        {
            question["options"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject { ["id"] = o, ["label"] = $"Option {o}" })]);
        }

        return question;
    }

    /// <summary>A 1 to 5 rating question the candidate's rating averages.</summary>
    public static JsonObject Rating5(string id, bool required = false) => Question(id, "Rating5", $"Rating {id}", required);

    public static JsonArray Questions(params JsonObject[] questions) => new([.. questions.Select(q => (JsonNode)q.DeepClone())]);

    /// <summary>Adds an own scorecard to the library and returns the key stages name it by.</summary>
    public static async Task<string> CreateScorecardAsync(Tenant tenant, string name, params JsonObject[] questions)
    {
        var created = (await tenant.PostAsync("/v1/scorecard-templates", new JsonObject { ["name"] = name, ["questions"] = Questions(questions) }))
            .Expect(HttpStatusCode.Created);
        return created.Text("id");
    }

    /// <summary>The default hiring template definition with these automations and the library scorecard <paramref name="scorecardKey"/> on <paramref name="stageId"/>.</summary>
    public static JsonObject DefinitionNaming(string stageId, string scorecardKey, params JsonObject[] automations)
    {
        var definition = Samples.DefinitionWith(automations);
        var stage = definition["workflow"]!["stages"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == stageId)!;
        stage["scorecardTemplateId"] = scorecardKey;
        return definition;
    }

    /// <summary>A job made from a new published template whose <paramref name="stageId"/> stage names the library scorecard <paramref name="scorecardKey"/>.</summary>
    public static async Task<Guid> JobNamingAsync(Tenant tenant, string title, string stageId, string scorecardKey, params JsonObject[] automations) =>
        (await JobFromVersionAsync(tenant, title, (await tenant.CreatePublishedTemplateAsync($"{title} template", DefinitionNaming(stageId, scorecardKey, automations))).VersionId))
            .Id();

    public static async Task<ApiResult> JobFromVersionAsync(Tenant tenant, string title, Guid templateVersionId)
    {
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = templateVersionId;
        return await tenant.CreateJobAsync(body);
    }

    /// <summary>A scorecard submission: <paramref name="answers"/> are answer objects built by <see cref="Answer"/>.</summary>
    public static JsonObject Submission(string stageId, string recommendation, params JsonObject[] answers) => new()
    {
        ["stageId"] = stageId,
        ["answers"] = new JsonArray([.. answers.Select(a => (JsonNode)a.DeepClone())]),
        ["recommendation"] = recommendation,
    };

    /// <summary>One answer. Pass the member the question's kind takes: a value, option ids or a rating.</summary>
    public static JsonObject Answer(string questionId, string? value = null, string[]? values = null, int? rating = null)
    {
        var answer = new JsonObject { ["questionId"] = questionId };
        if (value is not null)
        {
            answer["value"] = value;
        }

        if (values is not null)
        {
            answer["values"] = new JsonArray([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);
        }

        if (rating is { } number)
        {
            answer["rating"] = number;
        }

        return answer;
    }

    /// <summary>An answer to one of the built-in Interview scorecard's three 1 to 5 questions (<c>role_skills</c>, <c>problem_solving</c>, <c>work_knowledge</c>).</summary>
    public static JsonObject[] InterviewRatings(int roleSkills, int? problemSolving = null, int? workKnowledge = null) =>
    [
        Answer("role_skills", rating: roleSkills),
        .. problemSolving is { } p ? [Answer("problem_solving", rating: p)] : Array.Empty<JsonObject>(),
        .. workKnowledge is { } w ? [Answer("work_knowledge", rating: w)] : Array.Empty<JsonObject>(),
    ];

    /// <summary>Whether <paramref name="element"/> has <paramref name="name"/> with a value other than null (the API writes absent members as null).</summary>
    public static bool Has(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    public static bool IsNull(this ApiResult result, string path)
    {
        var parent = path.Contains('.') ? result.At(path[..path.LastIndexOf('.')]) : result.Body;
        return !parent.Has(path[(path.LastIndexOf('.') + 1)..]);
    }

    /// <summary>The first message of a validation problem's <paramref name="key"/>.</summary>
    public static string ProblemMessage(this ApiResult result, string key) =>
        result.Body.GetProperty("errors").GetProperty(key)[0].GetString()!;
}
