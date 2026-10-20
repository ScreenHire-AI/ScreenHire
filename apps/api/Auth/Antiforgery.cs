using Microsoft.AspNetCore.Antiforgery;

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
        context.Response.Cookies.Append(RequestTokenCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // the dashboard reads it to send the header
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
    }

    /// <summary>Rejects a request without a valid antiforgery header with 400 problem details.</summary>
    public static TBuilder RequireAntiforgeryToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var antiforgery = invocation.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            return await antiforgery.IsRequestValidAsync(invocation.HttpContext)
                ? await next(invocation)
                : TypedResults.Problem(
                    title: "Missing or invalid antiforgery token",
                    detail: $"Send the {RequestTokenCookie} cookie's value in the {HeaderName} header.",
                    statusCode: StatusCodes.Status400BadRequest);
        });
}
