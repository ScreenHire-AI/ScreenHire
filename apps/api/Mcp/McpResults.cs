using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using ModelContextProtocol;
using ScreenHire.Api.Auth;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// Turns what a hiring service answers its route with into what a tool returns: the value, or an
/// <see cref="McpException"/> whose message the assistant reads as the tool's error. The words are the route's own
/// problem details, so a refusal reads the same over HTTP and MCP.
/// </summary>
internal static class McpResults
{
    /// <summary>The value of an <c>Ok</c> or <c>Created</c> result; any other result throws its reason.</summary>
    public static T ValueOf<T>(IResult result, string notFound) => result switch
    {
        IValueHttpResult<T> { Value: { } value } => value,
        NotFound => throw new McpException(notFound),
        ValidationProblem invalid => throw new McpException(string.Join(
            " ", invalid.ProblemDetails.Errors.SelectMany(e => e.Value.Select(message => $"{e.Key}: {message}")))),
        ProblemHttpResult problem => throw new McpException(Describe(problem)),
        _ => throw new InvalidOperationException($"A hiring service answered with {result.GetType().Name}, which no tool expects."),
    };

    /// <summary>
    /// Refuses the call unless the key may use <paramref name="permission"/>, by the same rule as the route filter
    /// (<see cref="HiringAuthorization.KeyRefusal"/>). The write policy has already hidden write tools from a Read key.
    /// </summary>
    public static void Require(ClaimsPrincipal user, Permission permission)
    {
        if (HiringAuthorization.KeyRefusal(user, permission) is { } refused)
        {
            throw new McpException(Describe(refused));
        }
    }

    private static string Describe(ProblemHttpResult problem) =>
        string.Join(": ", new[] { problem.ProblemDetails.Title, problem.ProblemDetails.Detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
