using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Account;

/// <summary>
/// Your account's profile, habits, photo and Sign out everywhere (docs/architecture/sign-in-and-tenancy.md, "Your account").
/// </summary>
public sealed class AccountProfileTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The smallest PNG: one transparent pixel.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    [Fact]
    public async Task The_profile_saves_the_name_time_zone_language_and_date_format_and_refuses_what_it_cannot_read()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var email = (await owner.GetAsync("/v1/account")).Expect(HttpStatusCode.OK).Text("email");

        var saved = (await owner.PutAsync("/v1/account/profile", Profile("Avery Quinn", "America/Chicago", "en-GB", "DayMonthYear"))).Expect(HttpStatusCode.OK);
        saved.Text("name").Should().Be("Avery Quinn");
        saved.Text("timeZone").Should().Be("America/Chicago");
        saved.Text("language").Should().Be("en-GB");
        saved.Text("dateFormat").Should().Be("DayMonthYear");
        saved.Text("email").Should().Be(email, "the sign-in address is read only");
        (await owner.Session.CurrentUserAsync(Ct))!.Value.GetProperty("displayName").GetString().Should().Be("Avery Quinn");

        var refused = (await owner.PutAsync("/v1/account/profile", Profile(" ", "Mars/Olympus_Mons", "xx-NOPE", "Browser"))).Expect(HttpStatusCode.BadRequest);
        refused.ProblemKeys().Should().BeEquivalentTo(["name", "timeZone", "language"]);
        (await owner.GetAsync("/v1/account")).Text("timeZone").Should().Be("America/Chicago", "a refused save changed nothing");
    }

    [Fact]
    public async Task The_browsers_time_zone_fills_only_an_empty_one_and_the_habits_start_unsaved()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var fresh = (await owner.GetAsync("/v1/account")).Expect(HttpStatusCode.OK);
        fresh.At("shortcutsEnabled").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "the dashboard keeps the browser's until it saves one");

        (await owner.PostAsync("/v1/account/time-zone", new JsonObject { ["timeZone"] = "Europe/London" })).Expect(HttpStatusCode.NoContent);
        (await owner.PostAsync("/v1/account/time-zone", new JsonObject { ["timeZone"] = "Asia/Tokyo" })).Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync("/v1/account")).Text("timeZone").Should().Be("Europe/London", "only an empty time zone is filled");

        var habits = (await owner.PutAsync("/v1/account/preferences", new JsonObject { ["shortcutsEnabled"] = false, ["confirmMoves"] = true })).Expect(HttpStatusCode.OK);
        habits.At("shortcutsEnabled").GetBoolean().Should().BeFalse();
        habits.At("confirmMoves").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_photo_is_shown_to_teammates_only_and_a_picture_that_is_not_one_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var stranger = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync("/v1/account/photo", new JsonObject { ["data"] = Convert.ToBase64String(Png) })).Expect(HttpStatusCode.OK);
        var url = saved.Text("photoUrl");
        url.Should().StartWith($"/v1/users/{owner.UserId}/photo");

        using (var response = await teammate.Session.Client.GetAsync(url, Ct))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(Png);
        }

        (await stranger.GetAsync(url)).Status.Should().Be(HttpStatusCode.NotFound, "someone in another organization never sees it");
        (await owner.PutAsync("/v1/account/photo", new JsonObject { ["data"] = Convert.ToBase64String("<svg/>"u8.ToArray()) }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("data");
        // A picture over 512 KB is refused before it is read: its body is over the route's limit.
        (await owner.PutAsync("/v1/account/photo", new JsonObject { ["data"] = Convert.ToBase64String(new byte[600 * 1024]) }))
            .Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        (await owner.DeleteAsync("/v1/account/photo")).Expect(HttpStatusCode.OK).At("photoUrl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await teammate.GetAsync(url)).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sign_out_everywhere_ends_the_other_sessions_and_keeps_this_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var email = (await owner.GetAsync("/v1/account")).Expect(HttpStatusCode.OK).Text("email");
        using var other = new ApiSession(fixture);
        await other.SignInWithMagicLinkAsync(email, Ct);
        (await other.CurrentUserAsync(Ct)).Should().NotBeNull();

        (await owner.PostAsync("/v1/account/sign-out-everywhere")).Expect(HttpStatusCode.NoContent);

        // The other browser's cookie is checked against the new stamp at its next check (a second in the test stacks).
        await WolverineStore.WaitUntilAsync(async () => await other.CurrentUserAsync(Ct) is null, "the other session to end", Ct);
        (await owner.GetAsync("/v1/account")).Status.Should().Be(HttpStatusCode.OK, "this session was signed in again");
    }

    private static JsonObject Profile(string name, string? timeZone, string? language, string dateFormat) => new()
    {
        ["name"] = name,
        ["timeZone"] = timeZone,
        ["language"] = language,
        ["dateFormat"] = dateFormat,
    };
}
