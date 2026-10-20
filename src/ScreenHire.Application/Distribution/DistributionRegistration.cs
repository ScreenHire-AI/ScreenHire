using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Application.Distribution.GoogleIndexing;
using ScreenHire.Domain.Distribution;

namespace ScreenHire.Application.Distribution;

/// <summary>Job distribution's services, the same in the API and the worker so both see the same boards.</summary>
public static class DistributionRegistration
{
    public static IHostApplicationBuilder AddJobDistribution(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection(DistributionOptions.Section);
        builder.Services.AddOptions<DistributionOptions>().Bind(section);

        // Every adapter is registered; the registry keeps only the configured ones.
        builder.Services.AddScoped<IJobBoardAdapter, CareersPageAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, GoogleIndexingAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, JobBoardApiAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, GenericFeedAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, CareerjetFeedAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, TalentFeedAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, AdzunaFeedAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, JoobleFeedAdapter>();
        builder.Services.AddScoped<IJobBoardAdapter, SandboxBoardAdapter>();
        builder.Services.AddScoped<BoardRegistry>();
        builder.Services.AddScoped<DistributionPlanner>();

        switch (section.GetSection(nameof(DistributionOptions.GoogleIndexing)).GetValue<GoogleIndexingMode>(nameof(GoogleIndexingOptions.Mode)))
        {
            case GoogleIndexingMode.Google:
                builder.Services.AddHttpClient<IIndexingClient, GoogleIndexingClient>();
                break;
            case GoogleIndexingMode.Local:
                builder.Services.AddSingleton<LocalIndexingClient>();
                builder.Services.AddSingleton<IIndexingClient>(services => services.GetRequiredService<LocalIndexingClient>());
                break;
            default:
                // The board is hidden, so nothing is ever queued; the handler still needs its dependency to compile.
                builder.Services.AddSingleton<IIndexingClient, UnconfiguredIndexingClient>();
                break;
        }

        return builder;
    }

    private sealed class UnconfiguredIndexingClient : IIndexingClient
    {
        public Task NotifyAsync(string url, IndexingNotificationType type, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Google indexing is off (ScreenHire:Distribution:GoogleIndexing:Mode).");
    }
}
