using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Webhooks;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The public OpenAPI document Scalar shows (docs/architecture/api-reference.md), as the API build wrote it to
/// <c>packages/api-client/openapi_public.json</c>; <c>pnpm verify:contract</c> keeps the file current with the code.
/// Integrators read it as documentation, so every operation has a summary, nothing a key cannot call is in it, and
/// every operation sits under a tag group (Scalar's sidebar shows only grouped tags).
/// </summary>
public sealed class PublicApiDocumentTests
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

    [Fact]
    public void Every_operation_has_a_plain_summary()
    {
        using var document = Load("openapi_public.json");

        var missing = Operations(document.RootElement)
            .Where(o => !o.Operation.TryGetProperty("summary", out var summary) || string.IsNullOrWhiteSpace(summary.GetString()))
            .Select(o => o.Name)
            .ToList();
        var unplain = Operations(document.RootElement)
            .Select(o => (o.Name, Summary: o.Operation.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : ""))
            .Where(o => o.Summary.Length > 0 && (o.Summary.EndsWith('.') || !char.IsUpper(o.Summary[0]) || o.Summary.Length > 60))
            .Select(o => o.Name)
            .ToList();

        Operations(document.RootElement).Should().NotBeEmpty();
        missing.Should().BeEmpty("every route a key can call says what it does: add .WithSummary(...) next to its .WithName(...)");
        unplain.Should().BeEmpty("a summary is a short sentence-case phrase with no full stop");
    }

    [Fact]
    public void It_holds_exactly_the_operations_a_key_can_call()
    {
        using var full = Load("openapi.json");
        using var published = Load("openapi_public.json");

        var withKey = Operations(full.RootElement)
            .Where(o => o.Operation.TryGetProperty("security", out var security)
                && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("apiKey", out _)))
            .Select(o => o.Name);

        Operations(published.RootElement).Select(o => o.Name).Should().BeEquivalentTo(withKey);
        published.RootElement.GetProperty("components").GetProperty("securitySchemes").EnumerateObject().Select(p => p.Name)
            .Should().Equal("apiKey");
        published.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("ScreenHire API");
    }

    [Fact]
    public void Every_tag_in_use_sits_in_one_group_and_no_group_names_a_missing_tag()
    {
        using var document = Load("openapi_public.json");
        var root = document.RootElement;

        var webhooks = root.GetProperty("webhooks").EnumerateObject().Select(w => w.Value.GetProperty("post"));
        var used = Operations(root).Select(o => o.Operation).Concat(webhooks)
            .SelectMany(o => o.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)).ToHashSet();
        var grouped = root.GetProperty("x-tagGroups").EnumerateArray()
            .SelectMany(g => g.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)).ToList();
        var described = root.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();

        grouped.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(used);
        described.Should().BeEquivalentTo(used);
        root.GetProperty("tags").EnumerateArray().Where(t => !t.TryGetProperty("description", out var _)).Should().BeEmpty("every tag says what it holds");
    }

    [Fact]
    public void The_webhooks_section_has_every_event_with_its_sample()
    {
        using var document = Load("openapi_public.json");
        var webhooks = document.RootElement.GetProperty("webhooks");

        webhooks.EnumerateObject().Select(p => p.Name).Should().Equal(WebhookEventTypes.All);
        foreach (var entry in WebhookCatalog.All)
        {
            var operation = webhooks.GetProperty(entry.Type).GetProperty("post");
            operation.GetProperty("summary").GetString().Should().Be(entry.Summary);
            operation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("Webhooks");
            var example = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
            example.GetProperty("type").GetString().Should().Be(entry.Type);
            JsonNode.DeepEquals(JsonNode.Parse(example.GetProperty("data").GetRawText()), JsonSerializer.SerializeToNode(entry.Sample, entry.DataType, WebhookCatalog.Json))
                .Should().BeTrue($"the {entry.Type} example is the catalogue's sample");
            operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString())
                .Should().Contain("ScreenHire-Signature");
        }
    }

    private static JsonDocument Load(string file) =>
        JsonDocument.Parse(File.ReadAllText(RepositoryFiles.PathTo("packages", "api-client", file)));

    private static IEnumerable<(string Name, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject().SelectMany(path => path.Value.EnumerateObject()
            .Where(method => Methods.Contains(method.Name))
            .Select(method => ($"{method.Name.ToUpperInvariant()} {path.Name}", method.Value)));
}
