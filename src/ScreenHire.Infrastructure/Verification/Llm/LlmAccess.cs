using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Infrastructure.Verification.Llm;

/// <summary>
/// The configured model provider behind <see cref="IChatClient"/> (<see cref="LlmOptions"/>), or none. Anthropic's
/// official SDK is used only when a key is configured; the deterministic fake answers in Development and tests; with
/// neither, every check is recorded as unavailable. The rest of the code sees only <see cref="IChatClient"/>.
/// </summary>
public sealed class LlmAccess
{
    private LlmAccess(IChatClient? client, string extractionModel, string consistencyModel, string unavailableReason)
    {
        Client = client;
        ExtractionModel = extractionModel;
        ConsistencyModel = consistencyModel;
        UnavailableReason = unavailableReason;
    }

    /// <summary>
    /// Retries the SDK makes after a failed call (its own default, two), set explicitly so the longest a check can take
    /// is known (see <see cref="ConsistencyChecker.MaxCalls"/>) and a claim is never taken over while it is still working.
    /// </summary>
    public const int MaxRetries = 2;

    /// <summary>The client, or null when no model is configured.</summary>
    public IChatClient? Client { get; }

    public string ExtractionModel { get; }

    public string ConsistencyModel { get; }

    /// <summary>Why <see cref="Client"/> is null, for the check's note.</summary>
    public string UnavailableReason { get; }

    public static LlmAccess Unavailable(string reason) => new(null, "", "", reason);

    /// <summary>Builds the access <paramref name="options"/> describe. <paramref name="http"/> replaces the SDK's HTTP client in tests.</summary>
    public static LlmAccess Create(LlmOptions options, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        switch (options.Provider)
        {
            case LlmProvider.Fake:
                // The fake answers as itself whatever models are configured, so no record claims a real model answered.
                return new LlmAccess(new FakeConsistencyChatClient(), FakeConsistencyChatClient.ModelId, FakeConsistencyChatClient.ModelId, "");
            case LlmProvider.Anthropic when !string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)
                && !string.IsNullOrWhiteSpace(options.ExtractionModel) && !string.IsNullOrWhiteSpace(options.ConsistencyModel):
                // The configured key, set explicitly, takes precedence over anything the SDK could find in the environment.
                var anthropic = http is null
                    ? new AnthropicClient { ApiKey = options.Anthropic.ApiKey, Timeout = options.Timeout, MaxRetries = MaxRetries }
                    : new AnthropicClient { ApiKey = options.Anthropic.ApiKey, Timeout = options.Timeout, MaxRetries = MaxRetries, HttpClient = http };

                return new LlmAccess(
                    anthropic.AsIChatClient(defaultModelId: null, defaultMaxOutputTokens: options.MaxOutputTokens),
                    options.ExtractionModel!,
                    options.ConsistencyModel!,
                    "");
            case LlmProvider.Anthropic:
                return Unavailable("No language model is configured: the Anthropic provider needs an API key and both model ids.");
            default:
                return Unavailable("No language model is configured.");
        }
    }
}

/// <summary>
/// Refuses to start a host with the fake model outside Development: its answers are made up, and real candidates must
/// never be assessed with them.
/// </summary>
public sealed class FakeModelGate(IHostEnvironment environment) : IValidateOptions<VerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Llm.Provider == LlmProvider.Fake && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail("ScreenHire:Verification:Llm:Provider is Fake outside Development. The fake model is for local runs and tests only.")
            : ValidateOptionsResult.Success;
    }
}
