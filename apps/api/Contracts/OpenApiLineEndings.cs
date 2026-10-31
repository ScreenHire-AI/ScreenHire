using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// Gives every description in the OpenAPI document <c>\n</c> line endings. The XML-comment generator joins a doc
/// comment's lines with the build machine's newline, so a Windows build wrote <c>\r\n</c> into the committed
/// <c>packages/api-client/openapi.json</c> and a Linux build (CI, production) wrote <c>\n</c>, and the contract check
/// (<c>pnpm verify:contract</c>) failed on one of them whatever the code said. The generator has no option for it, so
/// this runs once the document is complete, as the last transformer.
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

            return Task.CompletedTask;
        });

    private static void Normalize(IOpenApiSchema schema)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return; // a reference: its target is normalized where it is defined
        }

        if (concrete.Description?.Contains('\r', StringComparison.Ordinal) == true)
        {
            concrete.Description = concrete.Description.ReplaceLineEndings("\n");
        }

        foreach (var property in concrete.Properties?.Values ?? [])
        {
            Normalize(property);
        }
    }
}
