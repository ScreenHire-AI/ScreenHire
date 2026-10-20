using System.Text.Json;
using FluentValidation;

namespace ScreenHire.Api.Contracts;

internal static class ValidationExtensions
{
    /// <summary>
    /// Validates <paramref name="request"/> and returns the errors in problem-details form, or null when it is valid.
    /// Keys are camelCase, matching the JSON property names the client sent.
    /// </summary>
    public static async Task<IDictionary<string, string[]>?> ProblemsAsync<T>(
        this IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid
            ? null
            : result.ToDictionary().ToDictionary(entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key), entry => entry.Value);
    }
}
