using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Declares how to authenticate in the OpenAPI document, so Scalar and the generated client show it: the session cookie
/// on every route that needs a signed-in user, and the API key as an alternative on the hiring API
/// (<see cref="ApiKeyAuthentication.HiringApiPolicy"/>). Public routes declare neither.
/// </summary>
internal static class SecuritySchemes
{
    public const string Session = "session";

    public const string ApiKey = "apiKey";

    public static OpenApiOptions AddScreenHireSecuritySchemes(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Session] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = AuthenticationSetup.SessionCookie,
                Description = "The dashboard's session, from signing in. Changes also need the X-XSRF-TOKEN header.",
            };
            document.Components.SecuritySchemes[ApiKey] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "shk_...",
                Description = "An organization API key, created by an owner or admin. Works on jobs, applications, candidates and templates.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            // Two requirements are alternatives: either one authenticates the request.
            operation.Security ??= [];
            operation.Security.Add(Requirement(Session, context.Document));
            if (metadata.OfType<IAuthorizeData>().Any(a => a.Policy == ApiKeyAuthentication.HiringApiPolicy))
            {
                operation.Security.Add(Requirement(ApiKey, context.Document));
            }

            return Task.CompletedTask;
        });

        return options;
    }

    private static OpenApiSecurityRequirement Requirement(string scheme, OpenApiDocument? document) =>
        new() { [new OpenApiSecuritySchemeReference(scheme, document)] = [] };
}
