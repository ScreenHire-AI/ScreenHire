using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.IntegrationTests;

/// <summary>A response reduced to what the hiring tests assert on: its status and its JSON body, if it had one.</summary>
internal sealed record ApiResult(HttpStatusCode Status, JsonElement Body)
{
    /// <summary>The member at a dotted path such as <c>template.versionId</c> or <c>versions.0.id</c>.</summary>
    public JsonElement At(string path) => Body.At(path);

    public string Text(string path) => At(path).GetString()!;

    public Guid Id(string path = "id") => At(path).GetGuid();

    public int Count(string path = "") => (path.Length == 0 ? Body : At(path)).GetArrayLength();

    /// <summary>The keys of a validation problem's <c>errors</c> object.</summary>
    public IReadOnlyList<string> ProblemKeys() =>
        Body.TryGetProperty("errors", out var errors) ? [.. errors.EnumerateObject().Select(p => p.Name)] : [];

    public ApiResult Expect(HttpStatusCode status)
    {
        Status.Should().Be(status, "the body was {0}", Body.ValueKind == JsonValueKind.Undefined ? "empty" : Body.GetRawText());
        return this;
    }
}

internal static class JsonElementPaths
{
    public static JsonElement At(this JsonElement element, string path)
    {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            element = element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index)
                ? element[index]
                : element.GetProperty(segment);
        }

        return element;
    }
}

/// <summary>
/// A person signed in to their own new organization, with a client for the dashboard API. Requests use the ambient test
/// cancellation token, so a test body stays about the behaviour.
/// </summary>
internal sealed class Tenant(ApiSession session, Guid organizationId, Guid userId) : IDisposable
{
    public ApiSession Session { get; } = session;

    public Guid OrganizationId { get; } = organizationId;

    public Guid UserId { get; } = userId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<ApiResult> GetAsync(string path) => await ReadAsync(await Session.Client.GetAsync(path, Ct));

    public async Task<ApiResult> PostAsync(string path, JsonNode? body = null) =>
        await ReadAsync(await Session.PostAsync(path, body ?? new JsonObject(), Ct));

    public async Task<ApiResult> PutAsync(string path, JsonNode body) => await ReadAsync(await Session.PutAsync(path, body, Ct));

    /// <summary>Creates a job and expects 201.</summary>
    public async Task<ApiResult> CreateJobAsync(JsonObject body) => (await PostAsync("/v1/jobs", body)).Expect(HttpStatusCode.Created);

    public async Task<ApiResult> PublishJobAsync(Guid jobId) => (await PostAsync($"/v1/jobs/{jobId}/publish")).Expect(HttpStatusCode.OK);

    public async Task<ApiResult> CloseJobAsync(Guid jobId) => (await PostAsync($"/v1/jobs/{jobId}/close")).Expect(HttpStatusCode.OK);

    /// <summary>Creates a template with a basic draft version 1 and expects 201.</summary>
    public async Task<ApiResult> CreateTemplateAsync(string name, JsonNode? definition = null)
    {
        var body = new JsonObject { ["name"] = name };
        if (definition is not null)
        {
            body["definition"] = definition;
        }

        return (await PostAsync("/v1/templates", body)).Expect(HttpStatusCode.Created);
    }

    /// <summary>A new template whose version 1 is published. Returns the template and that version's id.</summary>
    public async Task<(Guid TemplateId, Guid VersionId)> CreatePublishedTemplateAsync(string name, JsonNode? definition = null)
    {
        var created = await CreateTemplateAsync(name, definition);
        var templateId = created.Id();
        var published = (await PostAsync($"/v1/templates/{templateId}/versions/1/publish")).Expect(HttpStatusCode.OK);
        return (templateId, published.Id());
    }

    public async Task<ApiResult> CreateApplicationAsync(Guid jobId, string email, string fullName = "Casey Morgan", JsonObject? extra = null)
    {
        var body = new JsonObject { ["candidate"] = new JsonObject { ["fullName"] = fullName, ["email"] = email } };
        foreach (var (key, value) in extra ?? [])
        {
            body[key] = value?.DeepClone();
        }

        return (await PostAsync($"/v1/jobs/{jobId}/applications", body)).Expect(HttpStatusCode.Created);
    }

    public void Dispose() => Session.Dispose();

    private static async Task<ApiResult> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            using var document = text.Length == 0 ? null : JsonDocument.Parse(text);
            return new ApiResult(response.StatusCode, document?.RootElement.Clone() ?? default);
        }
    }
}

internal static class HiringTestExtensions
{
    /// <summary>
    /// Signs a new person in with an emailed link and onboards them into a new organization, as
    /// <c>TenantIsolationTests</c> does. Dispose the result.
    /// </summary>
    public static async Task<Tenant> SignedInWithOrganizationAsync(this AppHostFixture fixture)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = new ApiSession(fixture);
        try
        {
            await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("recruiter"), cancellationToken);
            var organizationId = await session.OnboardAsync(cancellationToken);
            var me = await session.CurrentUserAsync(cancellationToken);
            return new Tenant(session, organizationId, me!.Value.GetProperty("id").GetGuid());
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}

/// <summary>Request bodies, written as a client sends them: camelCase members and string enums.</summary>
internal static class Samples
{
    /// <summary>A complete, publishable remote job. It is also a valid full replacement for a PUT.</summary>
    public static JsonObject RemoteJob(string title) => new()
    {
        ["title"] = title,
        ["description"] = "<p>Build and run the things people rely on.</p>",
        ["employmentType"] = "FullTime",
        ["workplaceType"] = "Remote",
        ["applicantLocationRequirements"] = new JsonArray("GB"),
        ["salary"] = new JsonObject { ["min"] = 50000, ["max"] = 60000, ["currency"] = "GBP", ["period"] = "Year" },
    };

    /// <summary>The default hiring template definition, serialized the way the API and the JSONB columns do.</summary>
    public static JsonObject Definition() =>
        JsonSerializer.SerializeToNode(HiringTemplateDefinition.Default, JsonColumns.SerializerOptions)!.AsObject();

    /// <summary>The definition with these automations.</summary>
    public static JsonObject DefinitionWith(params JsonObject[] automations)
    {
        var definition = Definition();
        definition["workflow"]!["automations"] = new JsonArray([.. automations]);
        return definition;
    }

    /// <summary>An automation that, when an application arrives with <paramref name="tag"/>, sends an email.</summary>
    public static JsonObject EmailOnTag(string tag) => new()
    {
        ["id"] = "email-on-tag",
        ["name"] = "Email on tag",
        ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "application.tags", ["operator"] = "Contains", ["value"] = tag }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendEmail", ["emailTemplate"] = "stage-change" }),
    };

    /// <summary>An automation on a signal fact (<c>integrity.authenticity</c>) whose only action is <paramref name="action"/>.</summary>
    public static JsonObject IntegrityAutomation(JsonObject action) => new()
    {
        ["id"] = "integrity-rule",
        ["name"] = "Integrity rule",
        ["trigger"] = new JsonObject { ["type"] = "IntegrityAssessed" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = "integrity.authenticity", ["operator"] = "EqualTo", ["value"] = "low" }),
        },
        ["actions"] = new JsonArray(action),
    };

    public static JsonObject DefinitionWithRenamedStage(JsonObject definition, string stageId, string name)
    {
        var stage = definition["workflow"]!["stages"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == stageId)!;
        stage["name"] = name;
        return definition;
    }

    public static async Task<JsonObject> DefinitionOfAsync(Tenant tenant, Guid templateId, int version)
    {
        var result = (await tenant.GetAsync($"/v1/templates/{templateId}/versions/{version}")).Expect(HttpStatusCode.OK);
        return JsonNode.Parse(result.At("definition").GetRawText())!.AsObject();
    }
}
