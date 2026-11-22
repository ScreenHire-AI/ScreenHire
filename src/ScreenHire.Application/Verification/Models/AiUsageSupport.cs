using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Application.Verification;

/// <summary>Builds what the usage ledger records about a check's calls, from the organization's settings (docs/architecture/ai-usage.md).</summary>
public static class AiUsageContexts
{
    /// <summary>
    /// The context for calls made under <paramref name="settings"/>. The key source, provider and prices come from the
    /// settings, never from the client that answered: under the fake model an organization with its own key still records
    /// own-key calls, priced by its own setting. ScreenHire's key is priced from the deployment's configured prices.
    /// </summary>
    public static AiUsageContext For(VerificationSettings? settings, LlmOptions llm, Guid organizationId, Guid? applicationId)
    {
        ArgumentNullException.ThrowIfNull(llm);
        if (settings is { UsesOwnKey: true })
        {
            var own = (settings.OwnKeyInputPrice, settings.OwnKeyOutputPrice);
            return new AiUsageContext(
                organizationId, applicationId, settings.LlmProvider?.ToString() ?? "Unknown", AiKeySource.OwnKey, _ => own);
        }

        return new AiUsageContext(
            organizationId, applicationId, llm.Provider.ToString(), AiKeySource.BuiltIn,
            model => llm.Prices.TryGetValue(model, out var price) ? (price.Input, price.Output) : (null, null));
    }
}
