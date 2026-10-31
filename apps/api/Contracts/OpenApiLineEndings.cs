using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// Gives every description in the OpenAPI document <c>\n</c> line endings. The XML-comment generator joins a doc
/// comment's lines with the build machine's newline, so a Windows build wrote <c>\r\n</c> into the committed
/// <c>packages/api-client/openapi.json</c> and a Linux build (CI, production) wrote <c>\n</c>, and the contract check
/// (<c>pnpm verify:contract</c>) failed on one of them whatever the code said. The generator has no option for it, so
/// this runs once the document is complete, as the last transformer. It covers the component schemas, the schemas
/// nested in them, and each operation's summary, description, parameters and responses.
/// </summary>
internal static class OpenApiLineEndings
{
    public static OpenApiOptions UseUnixLineEndings(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            foreach (var schema in document.Components?.Schemas?.Values ?? [])
            {
                Normalize(schema);
            }

            foreach (var path in document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            {
                foreach (var operation in path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
                {
                    Normalize(operation);
                }
            }

            return Task.CompletedTask;
        });

    private static void Normalize(OpenApiOperation operation)
    {
        operation.Summary = Lf(operation.Summary);
        operation.Description = Lf(operation.Description);
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (parameter is OpenApiParameter concrete)
            {
                concrete.Description = Lf(concrete.Description);
                Normalize(concrete.Schema);
            }
        }

        foreach (var response in operation.Responses?.Values ?? Enumerable.Empty<IOpenApiResponse>())
        {
            if (response is OpenApiResponse concrete)
            {
                concrete.Description = Lf(concrete.Description);
            }
        }
    }

    private static void Normalize(IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return; // none, or a reference: its target is normalized where it is defined
        }

        concrete.Description = Lf(concrete.Description);
        foreach (var property in concrete.Properties?.Values ?? [])
        {
            Normalize(property);
        }

        Normalize(concrete.Items);
        Normalize(concrete.AdditionalProperties);
        foreach (var part in (concrete.AllOf ?? []).Concat(concrete.OneOf ?? []).Concat(concrete.AnyOf ?? []))
        {
            Normalize(part);
        }
    }

    private static string? Lf(string? text) =>
        text?.Contains('\r', StringComparison.Ordinal) == true ? text.ReplaceLineEndings("\n") : text;
}
