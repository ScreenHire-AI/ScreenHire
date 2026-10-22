using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;

namespace ScreenHire.Api.Auth;

/// <summary>
/// Cross-site request forgery protection for cookie-authenticated JSON endpoints: SameSite=Lax cookies plus
/// ASP.NET Core antiforgery with the request token in a header. The token reaches the dashboard in the readable
/// <see cref="RequestTokenCookie"/> cookie, and the API client echoes it in <see cref="HeaderName"/> on every
/// non-GET request.
/// </summary>
/// <remarks>
/// ASP.NET Core's antiforgery middleware validates form posts only, so JSON endpoints opt in with
/// <see cref="RequireAntiforgeryToken"/>, which calls the same <see cref="IAntiforgery"/> service.
/// </remarks>
internal static class Antiforgery
{
    public const string HeaderName = "X-XSRF-TOKEN";

    public const string RequestTokenCookie = "XSRF-TOKEN";

    /// <summary>
    /// Issues a request token for <see cref="HttpContext.User"/> as it is now. Tokens are bound to the user, so call this
    /// again after signing someone in or out.
    /// </summary>
    public static void IssueRequestToken(HttpContext context)
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        var scheme = context.RequestServices.GetRequiredService<IOptions<DomainOptions>>().Value.Scheme;
        context.Response.Cookies.Append(RequestTokenCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // the dashboard reads it to send the header
            SameSite = SameSiteMode.Strict,
            // From configuration, like the other cookies: behind a TLS-terminating proxy the request itself is http.
            Secure = scheme == "https",
            Path = "/",
        });
    }

    /// <summary>
    /// Rejects a request without a valid antiforgery header with 400 problem details. A request an API key authenticated
    /// passes: it carries no cookie a forged cross-site request could ride on, and browsers cannot add an
    /// <c>Authorization</c> header to one.
    /// </summary>
    public static TBuilder RequireAntiforgeryToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            if (invocation.HttpContext.User.IsApiKey())
            {
                return await next(invocation);
            }

            var antiforgery = invocation.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            return await antiforgery.IsRequestValidAsync(invocation.HttpContext)
                ? await next(invocation)
                : TypedResults.Problem(
                    title: "Missing or invalid antiforgery token",
                    detail: $"Send the {RequestTokenCookie} cookie's value in the {HeaderName} header.",
                    statusCode: StatusCodes.Status400BadRequest);
        });
}
