using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.E2E;

/// <summary>
/// The states a webhook endpoint goes through, on its page in a real browser, against a real receiver (WebhookReceiver):
/// one that hangs past the timeout, answers 500 and then recovers; one whose delivery gives up; one that keeps failing
/// until the worker turns the endpoint off; and a rotated secret the receiver does not know yet. Each shows its status
/// as an icon, a colour and a word, says what happened, and leaves a way forward on the page.
/// </summary>
public sealed class WebhookStatesTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Gb = ["GB"];

    private static readonly string[] JobPublished = ["job.published"];

    // Four attempts' worth of retries and the timeout fit in this.
    private static readonly LocatorAssertionsToContainTextOptions Slow = new() { Timeout = 60_000 };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_endpoint_that_times_out_fails_gives_up_is_turned_off_and_a_rotated_secret_each_say_so_with_a_way_forward()
    {
        var owner = ApiSession.NewEmail("e2e-webhook-states");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        await session.OnboardAsync(Ct);
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var (endpointId, firstSecret) = await CreateEndpointAsync(session, receiver);

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(owner);
        await Page.GotoAsync(DashboardUrl($"/settings/webhooks/{endpointId}"));
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Delivering");
        await Expect(Page.GetByText("No deliveries yet.")).ToBeVisibleAsync();
        var newest = Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Row).Nth(1);

        // 1. The receiver hangs past the stack's 5 second timeout, then answers 500, then recovers: the delivery is tried
        // again with backoff and ends Delivered with its three attempts. The first status is never read by the sender,
        // which has given up by then, but the receiver takes one per request.
        receiver.RespondWith(200, 500, 200);
        receiver.DelayResponses(AppHostFixture.WebhookTimeout + TimeSpan.FromSeconds(3));
        await PublishJobAsync(session, "Webhook one");
        await Expect(newest).ToContainTextAsync("Delivered", Slow);
        await Expect(newest.GetByRole(AriaRole.Cell).Nth(2)).ToHaveTextAsync("3");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-webhook-recovered-1440");

        // 2. The next delivery meets two 500s and then a receiver that hangs past the stack's 5 second timeout, so it
        // gives up as Failed and says the receiver did not answer. The endpoint stays on after one failed delivery, and
        // the failed delivery can be replayed from its row.
        receiver.RespondWith(500, 500, 200);
        receiver.DelayResponses(TimeSpan.Zero, TimeSpan.Zero, AppHostFixture.WebhookTimeout + TimeSpan.FromSeconds(3));
        await PublishJobAsync(session, "Webhook two");
        await Expect(newest).ToContainTextAsync("Failed", Slow);
        await Expect(newest).ToContainTextAsync("The receiver did not answer within 5 seconds.");
        await Expect(band).ToContainTextAsync("Delivering");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Actions for the Job published delivery" }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Replay delivery" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await SaveScreenshotAsync(Page, "dashboard-webhook-failed-1440");

        // 3. A second delivery in a row that fails every attempt turns the endpoint off: the page says why, and Turn on
        // is the way forward. Replay waits for it, and the log says so.
        receiver.RespondWith(500, 500, 500);
        await PublishJobAsync(session, "Webhook three");
        await Expect(band).ToContainTextAsync("Turned off", Slow);
        await Expect(newest).ToContainTextAsync("HTTP 500");
        await Expect(Page.GetByText("Disabled automatically: 2 deliveries in a row failed every attempt.")).ToBeVisibleAsync();
        await Expect(band.GetByRole(AriaRole.Button, new() { Name = "Turn on" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Turn the endpoint on to replay deliveries.")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-webhook-turned-off-1440");

        // 4. Turn on brings it back, with the notice gone. Rotating the secret asks first, because receivers fail until
        // they have the new one, and shows the new secret once.
        await band.GetByRole(AriaRole.Button, new() { Name = "Turn on" }).ClickAsync();
        await Expect(band).ToContainTextAsync("Delivering");
        await Expect(Page.GetByText("Disabled automatically")).ToHaveCountAsync(0);
        await band.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Rotate signing secret" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Dialog, new() { Name = "Rotate the signing secret?" });
        await Expect(confirm).ToContainTextAsync("Receivers fail to check deliveries until they have the new one.");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Rotate secret" }).ClickAsync();
        var shown = Page.GetByRole(AriaRole.Dialog, new() { Name = "Your new signing secret" });
        var field = shown.GetByRole(AriaRole.Textbox, new() { Name = "Signing secret" });
        await Expect(field).ToHaveValueAsync(new Regex("^whsec_"));
        var secret = await field.InputValueAsync();
        secret.Should().NotBe(firstSecret);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(shown).ToHaveCountAsync(0);

        // 5. A receiver that still has the old secret refuses what is signed with the new one (it answers 401): the delivery
        // fails in plain words. Once the receiver has the new secret, Replay sends it again, signed with the new secret only.
        receiver.RespondWith(401, 401, 401);
        var published = await PublishJobAsync(session, "Webhook four");
        await Expect(newest).ToContainTextAsync("Failed", Slow);
        await Expect(newest).ToContainTextAsync("HTTP 401");
        await newest.GetByRole(AriaRole.Button, new() { Name = "Actions for the Job published delivery" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Replay delivery" }).ClickAsync();
        await Expect(newest).ToContainTextAsync("Delivered", Slow);
        var replayed = (await receiver.WaitForAsync(r => r.Type == "job.published" && r.Body.Contains(published.ToString(), StringComparison.Ordinal), 4, "the failed attempts and the replay", Ct))[^1];
        replayed.IsSignedWith(secret).Should().BeTrue("a replay is signed with the secret now in force");
        replayed.IsSignedWith(firstSecret).Should().BeFalse("the old secret stopped when it was rotated");

        // The same page on a phone: the log reads as items and nothing pushes the window sideways.
        await Page.SetViewportSizeAsync(390, 844);
        await Page.ReloadAsync();
        await Expect(band).ToContainTextAsync("Delivering");
        var widths = await Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
        await SaveScreenshotAsync(Page, "dashboard-webhook-log-390");
        widths[0].Should().BeLessThanOrEqualTo(widths[1]);
    }

    private static async Task<(Guid Id, string Secret)> CreateEndpointAsync(ApiSession session, WebhookReceiver receiver)
    {
        using var response = await session.PostAsync("/v1/webhooks", new { url = receiver.Url.ToString(), events = JobPublished, description = "Applicant sync" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (created.GetProperty("endpoint").GetProperty("id").GetGuid(), created.GetProperty("signingSecret").GetString()!);
    }

    private static async Task<Guid> PublishJobAsync(ApiSession session, string prefix)
    {
        using var created = await session.PostAsync("/v1/jobs", new
        {
            title = $"{prefix} {Guid.NewGuid():N}"[..24],
            description = "<p>Build and run the things people rely on.</p>",
            employmentType = "FullTime",
            workplaceType = "Remote",
            applicantLocationRequirements = Gb,
            templateVersionId = SystemTemplates.SoftwareEngineerVersion1,
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var published = await session.PostAsync($"/v1/jobs/{jobId}/publish", new { }, Ct);
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Ct));
        return jobId;
    }

    private async Task SignInAsync(string email)
    {
        await Page.GotoAsync(DashboardUrl("/sign-in"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
