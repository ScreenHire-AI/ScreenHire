using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Contracts;

namespace ScreenHire.Api.Endpoints;

/// <summary>Unauthenticated metadata about the running API, for health pages and support.</summary>
internal static class MetaEndpoints
{
    private const string ProductName = "ScreenHire";

    // The informational version carries the source revision when the SDK knows it ("1.0.0+abc123").
    private static readonly string Version =
        typeof(MetaEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(MetaEndpoints).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/meta").WithTags("Meta").AllowAnonymous();

        group.MapGet("/", GetMeta).WithName("GetMeta");

        return app;
    }

    private static Ok<ApiMeta> GetMeta(IHostEnvironment environment) =>
        TypedResults.Ok(new ApiMeta(ProductName, Version, environment.EnvironmentName));
}
