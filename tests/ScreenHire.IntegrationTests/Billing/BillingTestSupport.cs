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

    public const string Credits = "/v1/organization/billing/credits";

    /// <summary>
    /// Buys <paramref name="amount"/> of AI credits as the owner does: starts the credit checkout and follows the sandbox's page, which
    /// confirms the payment to the worker. The credit is added a moment later, by the worker, so a caller waits for the balance.
    /// </summary>
    public static async Task BuyCreditsAsync(this AppHostFixture fixture, Tenant owner, decimal amount)
    {
        var checkout = (await owner.PostAsync($"{Credits}/checkout", new JsonObject { ["amount"] = amount })).Expect(HttpStatusCode.OK);
        var (status, location) = await fixture.FollowAsync(checkout.Text("url"));
        status.Should().Be(HttpStatusCode.Redirect);
        location!.ToString().Should().Contain("/settings/billing").And.Contain("credits=success");
    }

    public static async Task<ApiResult> CreditsAsync(this Tenant tenant) => (await tenant.GetAsync(Credits)).Expect(HttpStatusCode.OK);

    public static async Task<ApiResult> BillingAsync(this Tenant tenant) => (await tenant.GetAsync(Overview)).Expect(HttpStatusCode.OK);
}
