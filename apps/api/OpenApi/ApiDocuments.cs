using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Webhooks;

namespace ScreenHire.Api.OpenApi;

/// <summary>
/// The API's two OpenAPI documents (docs/architecture/api-reference.md). <c>v1</c> holds every route and feeds the
/// generated TypeScript client (<c>packages/api-client/openapi.json</c>). <see cref="Public"/> holds only what an
/// organization API key can call, reads like documentation (a title, an introduction, tag groups, a summary on every
/// operation) and carries the webhook events integrators receive; Scalar shows it at <c>/scalar</c>.
/// </summary>
internal static class ApiDocuments
{
    /// <summary>The public document's name: served at <c>/openapi/public.json</c>, written to <c>openapi_public.json</c>.</summary>
    public const string Public = "public";

    public const string Title = "ScreenHire API";

    public const string WebhooksTag = "Webhooks";

    /// <summary>
    /// The tag groups Scalar shows in its sidebar (<c>x-tagGroups</c>), in order. A tag with no operation in the
    /// document is left out, and so is a group left empty: Interviews, Tasks and Team are MCP tools, not key routes.
    /// </summary>
    public static IReadOnlyList<(string Group, string[] Tags)> TagGroups { get; } =
    [
        ("Hiring", ["Jobs", "Applications", "Candidates", "Interviews", "Scorecards", "Tasks", "Reports"]),
        ("Verification", ["Verification"]),
        ("Organization", ["Team", "Templates", "Tags"]),
        ("Webhooks", [WebhooksTag]),
    ];

    /// <summary>What each tag holds, shown above its operations.</summary>
    public static IReadOnlyDictionary<string, string> TagDescriptions { get; } = new Dictionary<string, string>
    {
        ["Jobs"] = "Jobs and their hiring process: create and edit them, publish them to the careers site and job boards, and close them.",
        ["Applications"] = "Applications to a job: add them, read them with their answers and activity, move them between stages, and add notes, tags and tasks.",
        ["Candidates"] = "The people behind applications. A candidate who applied to several jobs is one candidate with several applications.",
        ["Scorecards"] = "Scorecards reviewers fill in at a stage, and requests asking a teammate for one.",
        ["Reports"] = "The hiring report: the headline numbers, the funnel, time to hire and where applications came from.",
        ["Verification"] = "Verification results, for a key an Owner gave the Verification read scope. A level is evidence for a person to review, never a reason to reject anyone on its own; there is no score.",
        ["Templates"] = "Hiring templates a job starts from, the scorecard library and the form library.",
        ["Tags"] = "The organization's shared tags.",
        [WebhooksTag] = "Events ScreenHire sends to your endpoints. Add an endpoint in Settings, Webhooks; every delivery is signed with its secret.",
    };

    public static IServiceCollection AddScreenHireOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options
            .AddSchemaTransformer(DefinitionSchemas.LinkFromOpenApi)
            .AddScreenHireSecuritySchemes()
            .UseUnixLineEndings());
        services.AddOpenApi(Public, options =>
        {
            options.ShouldInclude = description => SecuritySchemes.AcceptsApiKey(description.ActionDescriptor.EndpointMetadata);
            options
                .AddSchemaTransformer(DefinitionSchemas.LinkFromOpenApi)
                .AddScreenHireSecuritySchemes(includeSession: false)
                .AddDocumentTransformer(DescribeAsync)
                .UseUnixLineEndings();
        });
        return services;
    }

    private static async Task DescribeAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo { Title = Title, Version = "v1", Description = Introduction };
        await AddWebhooksAsync(document, context, cancellationToken);

        var used = (document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            .SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .Concat((document.Webhooks?.Values ?? Enumerable.Empty<IOpenApiPathItem>()).SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
            .SelectMany(o => o.Tags ?? new HashSet<OpenApiTagReference>())
            .Select(t => t.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var ordered = TagGroups.SelectMany(g => g.Tags).Where(used.Contains).ToList();
        document.Tags = new HashSet<OpenApiTag>(ordered.Select(name => new OpenApiTag
        {
            Name = name,
            Description = TagDescriptions.GetValueOrDefault(name),
        }));

        var groups = new JsonArray();
        foreach (var (group, tags) in TagGroups)
        {
            var present = tags.Where(used.Contains).ToList();
            if (present.Count > 0)
            {
                groups.Add(new JsonObject { ["name"] = group, ["tags"] = new JsonArray([.. present.Select(t => JsonValue.Create(t))]) });
            }
        }

        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions["x-tagGroups"] = new JsonNodeExtension(groups);
    }

    // One POST per event type, as OpenAPI 3.1 describes requests the API makes rather than receives. The data records
    // are components of their own, so the payloads read like the API's other contracts.
    private static async Task AddWebhooksAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Webhooks ??= new Dictionary<string, IOpenApiPathItem>();
        foreach (var entry in WebhookCatalog.All)
        {
            var dataName = entry.DataType.Name;
            if (document.Components?.Schemas?.ContainsKey(dataName) != true)
            {
                document.AddComponent(dataName, await context.GetOrCreateSchemaAsync(entry.DataType, null, cancellationToken));
            }

            var envelope = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "id", "type", "occurredAt", "organizationId", "data" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["id"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", Description = "The event. The same on every attempt and replay, so use it to ignore a delivery you have handled." },
                    ["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Const = entry.Type },
                    ["occurredAt"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time", Description = "When it happened, in UTC." },
                    ["organizationId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                    ["data"] = new OpenApiSchemaReference(dataName, document),
                },
            };

            var operation = new OpenApiOperation
            {
                OperationId = "on" + string.Concat(entry.Type.Split('.', '_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])),
                Summary = entry.Summary,
                Description = entry.Description + "\n\n" + DeliveryNote,
                Tags = new HashSet<OpenApiTagReference> { new(WebhooksTag, document) },
                Parameters = [.. DeliveryHeaders.Select(h => (IOpenApiParameter)new OpenApiParameter
                {
                    Name = h.Name,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = h.Description,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                })],
                RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = envelope, Example = WebhookSample(entry) },
                    },
                },
                Responses = new OpenApiResponses
                {
                    ["2XX"] = new OpenApiResponse { Description = "Received. Any 2xx answer ends the delivery; anything else, or no answer in time, is tried again." },
                },
            };
            document.Webhooks[entry.Type] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = operation } };
        }
    }

    private static JsonNode? WebhookSample(WebhookCatalogEntry entry) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WebhookCatalog.SampleBody(entry), WebhookCatalog.Json));

    private static readonly (string Name, string Description)[] DeliveryHeaders =
    [
        ("ScreenHire-Signature", "`t={unix seconds},v1={hex}`: the hex HMAC-SHA256 of `{t}.{raw body}`, keyed with the endpoint's signing secret."),
        ("ScreenHire-Event-Id", "The event's id, the same as `id` in the body."),
        ("ScreenHire-Event-Type", "The event's type, the same as `type` in the body."),
        ("ScreenHire-Delivery-Id", "This delivery to this endpoint. A replay is a new delivery of the same event."),
        ("Idempotency-Key", "The event's id again, for frameworks that read this header."),
    ];

    private const string DeliveryNote =
        "Check the `ScreenHire-Signature` header before you trust the body, and refuse a `t` more than five minutes old. "
        + "A delivery that is not answered with a 2xx is tried again after 1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours and 12 hours. "
        + "The same event can arrive more than once: its `id` tells you.";

    /// <summary>The document's introduction, which Scalar shows above the operations. Markdown.</summary>
    internal const string Introduction = """
        The ScreenHire API lets your own systems and AI assistants work with your hiring: jobs, applications, candidates, scorecards, templates, reports and, with its own scope, verification results. This reference holds every route an API key can call. Webhooks tell your systems when something happens, and the MCP server lets an AI assistant use the same data through tools.

        ## Authentication

        Send an organization API key as a bearer token: `Authorization: Bearer shk_...`. An Owner or Admin creates keys in Settings, Integrations, API keys, and sees each key once. A key acts for the organization, not for a person, and every change it makes shows in the activity under the key's name.

        - **Read** lets a key read. Every key has it.
        - **Write** also lets it change hiring data: jobs, applications, notes, tags, tasks and templates.
        - **Verification read** lets it read verification results. Only an Owner can give it.

        AI assistants that sign in with OAuth, such as a custom connector, connect to the MCP server at `/v1/mcp`. Their access tokens work only there.

        ## Errors

        Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`) with a `title` and a `detail` you can show. A request that fails validation gets `400` with an `errors` object: each key names a field and holds its messages. Something that does not exist, or belongs to another organization, is `404`. A change that clashes with the current state, such as an edit made from an old copy, is `409`. A missing or revoked key gets `401`, and a key without the scope a route needs, such as a read-only key asking for a change, gets `403`.

        ## Pagination

        Lists are not paged yet: each list route returns every match in one response, newest first unless its description says otherwise. Paging will come as optional parameters, so code written now keeps working.

        ## Rate limits

        Each key may make 600 requests a minute unless it has a lower limit of its own, counted in fixed one-minute windows. Past the limit the API answers `429 Too Many Requests`; wait for the next minute and try again. Each MCP tool call counts as one request.

        ## Versioning

        The version is in the path: `/v1`. Within a version we only add: new routes, new optional parameters and new members in responses. Ignore members you do not know. A change that would break a client comes as a new version, and the old one keeps working while you move.
        """;
}
