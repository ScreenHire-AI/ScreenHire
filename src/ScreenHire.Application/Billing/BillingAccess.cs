using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Billing;

/// <summary>
/// Billing as this host is configured (<see cref="BillingOptions.Mode"/>): always registered, so every endpoint that takes it
/// resolves, and <see cref="IsConfigured"/> false hides billing and limits nothing, as <c>SlackAccess</c> does for Slack.
/// </summary>
public sealed class BillingAccess(IOptions<BillingOptions> options, StripeBillingProvider stripe, SandboxBillingProvider sandbox)
{
    public BillingMode Mode => options.Value.Mode;

    public bool IsConfigured => Mode != BillingMode.Off;

    /// <summary>The provider for <see cref="Mode"/>. Ask <see cref="IsConfigured"/> first: with billing off there is none.</summary>
    public IBillingProvider Provider => Mode switch
    {
        BillingMode.Stripe => stripe,
        BillingMode.Sandbox => sandbox,
        _ => throw new InvalidOperationException("Billing is not set up here (ScreenHire:Billing)."),
    };

    /// <summary>The sandbox, for its own routes. Ask <see cref="Mode"/> first.</summary>
    public SandboxBillingProvider Sandbox => sandbox;
}

/// <summary>
/// The plan or the payment details of an organization changed (Checkout finished, the owner used the portal, a payment
/// failed, the subscription ended), so an open Billing page refetches. Carries the organization only: the page reads the
/// state through the REST API, which applies its own authorization.
/// </summary>
public sealed record BillingChanged(Guid OrganizationId) : ITenantEvent;

public static class BillingRegistration
{
    /// <summary>Billing's services, for the API. The mode is read per call, from <see cref="BillingOptions"/>.</summary>
    public static IServiceCollection AddScreenHireBilling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<BillingOptions>().BindConfiguration(BillingOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingSandboxGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingTrialGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BillingOptions>, BillingAiMarginGate>());
        services.TryAddSingleton<PlanCatalogue>();
        services.TryAddSingleton<StripeBillingProvider>();
        services.TryAddSingleton<SandboxBillingProvider>();
        services.TryAddScoped<BillingAccess>();
        services.TryAddScoped<OpenJobCounter>();
        services.TryAddScoped<BillingService>();
        return services;
    }
}
