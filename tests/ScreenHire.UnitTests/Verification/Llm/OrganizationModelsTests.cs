using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.UnitTests.Verification.Llm;

/// <summary>
/// An organization's own model: the key at rest, the address it may use, and the real provider paths (listing models, the
/// connection test) against replayed answers, because the fake model that every other test uses never reaches them.
/// </summary>
public sealed class OrganizationModelsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_key_is_protected_per_organization_and_no_other_organization_reads_it()
    {
        var keys = new LlmKeys(DataProtectionProvider.Create("organization-models-tests"));
        var first = Guid.CreateVersion7();
        var secret = "sk-test-0123456789";

        var protectedKey = keys.Protect(first, secret);

        protectedKey.Should().NotContain(secret);
        keys.Unprotect(first, protectedKey).Should().Be(secret);
        keys.Unprotect(Guid.CreateVersion7(), protectedKey).Should().BeNull("a key copied into another organization's row does not read");
        keys.Unprotect(first, "not protected at all").Should().BeNull("a row that no longer reads is a missing key, never an exception");
    }

    [Theory]
    [InlineData("https://api.openai.com/v1/", null)]
    [InlineData("https://example-resource.openai.azure.com/openai/v1", null)]
    [InlineData("http://api.example.com/v1", "https://")]
    [InlineData("api.example.com", "https://")]
    [InlineData("https://user:password@api.example.com/v1", "password")]
    [InlineData("https://api.example.com/v1?key=abc", "query")]
    [InlineData("", "https://")]
    [InlineData(null, "https://")]
    public void A_base_url_is_an_https_address_with_no_credentials_in_it(string? baseUrl, string? problem)
    {
        var result = OrganizationModels.BaseUrlProblem(baseUrl);

        if (problem is null)
        {
            result.Should().BeNull();
        }
        else
        {
            result.Should().Contain(problem);
        }
    }

    [Fact]
    public async Task The_fake_deployment_answers_for_every_organization_and_refuses_the_key_invalid()
    {
        using var models = new OrganizationModels(Options(LlmProvider.Fake));
        var good = new ModelEndpoint(ModelProvider.Anthropic, null, "any-key", "any-model");
        var bad = good with { ApiKey = OrganizationModels.FakeInvalidKey };

        (await models.TestAsync(good, Ct)).Ok.Should().BeTrue();
        (await models.TestAsync(bad, Ct)).Should().Be(ModelTestResult.Failed(ModelProblem.InvalidKey));
        (await models.ListAsync(good, Ct)).Should().BeEquivalentTo(new ModelListing(true, [FakeConsistencyChatClient.ModelId]));
        (await models.ListAsync(bad, Ct)).Listed.Should().BeFalse();
    }

    [Fact]
    public async Task An_openai_compatible_endpoint_lists_its_models_and_is_tested_with_one_small_request()
    {
        var handler = new Replay(request => request.RequestUri!.AbsolutePath switch
        {
            "/openai/v1/models" => Json("""{"object":"list","data":[{"id":"zeta-2","object":"model","created":1,"owned_by":"x"},{"id":"alpha-1","object":"model","created":1,"owned_by":"x"}]}"""),
            "/openai/v1/chat/completions" => Json("""{"id":"c1","object":"chat.completion","created":1,"model":"alpha-1","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"OK"}}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic), new HttpClient(handler));
        var endpoint = new ModelEndpoint(ModelProvider.OpenAiCompatible, "https://example-resource.example.com/openai/v1/", "sk-test", "alpha-1");

        var listing = await models.ListAsync(endpoint, Ct);
        var test = await models.TestAsync(endpoint, Ct);

        listing.Listed.Should().BeTrue();
        listing.Models.Should().Equal("alpha-1", "zeta-2");
        test.Should().Be(ModelTestResult.Success);
        handler.Requests.Should().OnlyContain(r => r.Authorization == "Bearer sk-test", "the key travels as the provider's own bearer header");
        handler.Requests.Select(r => r.Path).Should().Contain("/openai/v1/chat/completions");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ModelProblem.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, ModelProblem.InvalidKey)]
    [InlineData(HttpStatusCode.NotFound, ModelProblem.ModelNotFound)]
    public async Task A_failed_test_is_a_word_for_the_person_never_the_providers_text(HttpStatusCode status, ModelProblem expected)
    {
        var handler = new Replay(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":{"message":"Incorrect API key provided: sk-test-leak","type":"invalid_request_error"}}""", Encoding.UTF8, "application/json"),
        });
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic), new HttpClient(handler));

        var result = await models.TestAsync(new ModelEndpoint(ModelProvider.OpenAiCompatible, "https://example-resource.example.com/v1/", "sk-test-leak", "m"), Ct);

        result.Should().Be(ModelTestResult.Failed(expected));
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("leak");
    }

    [Fact]
    public async Task A_provider_that_offers_no_list_is_not_a_listing_and_the_person_types_a_model()
    {
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic), new HttpClient(new Replay(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var listing = await models.ListAsync(new ModelEndpoint(ModelProvider.OpenAiCompatible, "https://example-resource.example.com/v1/", "k", null), Ct);

        listing.Should().BeEquivalentTo(new ModelListing(false, []));
    }

    [Fact]
    public async Task An_address_inside_the_network_is_never_reached()
    {
        // The default client connects through the public-only handler, the same one webhooks use: a private address is
        // refused where the connection is made, before anything is sent.
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic));

        var result = await models.TestAsync(new ModelEndpoint(ModelProvider.OpenAiCompatible, "https://10.0.0.1/v1/", "sk-test", "m"), Ct);

        result.Should().Be(ModelTestResult.Failed(ModelProblem.AddressNotAllowed));
    }

    [Fact]
    public void An_own_key_that_no_longer_reads_makes_the_check_unavailable_with_a_reason_to_enter_it_again()
    {
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic));
        var keys = new LlmKeys(DataProtectionProvider.Create("organization-models-tests"));
        var settings = new VerificationSettings
        {
            OrganizationId = Guid.CreateVersion7(),
            LlmSource = LlmSource.OwnKey,
            LlmProvider = ModelProvider.Anthropic,
            LlmModel = "m",
            ProtectedLlmApiKey = "not protected at all",
        };
        var deployment = LlmAccess.Unavailable("deployment");

        var access = models.AccessFor(settings, deployment, keys);

        access.Client.Should().BeNull();
        access.UnavailableReason.Should().Contain("Enter it again");
        models.AccessFor(new VerificationSettings { LlmSource = LlmSource.Included }, deployment, keys).Should().BeSameAs(deployment);
        models.AccessFor(null, deployment, keys).Should().BeSameAs(deployment);
    }

    [Fact]
    public async Task Anthropic_is_asked_for_its_whole_list_of_models()
    {
        string? query = null;
        var handler = new Replay(request =>
        {
            query = request.RequestUri!.Query;
            return Json("""{"data":[{"id":"zeta-2","created_at":"2026-01-01T00:00:00Z","display_name":"Zeta","type":"model"},{"id":"alpha-1","created_at":"2026-01-01T00:00:00Z","display_name":"Alpha","type":"model"}],"first_id":"zeta-2","has_more":false,"last_id":"alpha-1"}""");
        });
        using var models = new OrganizationModels(Options(LlmProvider.Anthropic), new HttpClient(handler));

        var listing = await models.ListAsync(new ModelEndpoint(ModelProvider.Anthropic, null, "key", null), Ct);

        listing.Models.Should().Equal("alpha-1", "zeta-2");
        query.Should().Contain("limit=1000");
    }

    [Fact]
    public async Task An_answer_past_the_limit_fails_and_one_within_it_reads()
    {
        var size = ModelResponseLimit.MaxBytes;
        using var invoker = new HttpMessageInvoker(new ModelResponseLimit(new Replay(request =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size]) })));
        using var fits = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://models.example.com/"), Ct);
        (await fits.Content.ReadAsByteArrayAsync(Ct)).Length.Should().Be(size);

        size++;
        var tooBig = () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://models.example.com/"), Ct);

        await tooBig.Should().ThrowAsync<HttpRequestException>();
    }

    private static IOptions<VerificationOptions> Options(LlmProvider provider) =>
        Microsoft.Extensions.Options.Options.Create(new VerificationOptions { Llm = new LlmOptions { Provider = provider, Timeout = TimeSpan.FromSeconds(5) } });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Replay(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond(request));
        }
    }
}
