using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using ScreenHire.Application.Automations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Application.Verification;

/// <summary>An organization's own model endpoint as typed into the settings page.</summary>
/// <param name="Provider">Anthropic, or any endpoint that speaks the OpenAI chat API.</param>
/// <param name="BaseUrl">The OpenAI-compatible endpoint; ignored for Anthropic.</param>
/// <param name="ApiKey">The key; never logged.</param>
/// <param name="Model">The model id; null when only listing models.</param>
public sealed record ModelEndpoint(ModelProvider Provider, string? BaseUrl, string ApiKey, string? Model);

/// <summary>Why a model could not be reached. A word for the person, never the provider's own text.</summary>
public enum ModelProblem
{
    InvalidKey,
    ModelNotFound,
    Unreachable,
    AddressNotAllowed,
    Other,
}

public sealed record ModelTestResult(bool Ok, ModelProblem? Problem)
{
    public static ModelTestResult Success { get; } = new(true, null);

    public static ModelTestResult Failed(ModelProblem problem) => new(false, problem);
}

/// <summary>The models a provider lists for a key. <c>Listed</c> is false when it offers no list; the person then types an id.</summary>
public sealed record ModelListing(bool Listed, IReadOnlyList<string> Models);

/// <summary>A host a typed-in model endpoint resolved only to non-public addresses for.</summary>
public sealed class ModelAddressRefusedException(string host)
    : HttpRequestException($"Model host {host} does not resolve to a public address.");

/// <summary>
/// Reads a model endpoint's whole answer into memory, up to <see cref="MaxBytes"/>. The SDKs ask for the headers first, so
/// <see cref="HttpClient.MaxResponseContentBufferSize"/> never applies, and an address someone typed could otherwise answer
/// with gigabytes. Past the limit the call fails as an unreachable provider does.
/// </summary>
public sealed class ModelResponseLimit(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public const int MaxBytes = 4 * 1024 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        try
        {
            await response.Content.LoadIntoBufferAsync(MaxBytes, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Builds the clients for an organization's own model, tests a key and lists a provider's models. Every call goes through
/// <see cref="PublicOnlyHandler"/>, so a base URL an administrator typed can never make ScreenHire reach into its own network.
/// A deployment running the fake model (Development and tests) answers for every organization with the fake: no test needs
/// a real key, and the key <c>invalid</c> fails the test so the failure path can be seen.
/// </summary>
public sealed class OrganizationModels : IDisposable
{
    /// <summary>The key the fake model refuses, so tests and the local demo can see a failed connection.</summary>
    public const string FakeInvalidKey = "invalid";

    private readonly LlmOptions llm;
    private readonly HttpClient http;

    public OrganizationModels(IOptions<VerificationOptions> options)
        : this(options, new HttpClient(new ModelResponseLimit(PublicOnlyHandler.Create(() => false, host => new ModelAddressRefusedException(host))), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // the SDK clients apply LlmOptions.Timeout themselves
        })
    {
    }

    /// <summary>Tests replace the HTTP client, as <see cref="LlmAccess.Create"/> allows, to replay a provider's answers.</summary>
    public OrganizationModels(IOptions<VerificationOptions> options, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        llm = options.Value.Llm;
        this.http = http;
    }

    /// <summary>Is the deployment's model the fake?</summary>
    private bool IsFake => llm.Provider == LlmProvider.Fake;

    /// <summary>The problem with a typed base URL, or null when it is usable: an absolute https address with no credentials in it.</summary>
    public static string? BaseUrlProblem(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "Enter the full address of the endpoint, starting with https://.";
        }

        return uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            ? "The address cannot hold a password, a query or a fragment."
            : null;
    }

    /// <summary>
    /// The access an organization's checks use. ScreenHire's own for <see cref="LlmSource.Included"/>. For an own key, a
    /// client for the organization's provider; unavailable, with a reason for the check's note, when the key or model is
    /// missing or no longer reads. The fake deployment answers with <paramref name="deployment"/> either way.
    /// </summary>
    public LlmAccess AccessFor(VerificationSettings? settings, LlmAccess deployment, LlmKeys keys)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(keys);
        if (settings is not { LlmSource: LlmSource.OwnKey } || IsFake)
        {
            return deployment;
        }

        if (settings.LlmProvider is not { } provider || string.IsNullOrWhiteSpace(settings.LlmModel) || settings.ProtectedLlmApiKey is null)
        {
            return LlmAccess.Unavailable("The organization's own model is not fully set up. Enter the provider, model and key in Verification settings.");
        }

        if (keys.Unprotect(settings.OrganizationId, settings.ProtectedLlmApiKey) is not { } key)
        {
            return LlmAccess.Unavailable("The saved API key could not be read. Enter it again in Verification settings.");
        }

        var endpoint = new ModelEndpoint(provider, settings.LlmBaseUrl, key, settings.LlmModel);
        return Create(endpoint) is { } client
            ? LlmAccess.ForClient(client, settings.LlmModel)
            : LlmAccess.Unavailable("The organization's own model address is not valid.");
    }

    /// <summary>Proves the endpoint works: one tiny request with the key and the model.</summary>
    public async Task<ModelTestResult> TestAsync(ModelEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (IsFake)
        {
            return endpoint.ApiKey == FakeInvalidKey ? ModelTestResult.Failed(ModelProblem.InvalidKey) : ModelTestResult.Success;
        }

        if (string.IsNullOrWhiteSpace(endpoint.Model) || Create(endpoint) is not { } client)
        {
            return ModelTestResult.Failed(ModelProblem.Other);
        }

        try
        {
            // Not disposed: the SDK clients share this service's HttpClient, which a dispose could take with it.
            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                new ChatOptions { ModelId = endpoint.Model, MaxOutputTokens = 16 },
                cancellationToken);
            return ModelTestResult.Success;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return ModelTestResult.Failed(Classify(exception));
        }
    }

    /// <summary>The provider's own list of models for the key. A provider that offers none, or a failure, is not a listing.</summary>
    public async Task<ModelListing> ListAsync(ModelEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (IsFake)
        {
            return endpoint.ApiKey == FakeInvalidKey ? new ModelListing(false, []) : new ModelListing(true, [FakeConsistencyChatClient.ModelId]);
        }

        try
        {
            if (endpoint.Provider == ModelProvider.Anthropic)
            {
                var anthropic = NewAnthropic(endpoint.ApiKey);
                // The API's largest page: the default of 20 can leave out the model someone came for.
                var page = await anthropic.Models.List(new Anthropic.Models.Models.ModelListParams { Limit = 1000 }, cancellationToken);
                return new ModelListing(true, [.. page.Items.Select(m => m.ID).Order(StringComparer.Ordinal)]);
            }

            if (BaseUrlProblem(endpoint.BaseUrl) is not null)
            {
                return new ModelListing(false, []);
            }

            var openai = NewOpenAi(endpoint);
            var models = await openai.GetOpenAIModelClient().GetModelsAsync(cancellationToken);
            return new ModelListing(true, [.. models.Value.Select(m => m.Id).Order(StringComparer.Ordinal)]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new ModelListing(false, []);
        }
    }

    private IChatClient? Create(ModelEndpoint endpoint)
    {
        if (endpoint.Provider == ModelProvider.Anthropic)
        {
            return NewAnthropic(endpoint.ApiKey).AsIChatClient(defaultModelId: null, defaultMaxOutputTokens: llm.MaxOutputTokens);
        }

        return BaseUrlProblem(endpoint.BaseUrl) is not null || string.IsNullOrWhiteSpace(endpoint.Model)
            ? null
            : NewOpenAi(endpoint).GetChatClient(endpoint.Model).AsIChatClient();
    }

    private AnthropicClient NewAnthropic(string apiKey) =>
        new() { ApiKey = apiKey, Timeout = llm.Timeout, MaxRetries = LlmAccess.MaxRetries, HttpClient = http };

    private OpenAIClient NewOpenAi(ModelEndpoint endpoint) =>
        new(new ApiKeyCredential(endpoint.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.BaseUrl!.Trim()),
            Transport = new HttpClientPipelineTransport(http),
            NetworkTimeout = llm.Timeout,
        });

    // Words the failure without echoing anything the provider sent, which can hold the key or the request.
    private static ModelProblem Classify(Exception exception)
    {
        // The HTTP stack wraps what the connect callback throws, so a refused address is looked for in the whole chain first.
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is ModelAddressRefusedException)
            {
                return ModelProblem.AddressNotAllowed;
            }
        }

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case AnthropicUnauthorizedException or AnthropicForbiddenException:
                    return ModelProblem.InvalidKey;
                case AnthropicNotFoundException:
                    return ModelProblem.ModelNotFound;
                case ClientResultException { Status: 401 or 403 }:
                    return ModelProblem.InvalidKey;
                case ClientResultException { Status: 404 }:
                    return ModelProblem.ModelNotFound;
                case AnthropicIOException or HttpRequestException or TaskCanceledException or TimeoutException:
                    return ModelProblem.Unreachable;
            }
        }

        return ModelProblem.Other;
    }

    public void Dispose() => http.Dispose();
}
