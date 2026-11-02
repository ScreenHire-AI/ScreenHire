using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// The calendar providers as this host is configured (<see cref="CalendarOptions.AvailableProviders"/>), and a valid access
/// token for a member's connection. Always registered, so every handler and endpoint that takes it resolves; a provider
/// that is not configured is not found, as Slack is hidden when it is off.
/// </summary>
/// <remarks>
/// The providers are constructor parameters, not looked up from an <c>IServiceProvider</c>: Wolverine generates handler
/// code and refuses a handler whose dependency locates services at run time (<c>ServiceLocationPolicy.NotAllowed</c>).
/// </remarks>
public sealed class CalendarAccess(
    IOptions<CalendarOptions> options,
    CalendarTokens tokens,
    ScreenHireDbContext db,
    TimeProvider time,
    GoogleCalendarProvider google,
    MicrosoftCalendarProvider microsoft,
    SandboxCalendarProvider sandbox)
{
    /// <summary>A token this close to its expiry is refreshed before it is used, so it cannot lapse mid-call.</summary>
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    public CalendarOptions Options => options.Value;

    /// <summary>The providers a member can connect here.</summary>
    public IReadOnlyList<ICalendarProvider> Providers => [.. Options.AvailableProviders.Select(p => Provider(p)!)];

    public bool IsConfigured => Options.AvailableProviders.Count > 0;

    /// <summary>The provider, or null when this host has not configured it.</summary>
    public ICalendarProvider? Find(CalendarProvider id) => Options.IsAvailable(id) ? Provider(id) : null;

    /// <summary>
    /// A usable session for the member's connection: the provider and an access token that is valid now, refreshed when it
    /// is within <see cref="RefreshWindow"/> of expiring. Null when the member has no connection, the provider is not
    /// configured here, or the connection needs reconnecting (the connection then says so in its
    /// <see cref="CalendarConnection.Problem"/>). A provider that is only down throws <see cref="CalendarApiException"/>,
    /// which a handler lets propagate so its retry policy runs.
    /// </summary>
    public async Task<CalendarSession?> GetSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.CalendarConnections.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken) is { } connection
            ? await GetSessionAsync(connection, cancellationToken)
            : null;

    /// <inheritdoc cref="GetSessionAsync(Guid, CancellationToken)"/>
    public async Task<CalendarSession?> GetSessionAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Find(connection.Provider) is not { } provider || connection.Problem is not null)
        {
            return null;
        }

        if (connection.AccessTokenExpiresAt - time.GetUtcNow() > RefreshWindow)
        {
            return await ReadAsync(connection, provider, cancellationToken);
        }

        // Microsoft rotates the refresh token on every refresh, so two refreshes at once would burn the token the second
        // one holds. The per-connection lock makes the second wait, then find the fresh token and use it.
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"calendar-token:" + connection.Id.ToString("N")}, 0))", cancellationToken);
        await db.Entry(connection).ReloadAsync(cancellationToken);
        if (connection.Problem is not null)
        {
            return null;
        }

        CalendarSession? session;
        if (connection.AccessTokenExpiresAt - time.GetUtcNow() > RefreshWindow)
        {
            session = await ReadAsync(connection, provider, cancellationToken);
        }
        else
        {
            session = await RefreshAsync(connection, provider, cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return session;
    }

    private async Task<CalendarSession?> ReadAsync(CalendarConnection connection, ICalendarProvider provider, CancellationToken cancellationToken)
    {
        if (tokens.Unprotect(connection.OrganizationId, connection.UserId, connection.ProtectedAccessToken) is { } accessToken)
        {
            return new CalendarSession(connection, provider, accessToken);
        }

        await RecordProblemAsync(connection, cancellationToken);
        return null;
    }

    private async Task<CalendarSession?> RefreshAsync(CalendarConnection connection, ICalendarProvider provider, CancellationToken cancellationToken)
    {
        if (tokens.Unprotect(connection.OrganizationId, connection.UserId, connection.ProtectedRefreshToken) is not { Length: > 0 } refreshToken)
        {
            await RecordProblemAsync(connection, cancellationToken);
            return null;
        }

        CalendarGrant grant;
        try
        {
            grant = await provider.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (CalendarApiException e) when (e.Permanent)
        {
            await RecordProblemAsync(connection, cancellationToken);
            return null;
        }

        connection.Refreshed(
            tokens.Protect(connection.OrganizationId, connection.UserId, grant.AccessToken),
            grant.RefreshToken is { } rotated ? tokens.Protect(connection.OrganizationId, connection.UserId, rotated) : null,
            grant.ExpiresAt);
        await db.SaveChangesAsync(cancellationToken);
        return new CalendarSession(connection, provider, grant.AccessToken);
    }

    private async Task RecordProblemAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        connection.RecordProblem(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private ICalendarProvider? Provider(CalendarProvider id) => id switch
    {
        CalendarProvider.Google => google,
        CalendarProvider.Microsoft => microsoft,
        CalendarProvider.Sandbox => sandbox,
        _ => null,
    };
}

/// <summary>A member's connection with its provider and an access token that is valid now. Hold it for one piece of work, not across messages.</summary>
public sealed record CalendarSession(CalendarConnection Connection, ICalendarProvider Provider, string AccessToken);

/// <summary>The route and address form of a provider's id: <c>google</c>, <c>microsoft</c>, <c>sandbox</c>.</summary>
public static class CalendarProviderNames
{
    public static string Slug(CalendarProvider provider) => provider.ToString().ToLowerInvariant();

    public static bool TryParse(string? slug, out CalendarProvider provider) =>
        Enum.TryParse(slug, ignoreCase: true, out provider) && Enum.IsDefined(provider);

    /// <summary>What the sheet calls the provider.</summary>
    public static string DisplayName(CalendarProvider provider) => provider switch
    {
        CalendarProvider.Google => "Google Calendar",
        CalendarProvider.Microsoft => "Outlook calendar",
        CalendarProvider.Sandbox => "Sandbox calendar",
        _ => provider.ToString(),
    };
}

public static class CalendarRegistration
{
    /// <summary>The calendar services, the same in the API and the worker. Which providers are on is read per call, from <see cref="CalendarOptions"/>.</summary>
    public static IServiceCollection AddScreenHireCalendar(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<CalendarOptions>().BindConfiguration(CalendarOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CalendarOptions>, CalendarSandboxGate>());
        services.TryAddSingleton<GoogleCalendarProvider>();
        services.TryAddSingleton<MicrosoftCalendarProvider>();
        services.TryAddScoped<SandboxCalendarProvider>();
        services.TryAddScoped<CalendarAccess>();
        services.TryAddSingleton<CalendarTokens>();
        services.TryAddSingleton<CalendarOAuthState>();
        return services;
    }
}
