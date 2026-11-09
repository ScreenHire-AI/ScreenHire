using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.UnitTests.Verification.Llm;

/// <summary>
/// The provider presets (an address and a way to send the key, filled in for the person) against replayed answers, and the
/// usage ledger's recorder around a model client. No test needs a key or the internet.
/// </summary>
public sealed class ModelPresetsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ModelProvider.OpenAi, "api.openai.com", "/v1")]
    [InlineData(ModelProvider.OpenRouter, "openrouter.ai", "/api/v1")]
    [InlineData(ModelProvider.Gemini, "generativelanguage.googleapis.com", "/v1beta/openai")]
    public void A_preset_with_a_fixed_address_fills_it_in_and_ignores_anything_typed(ModelProvider provider, string host, string path)
    {
        var preset = ModelPresets.For(provider);

        preset.BaseUrlEditable.Should().BeFalse();
        ModelPresets.ResolveBaseUrl(provider, null).Should().Be($"https://{host}{path}");
        ModelPresets.ResolveBaseUrl(provider, "https://elsewhere.example.com/v1").Should().Be($"https://{host}{path}", "a typed address never reroutes a preset");
        ModelPresets.BaseUrlProblem(provider, null).Should().BeNull();
        ModelPresets.BaseUrlProblem(provider, "https://elsewhere.example.com/v1").Should().Contain("fixed address");
    }

    [Fact]
    public void Azure_and_Other_take_the_address_the_person_types_and_Azure_gets_the_v1_path_added_to_a_bare_resource()
    {
        ModelPresets.BaseUrlProblem(ModelProvider.AzureOpenAi, null).Should().Contain("https://");
        ModelPresets.BaseUrlProblem(ModelProvider.OpenAiCompatible, "http://insecure.example.com").Should().Contain("https://");
        ModelPresets.ResolveBaseUrl(ModelProvider.AzureOpenAi, "https://acme.openai.azure.com/").Should().Be("https://acme.openai.azure.com/openai/v1");
        ModelPresets.ResolveBaseUrl(ModelProvider.AzureOpenAi, "https://acme.openai.azure.com/openai/v1/").Should().Be("https://acme.openai.azure.com/openai/v1");
        ModelPresets.ResolveBaseUrl(ModelProvider.OpenAiCompatible, "https://llm.example.com/v1/").Should().Be("https://llm.example.com/v1");
        ModelPresets.ResolveBaseUrl(ModelProvider.OpenAiCompatible, null).Should().BeNull();
    }

    [Fact]
    public void Every_provider_has_exactly_one_preset_and_Anthropic_alone_speaks_its_own_protocol()
    {
        ModelPresets.All.Select(p => p.Provider).Should().BeEquivalentTo(Enum.GetValues<ModelProvider>());
        ModelPresets.All.Where(p => p.Protocol == ModelProtocol.Anthropic).Select(p => p.Provider).Should().Equal(ModelProvider.Anthropic);
        ModelPresets.All.Where(p => p.Auth == ModelAuth.XApiKey).Select(p => p.Provider).Should().Equal(ModelProvider.Anthropic);
    }

    [Theory]
    [InlineData(ModelProvider.OpenAi, "/v1/models", "/v1/chat/completions")]
    [InlineData(ModelProvider.OpenRouter, "/api/v1/models", "/api/v1/chat/completions")]
    [InlineData(ModelProvider.Gemini, "/v1beta/openai/models", "/v1beta/openai/chat/completions")]
    public async Task A_fixed_preset_lists_and_tests_at_its_own_address_with_the_key_as_a_bearer_header(ModelProvider provider, string models, string chat)
    {
        var handler = new Replay(request => request.RequestUri!.AbsolutePath == models
            ? Json("""{"object":"list","data":[{"id":"models/m-2","object":"model","created":1,"owned_by":"x"},{"id":"m-1","object":"model","created":1,"owned_by":"x"}]}""")
            : request.RequestUri.AbsolutePath == chat ? Json(ChatAnswer("m-1", 5, 1)) : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = new OrganizationModels(Options(), new HttpClient(handler));
        var endpoint = new ModelEndpoint(provider, null, "sk-test", "m-1");

        var listing = await service.ListAsync(endpoint, Ct);
        var test = await service.TestAsync(endpoint, Ct);

        listing.Listed.Should().BeTrue();
        listing.Models.Should().Equal(provider == ModelProvider.Gemini ? ["m-1", "m-2"] : ["m-1", "models/m-2"], "Google's models/ prefix is dropped, since the chat endpoint does not take it");
        test.Should().Be(ModelTestResult.Success);
        handler.Requests.Select(r => r.Path).Should().Contain([models, chat]);
        handler.Requests.Should().OnlyContain(r => r.Authorization == "Bearer sk-test");
    }

    [Fact]
    public async Task Azure_OpenAI_is_called_at_the_v1_path_of_the_organizations_own_resource_with_the_deployment_as_the_model()
    {
        var handler = new Replay(request => request.RequestUri!.AbsolutePath == "/openai/v1/chat/completions"
            ? Json(ChatAnswer("my-deployment", 5, 1))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = new OrganizationModels(Options(), new HttpClient(handler));

        var test = await service.TestAsync(new ModelEndpoint(ModelProvider.AzureOpenAi, "https://acme.openai.azure.com/", "azure-key", "my-deployment"), Ct);

        test.Should().Be(ModelTestResult.Success);
        handler.Requests.Single().Authorization.Should().Be("Bearer azure-key");
    }

    [Fact]
    public async Task Azure_OpenAI_is_not_asked_for_a_list_because_its_models_are_not_the_deployments_a_key_calls()
    {
        var handler = new Replay(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = new OrganizationModels(Options(), new HttpClient(handler));

        var listing = await service.ListAsync(new ModelEndpoint(ModelProvider.AzureOpenAi, "https://acme.openai.azure.com/", "k", null), Ct);

        listing.Listed.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_fake_deployment_answers_for_every_preset_so_no_test_needs_a_key()
    {
        using var service = new OrganizationModels(Options(LlmProvider.Fake));

        foreach (var provider in Enum.GetValues<ModelProvider>())
        {
            var endpoint = new ModelEndpoint(provider, "https://example.com/v1", "any-key", "any-model");
            (await service.TestAsync(endpoint, Ct)).Ok.Should().BeTrue(provider.ToString());
            (await service.ListAsync(endpoint, Ct)).Listed.Should().BeTrue(provider.ToString());
        }
    }

    [Fact]
    public async Task The_recorder_writes_one_row_per_call_with_the_providers_id_the_tokens_the_price_and_the_key_it_was_told_of()
    {
        var recorder = new AiUsageRecorder(
            new AiUsageContext(Guid.CreateVersion7(), Guid.CreateVersion7(), "OpenRouter", AiKeySource.OwnKey, _ => (3m, 15m)),
            new FixedTime(new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero)));
        var client = recorder.Wrap(new FakeConsistencyChatClient());

        AiUsageStep.Set(AiUsageChecks.ConsistencyExtraction);
        await client.GetResponseAsync([new ChatMessage(ChatRole.System, "system text that is long enough"), new ChatMessage(ChatRole.User, "user text")], new ChatOptions { ModelId = "asked" }, Ct);

        var call = recorder.Calls.Should().ContainSingle().Subject;
        call.KeySource.Should().Be(AiKeySource.OwnKey, "the settings decide whose key paid, never the client that answered");
        call.Provider.Should().Be("OpenRouter");
        call.CheckName.Should().Be(AiUsageChecks.ConsistencyExtraction);
        call.Model.Should().Be(FakeConsistencyChatClient.ModelId, "the model the provider reported, else the one asked for");
        call.ProviderRequestId.Should().StartWith(FakeConsistencyChatClient.RequestIdPrefix);
        call.InputTokens.Should().BeGreaterThan(0);
        call.Cost.Should().BeGreaterThan(0m);
        call.CostBasis.Should().Be(AiCostBasis.Priced);
        call.Outcome.Should().Be(AiCallOutcome.Ok);
        call.OccurredAt.Should().Be(new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_call_that_fails_is_recorded_with_its_kind_and_never_its_text_and_the_failure_still_reaches_the_caller()
    {
        var recorder = new AiUsageRecorder(
            new AiUsageContext(Guid.CreateVersion7(), null, "Anthropic", AiKeySource.BuiltIn, _ => (null, null)), TimeProvider.System);
        var client = recorder.Wrap(new Throwing(new InvalidOperationException("the request held the candidate's resume")));

        await FluentActions.Awaiting(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], new ChatOptions { ModelId = "m" }, Ct))
            .Should().ThrowAsync<InvalidOperationException>();

        var call = recorder.Calls.Should().ContainSingle().Subject;
        call.Outcome.Should().Be(AiCallOutcome.Error);
        call.ErrorKind.Should().Be(nameof(InvalidOperationException));
        call.CostBasis.Should().Be(AiCostBasis.Unknown);
        call.Cost.Should().BeNull();
        call.ProviderRequestId.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(call).Should().NotContain("resume");
    }

    [Fact]
    public async Task A_timeout_the_client_applied_is_a_timed_out_call_not_an_error()
    {
        var recorder = new AiUsageRecorder(
            new AiUsageContext(Guid.CreateVersion7(), null, "Anthropic", AiKeySource.BuiltIn, _ => (null, null)), TimeProvider.System);
        var client = recorder.Wrap(new Throwing(new TaskCanceledException("timed out", new TimeoutException())));

        await FluentActions.Awaiting(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], new ChatOptions { ModelId = "m" }, Ct))
            .Should().ThrowAsync<TaskCanceledException>();

        recorder.Calls.Should().ContainSingle().Which.Outcome.Should().Be(AiCallOutcome.TimedOut);
    }

    [Fact]
    public async Task A_cost_the_provider_reported_beats_the_configured_price()
    {
        var recorder = new AiUsageRecorder(
            new AiUsageContext(Guid.CreateVersion7(), null, "OpenRouter", AiKeySource.OwnKey, _ => (3m, 15m)), TimeProvider.System);
        var client = recorder.Wrap(new ReportingCost(0.5m));

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], new ChatOptions { ModelId = "m" }, Ct);

        var call = recorder.Calls.Should().ContainSingle().Subject;
        call.Cost.Should().Be(0.5m);
        call.CostBasis.Should().Be(AiCostBasis.Reported);
    }

    [Fact]
    public async Task The_connection_test_of_an_own_key_is_recorded_when_a_recorder_is_given()
    {
        var handler = new Replay(request => request.RequestUri!.AbsolutePath == "/v1/chat/completions"
            ? Json(ChatAnswer("m-1", 12, 2))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = new OrganizationModels(Options(), new HttpClient(handler));
        var recorder = new AiUsageRecorder(
            new AiUsageContext(Guid.CreateVersion7(), null, "OpenAi", AiKeySource.OwnKey, _ => (1m, 2m)), TimeProvider.System);

        var result = await service.TestAsync(new ModelEndpoint(ModelProvider.OpenAi, null, "sk", "m-1"), Ct, recorder);

        result.Ok.Should().BeTrue();
        var call = recorder.Calls.Should().ContainSingle().Subject;
        call.CheckName.Should().Be(AiUsageChecks.ModelTest);
        call.InputTokens.Should().Be(12);
        call.OutputTokens.Should().Be(2);
    }

    [Fact]
    public void Model_prices_are_configuration_and_a_negative_one_is_refused()
    {
        var options = new VerificationOptions();
        options.Llm.Prices["model-a"] = new ModelPrice { Input = 3m, Output = 15m };
        new LlmLimitsCheck().Validate(null, options).Succeeded.Should().BeTrue();

        options.Llm.Prices["model-b"] = new ModelPrice { Input = -1m };
        new LlmLimitsCheck().Validate(null, options).Failed.Should().BeTrue();
    }

    private static IOptions<VerificationOptions> Options(LlmProvider provider = LlmProvider.Anthropic) =>
        Microsoft.Extensions.Options.Options.Create(new VerificationOptions { Llm = new LlmOptions { Provider = provider } });

    private static string ChatAnswer(string model, int input, int output) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"" + model
        + "\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}],\"usage\":{\"prompt_tokens\":" + input
        + ",\"completion_tokens\":" + output + ",\"total_tokens\":" + (input + output) + "}}";

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

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Throwing(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // Answers like OpenRouter does: the cost of the call is written into the slot the HTTP handler would have filled.
    private sealed class ReportingCost(decimal cost) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ReportedCost.Current!.Value = cost;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK"))
            {
                ModelId = "m",
                ResponseId = "gen-1",
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
