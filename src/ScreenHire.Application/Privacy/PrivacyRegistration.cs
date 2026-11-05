using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Messaging;

namespace ScreenHire.Application.Privacy;

/// <summary><c>ScreenHire:DataRequests</c>: candidates' requests for a copy of their data or for its erasure (docs/architecture/data-requests.md).</summary>
public sealed class DataRequestOptions
{
    public const string Section = "ScreenHire:DataRequests";

    /// <summary>The blob container the copies of candidates' data are written to.</summary>
    public const string DefaultExportContainer = "data-exports";

    /// <summary>The team's target for answering a request, in days from the day it arrived. A target on the dashboard, not a promise to the candidate.</summary>
    public int DueDays { get; set; } = 30;

    /// <summary>How long the emailed link that opens the choices lasts.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The least time between two such emails to one address, so the form cannot be used to flood an inbox.</summary>
    public TimeSpan LinkCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a copy of a candidate's data can be downloaded before it is deleted.</summary>
    public TimeSpan ExportLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How many requests to the public data request routes one address may make in a minute.</summary>
    public int PermitsPerMinute { get; set; } = 10;

    /// <summary>The blob container for copies of candidates' data (private; the worker writes and deletes, the API issues short read links).</summary>
    public string ExportContainer { get; set; } = DefaultExportContainer;

    /// <summary>The answer target as a span, for <c>DataRequest.Open</c>.</summary>
    public TimeSpan DueIn => TimeSpan.FromDays(DueDays);
}

/// <summary><c>ScreenHire:Retention</c>: how the daily record retention run works (docs/architecture/data-requests.md). The period itself is each organization's setting.</summary>
/// <remarks>Named apart from the verification settings' <c>RetentionOptions</c>, which governs raw network data under <c>ScreenHire:Verification:Retention</c>.</remarks>
public sealed class RecordRetentionOptions
{
    public const string Section = "ScreenHire:Retention";

    /// <summary>The most applications one run deletes for an organization; a full batch schedules the next straight away.</summary>
    public int BatchSize { get; set; } = 200;
}

/// <summary>
/// Where copies of candidates' data are kept: the worker writes them and deletes them with the candidate or after their
/// download window. The blob implementation lives in the worker; <see cref="NoDataExportStore"/> stands in where no
/// storage is configured.
/// </summary>
public interface IDataExportStore
{
    /// <summary>Deletes a copy's blob. A blob that is not there is already gone, not an error.</summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>Stands in for storage where none is configured: there is nothing to delete.</summary>
public sealed class NoDataExportStore : IDataExportStore
{
    public Task DeleteAsync(string blobName, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The name of the container holding copies of candidates' data, as the hosts read it: <c>ScreenHire:DataRequests:ExportContainer</c>.</summary>
public static class DataExportsContainer
{
    public static string From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[$"{DataRequestOptions.Section}:{nameof(DataRequestOptions.ExportContainer)}"] is { Length: > 0 } name
            ? name
            : DataRequestOptions.DefaultExportContainer;
    }
}

/// <summary>
/// Deletes copies of candidates' data from storage: those of an erased candidate, sent in the transaction that erases them
/// so a crash cannot strand them. The blob names are paths, so the message holds them only until the worker has handled it.
/// </summary>
public sealed record EraseDataExports(Guid OrganizationId, IReadOnlyList<string> BlobNames) : ITenantMessage;

public static partial class EraseDataExportsHandler
{
    // Idempotent without a key: deleting a blob that is already gone is a no-op, so a redelivery does no harm.
    public static async Task Handle(
        EraseDataExports message, IDataExportStore exports, ILogger<EraseDataExports> logger, CancellationToken cancellationToken)
    {
        var names = message.BlobNames.Distinct(StringComparer.Ordinal).ToList();
        foreach (var name in names)
        {
            await exports.DeleteAsync(name, cancellationToken);
        }

        LogErased(logger, message.OrganizationId, names.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organization {OrganizationId}: deleted {Copies} copies of an erased candidate's data")]
    private static partial void LogErased(ILogger logger, Guid organizationId, int copies);
}

public static class PrivacyRegistration
{
    /// <summary>
    /// The options of data requests and record retention, and the shared erasure service. Registered by the API and by the
    /// worker's messaging setup, which also registers the copy store (<see cref="IDataExportStore"/>).
    /// </summary>
    public static IServiceCollection AddScreenHirePrivacy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<DataRequestOptions>().BindConfiguration(DataRequestOptions.Section);
        services.AddOptions<RecordRetentionOptions>().BindConfiguration(RecordRetentionOptions.Section);
        services.TryAddScoped<CandidateErasure>();
        return services;
    }
}
