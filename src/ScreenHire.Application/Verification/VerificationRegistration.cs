using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Verification.Content;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Application.Verification;

public static class VerificationRegistration
{
    /// <summary>
    /// The verification pipeline, its analyzers and their local adapters (<c>ScreenHire:Verification</c>). Every source is
    /// optional; a missing one makes its analyzer record why it saw nothing. Registered by the worker's messaging setup.
    /// A real source registered first (a different <see cref="ITelemetrySource"/>, say) wins.
    /// </summary>
    public static IServiceCollection AddScreenHireVerification(this IServiceCollection services)
    {
        services.AddVerificationOptions();
        // Only the worker reads the IP data files, so only the worker checks them.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VerificationOptions>, NetworkSourceFiles>());

        services.TryAddSingleton<IIpIntelligence, IpIntelligence>();
        services.TryAddSingleton<IMailDomainChecker, DnsMailDomainChecker>();
        services.TryAddSingleton<DisposableEmailDomains>();
        services.TryAddScoped<ITelemetrySource, DatabaseTelemetrySource>();
        services.TryAddScoped<IApplicantDirectory, DatabaseApplicantDirectory>();

        // Content forensics (verification-engine.md §10.10): the worker registers blob storage for the files first; the
        // model provider comes from ScreenHire:Verification:Llm, and the fake model refuses to start outside Development.
        services.TryAddSingleton<IApplicantFileStore, NoApplicantFileStore>();
        services.TryAddScoped<IContentRecords, DatabaseContentRecords>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VerificationOptions>, FakeModelGate>());
        services.TryAddSingleton(provider => LlmAccess.Create(provider.GetRequiredService<IOptions<VerificationOptions>>().Value.Llm));
        services.TryAddSingleton<ConsistencyChecker>();

        // The order analyzers run in. Each is keyed by its own name and version, so adding one never re-runs the others.
        services.AddScoped<IVerificationAnalyzer, NetworkAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, IdentityAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, AutomationAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, DuplicateAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, BehaviouralAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, DocumentAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, AnswerAnalyzer>();
        services.AddScoped<IVerificationAnalyzer, ConsistencyAnalyzer>();
        return services;
    }

    /// <summary>
    /// <see cref="VerificationOptions"/> with the checks every host that reads them applies at startup: the Signal Network
    /// stays off and raw data is kept no longer than the notice says. Used by the worker and by the API's apply flow.
    /// </summary>
    public static IServiceCollection AddVerificationOptions(this IServiceCollection services)
    {
        services.AddOptions<VerificationOptions>().BindConfiguration(VerificationOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VerificationOptions>, SignalNetworkGate>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VerificationOptions>, RetentionLimit>());
        return services;
    }
}

/// <summary>
/// Refuses to start a host with the Signal Network switched on (ADR-0005). Enabling it needs legal review, a DPIA, a
/// controller notice, grants for the application role and the audited query path, none of which exist yet, so the
/// flag being on is a configuration mistake, not a feature.
/// </summary>
public sealed class SignalNetworkGate : IValidateOptions<VerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.SignalNetwork.Enabled
            ? ValidateOptionsResult.Fail(
                "ScreenHire:Verification:SignalNetwork:Enabled is on, but the Signal Network stays off until legal review and DPIA sign-off (ADR-0005).")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Refuses a raw-data retention the careers privacy notice does not allow. The notice tells candidates their network
/// address and browser details are deleted within 30 days, and each application records the notice version it was sent
/// under, so a longer setting would make that record false.
/// </summary>
public sealed class RetentionLimit : IValidateOptions<VerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Retention.RawNetworkDays is >= 1 and <= RetentionOptions.MaxRawNetworkDays
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"ScreenHire:Verification:Retention:RawNetworkDays must be 1 to {RetentionOptions.MaxRawNetworkDays}, the period the careers privacy notice promises.");
    }
}

/// <summary>
/// Refuses to start the worker when a configured IP data file is missing. The files are read on the first lookup, so a
/// wrong path would otherwise fail every verification analysis until the messages dead-letter. Unset paths are fine: that
/// source is simply off.
/// </summary>
public sealed class NetworkSourceFiles : IValidateOptions<VerificationOptions>
{
    public ValidateOptionsResult Validate(string? name, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var missing = new List<string>();
        if (options.Network.Ip2AsnFile is { } ip2Asn && !string.IsNullOrWhiteSpace(ip2Asn) && !File.Exists(ip2Asn))
        {
            missing.Add($"ScreenHire:Verification:Network:Ip2AsnFile ({ip2Asn})");
        }

        if (options.Network.TorExitListFile is { } torExits && !string.IsNullOrWhiteSpace(torExits) && !File.Exists(torExits))
        {
            missing.Add($"ScreenHire:Verification:Network:TorExitListFile ({torExits})");
        }

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(missing.Select(setting => $"{setting} names a file that does not exist."));
    }
}
