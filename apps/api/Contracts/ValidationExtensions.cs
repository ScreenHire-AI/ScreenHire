using System.Text.Json;
using FluentValidation;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.Api.Contracts;

internal static class ValidationExtensions
{
    /// <summary>
    /// Validates <paramref name="request"/> and returns the errors in problem-details form, or null when it is valid.
    /// Keys are camelCase paths, matching the JSON the client sent (<c>salary.currency</c>, <c>locations[0].country</c>),
    /// under <paramref name="prefix"/> when one is given.
    /// </summary>
    public static async Task<IDictionary<string, string[]>?> ProblemsAsync<T>(
        this IValidator<T> validator, T request, CancellationToken cancellationToken, string prefix = "")
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid
            ? null
            : result.ToDictionary().ToDictionary(entry => Join(prefix, CamelCasePath(entry.Key)), entry => entry.Value);
    }

    /// <summary>Definition errors in problem-details form under <paramref name="prefix"/>, or null when there are none.</summary>
    public static IDictionary<string, string[]>? ToProblems(this IReadOnlyList<DefinitionError> errors, string prefix) =>
        errors.Count == 0
            ? null
            : errors.GroupBy(e => Join(prefix, e.Path)).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());

    /// <summary>One problem for one field.</summary>
    public static Dictionary<string, string[]> Problem(string key, string message) => new() { [key] = [message] };

    internal static string CamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    private static string Join(string prefix, string key) =>
        prefix.Length == 0 ? key : key.Length == 0 ? prefix : $"{prefix}.{key}";
}
