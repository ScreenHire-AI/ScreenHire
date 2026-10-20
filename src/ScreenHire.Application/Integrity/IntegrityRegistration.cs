using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Integrity.Content;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Integrity.Llm;

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
        services.AddIntegrityOptions();
        // Only the worker reads the IP data files, so only the worker checks them.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, NetworkSourceFiles>());

        services.TryAddSingleton<IIpIntelligence, IpIntelligence>();
        services.TryAddSingleton<IMailDomainChecker, DnsMailDomainChecker>();
        services.TryAddSingleton<DisposableEmailDomains>();
        services.TryAddSingleton<ITelemetrySource, NoTelemetrySource>();
        services.TryAddScoped<IApplicantDirectory, DatabaseApplicantDirectory>();

        // Content forensics (integrity-engine.md §10.10): the worker registers blob storage for the files first; the
        // model provider comes from ScreenHire:Integrity:Llm, and the fake model refuses to start outside Development.
        services.TryAddSingleton<IApplicantFileStore, NoApplicantFileStore>();
        services.TryAddScoped<IContentRecords, DatabaseContentRecords>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, FakeModelGate>());
        services.TryAddSingleton(provider => LlmAccess.Create(provider.GetRequiredService<IOptions<IntegrityOptions>>().Value.Llm));
        services.TryAddSingleton<ConsistencyChecker>();

        // The order analyzers run in. Each is keyed by its own name and version, so adding one never re-runs the others.
        services.AddScoped<IIntegrityAnalyzer, NetworkAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, IdentityAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, AutomationAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, DuplicateAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, BehaviouralAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, DocumentAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, AnswerAnalyzer>();
        services.AddScoped<IIntegrityAnalyzer, ConsistencyAnalyzer>();
        return services;
    }

    /// <summary>
    /// <see cref="IntegrityOptions"/> with the checks every host that reads them applies at startup: the Signal Network
    /// stays off and raw data is kept no longer than the notice says. Used by the worker and by the API's apply flow.
    /// </summary>
    public static IServiceCollection AddIntegrityOptions(this IServiceCollection services)
    {
        services.AddOptions<IntegrityOptions>().BindConfiguration(IntegrityOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, SignalNetworkGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, RetentionLimit>());
        return services;
    }
}

/// <summary>
/// Refuses to start a host with the Signal Network switched on (ADR-0005). Enabling it needs legal review, a DPIA, a
/// controller notice, grants for the application role and the audited query path, none of which exist yet, so the
/// flag being on is a configuration mistake, not a feature.
/// </summary>
public sealed class SignalNetworkGate : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.SignalNetwork.Enabled
            ? ValidateOptionsResult.Fail(
                "ScreenHire:Integrity:SignalNetwork:Enabled is on, but the Signal Network stays off until legal review and DPIA sign-off (ADR-0005).")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Refuses a raw-data retention the careers privacy notice does not allow. The notice tells candidates their network
/// address and browser details are deleted within 30 days, and each application records the notice version it was sent
/// under, so a longer setting would make that record false.
/// </summary>
public sealed class RetentionLimit : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Retention.RawNetworkDays is >= 1 and <= RetentionOptions.MaxRawNetworkDays
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"ScreenHire:Integrity:Retention:RawNetworkDays must be 1 to {RetentionOptions.MaxRawNetworkDays}, the period the careers privacy notice promises.");
    }
}

/// <summary>
/// Refuses to start the worker when a configured IP data file is missing. The files are read on the first lookup, so a
/// wrong path would otherwise fail every integrity analysis until the messages dead-letter. Unset paths are fine: that
/// source is simply off.
/// </summary>
public sealed class NetworkSourceFiles : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var missing = new List<string>();
        if (options.Network.Ip2AsnFile is { } ip2Asn && !string.IsNullOrWhiteSpace(ip2Asn) && !File.Exists(ip2Asn))
        {
            missing.Add($"ScreenHire:Integrity:Network:Ip2AsnFile ({ip2Asn})");
        }

        if (options.Network.TorExitListFile is { } torExits && !string.IsNullOrWhiteSpace(torExits) && !File.Exists(torExits))
        {
            missing.Add($"ScreenHire:Integrity:Network:TorExitListFile ({torExits})");
        }

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(missing.Select(setting => $"{setting} names a file that does not exist."));
    }
}
