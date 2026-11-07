using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// The sandbox provider's round trips as the dashboard does them: the API returns a link, the browser follows it (an
/// anonymous navigation that the API answers with a redirect back to the Billing page).
/// </summary>
internal static class BillingTestSupport
{
    public const string Overview = "/v1/organization/billing";

    /// <summary>Follows a sandbox link once, signed out and without following the redirect. Returns the response's status and where it points.</summary>
    public static async Task<(HttpStatusCode Status, Uri? Location)> FollowAsync(this AppHostFixture fixture, string link)
    {
        using var browser = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = fixture.GetEndpoint("api") };
        using var response = await browser.GetAsync(link, TestContext.Current.CancellationToken);
        return (response.StatusCode, response.Headers.Location);
    }

    /// <summary>Starts a checkout for <paramref name="plan"/> as the owner and follows the sandbox's page, which activates the plan.</summary>
    public static async Task SubscribeAsync(this AppHostFixture fixture, Tenant owner, string plan)
    {
        var checkout = (await owner.PostAsync($"{Overview}/checkout", new JsonObject { ["plan"] = plan })).Expect(HttpStatusCode.OK);
        var (status, location) = await fixture.FollowAsync(checkout.Text("url"));
        status.Should().Be(HttpStatusCode.Redirect);
        location!.ToString().Should().Contain("/settings/billing").And.Contain("checkout=success");
    }

    /// <summary>Opens the portal as the owner and follows the sandbox's page, which cancels the subscription at once.</summary>
    public static async Task CancelSubscriptionAsync(this AppHostFixture fixture, Tenant owner)
    {
        var portal = (await owner.PostAsync($"{Overview}/portal")).Expect(HttpStatusCode.OK);
        var (status, location) = await fixture.FollowAsync(portal.Text("url"));
        status.Should().Be(HttpStatusCode.Redirect);
        location!.ToString().Should().Contain("/settings/billing");
    }

    public static async Task<ApiResult> BillingAsync(this Tenant tenant) => (await tenant.GetAsync(Overview)).Expect(HttpStatusCode.OK);
}
