using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Application.Integrity;

public static class IntegrityRegistration
{
    /// <summary>
    /// The integrity pipeline, its analyzers and their local adapters (<c>ScreenHire:Integrity</c>). Every source is
    /// optional; a missing one makes its analyzer record why it saw nothing. Registered by the worker's messaging setup.
    /// A real source registered first (a different <see cref="ITelemetrySource"/>, say) wins.
    /// </summary>
    public static IServiceCollection AddScreenHireIntegrity(this IServiceCollection services)
    {
        services.AddOptions<IntegrityOptions>().BindConfiguration(IntegrityOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, SignalNetworkGate>());

        services.TryAddSingleton<IIpIntelligence, IpIntelligence>();
        services.TryAddSingleton<IMailDomainChecker, DnsMailDomainChecker>();
        services.TryAddSingleton<DisposableEmailDomains>();
        services.TryAddSingleton<ITelemetrySource, NoTelemetrySource>();
        services.TryAddScoped<IApplicantDirectory, DatabaseApplicantDirectory>();

        // The order analyzers run in. Each is keyed by its own name and version, so adding one never re-runs the others.
        services.AddScoped<IIntegrityAnalyzer, NetworkAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, IdentityAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, AutomationAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, DuplicateAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, BehaviouralAnalyzer>();
        return services;
    }
}
