using ScreenHire.Domain.Verification;

namespace ScreenHire.Application.Verification;

/// <summary>How the provider speaks.</summary>
public enum ModelProtocol
{
    /// <summary>Anthropic's own API, through its official SDK.</summary>
    Anthropic,

    /// <summary>The OpenAI chat API, which most other providers also offer.</summary>
    OpenAi,
}

/// <summary>How the key is sent.</summary>
public enum ModelAuth
{
    /// <summary>Anthropic's <c>x-api-key</c> header.</summary>
    XApiKey,

    /// <summary>An <c>Authorization: Bearer</c> header, which every OpenAI-protocol provider here accepts for an API key.</summary>
    Bearer,
}

/// <summary>
/// What a provider preset fills in for the person: the address and the way the key is sent. The person enters the key; the
/// model list comes from the provider's own models endpoint at run time, never from code.
/// </summary>
/// <param name="Provider">The preset.</param>
/// <param name="Protocol">How the provider speaks.</param>
/// <param name="Auth">How the key is sent.</param>
/// <param name="BaseUrl">The fixed address, or null when the person enters it.</param>
/// <param name="BaseUrlEditable">Whether the person enters the address (Azure OpenAI's resource, or any other endpoint).</param>
/// <param name="ListsModels">Whether the provider's models endpoint lists what the key can use. Azure lists base models, not the deployments a key calls.</param>
public sealed record ModelPreset(ModelProvider Provider, ModelProtocol Protocol, ModelAuth Auth, string? BaseUrl, bool BaseUrlEditable, bool ListsModels);

/// <summary>
/// The provider presets (docs/architecture/ai-usage.md, "Providers"). A fixed address is the provider's public API address,
/// the same for everyone. Model ids are never kept here.
/// </summary>
public static class ModelPresets
{
    /// <summary>The prefix Google puts on the model ids in its models list, which the chat endpoint does not take.</summary>
    private const string GeminiIdPrefix = "models/";

    public static IReadOnlyList<ModelPreset> All { get; } =
    [
        new(ModelProvider.Anthropic, ModelProtocol.Anthropic, ModelAuth.XApiKey, null, false, true),
        new(ModelProvider.OpenAi, ModelProtocol.OpenAi, ModelAuth.Bearer, "https://api.openai.com/v1", false, true),
        new(ModelProvider.OpenRouter, ModelProtocol.OpenAi, ModelAuth.Bearer, "https://openrouter.ai/api/v1", false, true),
        new(ModelProvider.AzureOpenAi, ModelProtocol.OpenAi, ModelAuth.Bearer, null, true, false),
        new(ModelProvider.Gemini, ModelProtocol.OpenAi, ModelAuth.Bearer, "https://generativelanguage.googleapis.com/v1beta/openai", false, true),
        new(ModelProvider.OpenAiCompatible, ModelProtocol.OpenAi, ModelAuth.Bearer, null, true, true),
    ];

    public static ModelPreset For(ModelProvider provider) =>
        All.Single(p => p.Provider == provider);

    /// <summary>
    /// The address to call: the preset's own, or the one the person typed. For Azure OpenAI a bare resource address
    /// (<c>https://name.openai.azure.com</c>) gets the v1 path added. Null when there is none to call.
    /// </summary>
    public static string? ResolveBaseUrl(ModelProvider provider, string? typed)
    {
        var preset = For(provider);
        if (preset.Protocol == ModelProtocol.Anthropic)
        {
            return null;
        }

        if (!preset.BaseUrlEditable)
        {
            return preset.BaseUrl;
        }

        if (OrganizationModels.BaseUrlProblem(typed) is not null)
        {
            return null;
        }

        var trimmed = typed!.Trim().TrimEnd('/');
        if (provider == ModelProvider.AzureOpenAi && new Uri(trimmed).AbsolutePath is "" or "/")
        {
            return trimmed + "/openai/v1";
        }

        return trimmed;
    }

    /// <summary>
    /// What is wrong with the address in a request, in words, or null. A preset with a fixed address takes none (the person
    /// is not asked for one); Anthropic takes none; the others need a valid https address.
    /// </summary>
    public static string? BaseUrlProblem(ModelProvider provider, string? typed)
    {
        var preset = For(provider);
        if (preset.BaseUrlEditable)
        {
            return OrganizationModels.BaseUrlProblem(typed);
        }

        return string.IsNullOrWhiteSpace(typed) ? null : $"{provider} has a fixed address. Leave the address empty.";
    }

    /// <summary>A model id as the chat endpoint takes it: Google's list prefixes ids with <c>models/</c>.</summary>
    public static string ModelIdOf(ModelProvider provider, string listed) =>
        provider == ModelProvider.Gemini && listed.StartsWith(GeminiIdPrefix, StringComparison.Ordinal) ? listed[GeminiIdPrefix.Length..] : listed;
}
