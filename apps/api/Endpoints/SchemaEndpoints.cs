using Microsoft.AspNetCore.Http.HttpResults;
using ScreenHire.Api.Hiring;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The hiring definitions' JSON Schemas (<see cref="DefinitionSchemas"/>). Public, like the OpenAPI document: they
/// describe the contract, not any organization's data.
/// </summary>
internal static class SchemaEndpoints
{
    private const string SchemaContentType = "application/schema+json";

    public static IEndpointRouteBuilder MapSchemaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Schemas").AllowAnonymous();

        group.MapGet(DefinitionSchemas.HiringTemplateDefinition.Path, GetHiringTemplateDefinitionSchema)
            .WithName("GetHiringTemplateDefinitionSchema")
            .Produces(StatusCodes.Status200OK, contentType: SchemaContentType);
        group.MapGet(DefinitionSchemas.HiringWorkflow.Path, GetHiringWorkflowSchema)
            .WithName("GetHiringWorkflowSchema")
            .Produces(StatusCodes.Status200OK, contentType: SchemaContentType);
        group.MapGet(DefinitionSchemas.Questionnaire.Path, GetQuestionnaireSchema)
            .WithName("GetQuestionnaireSchema")
            .Produces(StatusCodes.Status200OK, contentType: SchemaContentType);

        return app;
    }

    private static ContentHttpResult GetHiringTemplateDefinitionSchema() => Serve(DefinitionSchemas.HiringTemplateDefinition);

    private static ContentHttpResult GetHiringWorkflowSchema() => Serve(DefinitionSchemas.HiringWorkflow);

    private static ContentHttpResult GetQuestionnaireSchema() => Serve(DefinitionSchemas.Questionnaire);

    private static ContentHttpResult Serve(DefinitionSchema schema) => TypedResults.Text(schema.Json, SchemaContentType);
}
