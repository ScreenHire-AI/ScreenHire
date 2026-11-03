using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Calendar;

/// <summary>
/// A member's calendar through the running stack, against the local sandbox calendar
/// (docs/architecture/interview-scheduling.md): connecting it with the OAuth round trip, what the sheet shows, the state
/// and cookie checks that keep a callback to the browser and person that started it, disconnecting, a member leaving,
/// and tenant isolation. Each test uses its own organization.
/// </summary>
public sealed class CalendarConnectionTests(AppHostFixture fixture)
{
    private const string Callback = "/v1/calendar/oauth/callback";
    private const string SandboxCallback = Callback + "/sandbox";
    private const string Cookie = "sh.calendar-oauth";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_sandbox_is_offered_here_and_nobody_has_a_calendar_yet()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        var status = (await member.GetAsync("/v1/calendar")).Expect(HttpStatusCode.OK);

        status.At("sandbox").GetBoolean().Should().BeTrue();
        status.Count("providers").Should().Be(1, "Google and Microsoft are hidden until their client id and secret are set");
        status.Text("providers.0.id").Should().Be("Sandbox");
        status.Text("providers.0.name").Should().Be("Sandbox calendar");
        status.At("connection").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_member_connects_the_sandbox_calendar_and_the_sheet_shows_it_without_the_tokens()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        var landed = await ConnectAsync(member, "/jobs");

        landed.PathAndQuery.Should().Be("/jobs?calendar=connected");
        var status = (await member.GetAsync("/v1/calendar")).Expect(HttpStatusCode.OK);
        status.Text("connection.provider").Should().Be("Sandbox");
        status.Text("connection.providerName").Should().Be("Sandbox calendar");
        status.Text("connection.accountEmail").Should().NotBeNullOrEmpty();
        status.At("connection.problem").ValueKind.Should().Be(JsonValueKind.Null);
        status.Body.GetRawText().Should().NotContainEquivalentOf("token").And.NotContainEquivalentOf("sandbox-access");

        // The nonce cookie is spent: the callback deletes it.
        CookieOf(member).Should().BeNull();
    }

    [Fact]
    public async Task The_tokens_are_protected_at_rest()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(member);

        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT protected_access_token, protected_refresh_token FROM calendar_connections WHERE organization_id = @org", connection);
        command.Parameters.AddWithValue("org", member.OrganizationId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        foreach (var column in new[] { 0, 1 })
        {
            reader.GetString(column).Should().NotBeEmpty().And.NotContain("sandbox-", "the provider's tokens are protected at rest");
        }
    }

    [Fact]
    public async Task Connecting_again_keeps_one_calendar_per_member()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(member);

        (await ConnectAsync(member)).PathAndQuery.Should().Be("/?calendar=connected");

        await using var db = await TenantDbAsync(member.OrganizationId);
        (await db.CalendarConnections.ToListAsync(Ct)).Should().ContainSingle().Which.UserId.Should().Be(member.UserId);
    }

    [Fact]
    public async Task A_callback_that_does_not_belong_to_the_browser_and_person_that_started_it_connects_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var state = StateOf(await AuthorizeUrlAsync(owner));
        var cookie = CookieOf(owner);
        cookie.Should().NotBeNull("connecting sets the nonce cookie the callback checks");

        // Another person's browser carrying the same cookie and state: the state names the person who started it. Once for a
        // teammate in the same organization (only the person differs) and once for someone in another organization.
        foreach (var stranger in new[] { teammate, other })
        {
            stranger.Session.Cookies.Add(
                new Uri(stranger.Session.BaseAddress, Callback), new System.Net.Cookie(cookie!.Name, cookie.Value, Callback));
            (await CallbackAsync(stranger, "sandbox", CodeFor(stranger), state)).PathAndQuery.Should().Be("/?calendar=failed");
        }

        // The same person without the cookie: a state copied out of the browser that started it.
        cookie.Expired = true;
        (await CallbackAsync(owner, "sandbox", CodeFor(owner), state)).PathAndQuery.Should().Be("/?calendar=failed");

        foreach (var who in new[] { owner, other, teammate })
        {
            (await who.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task A_sandbox_code_naming_someone_else_connects_nothing()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var state = StateOf(await AuthorizeUrlAsync(member));

        // The code is in the address, so a person could swap in another's id, here someone in another organization.
        (await CallbackAsync(member, "sandbox", CodeFor(other), state)).PathAndQuery.Should().Be("/?calendar=failed");

        (await member.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_state_that_does_not_read_is_expired_and_a_cancelled_consent_is_denied()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        (await CallbackAsync(member, "sandbox", CodeFor(member), "garbage")).PathAndQuery.Should().Be("/?calendar=expired");

        var state = StateOf(await AuthorizeUrlAsync(member, "/jobs"));
        using var response = await member.Session.Client.GetAsync(
            $"{SandboxCallback}?error=access_denied&state={Uri.EscapeDataString(state)}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.Should().Be("/jobs?calendar=denied");

        (await member.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_callback_for_a_provider_other_than_the_one_that_started_it_connects_nothing()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        var state = StateOf(await AuthorizeUrlAsync(member));

        (await CallbackAsync(member, "google", CodeFor(member), state)).PathAndQuery.Should().Be("/?calendar=failed");
        (await CallbackAsync(member, "zoom", CodeFor(member), state)).PathAndQuery.Should().Be("/?calendar=failed");

        (await member.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_callback_with_nobody_signed_in_goes_to_sign_in()
    {
        using var anonymous = new ApiSession(fixture);

        using var response = await anonymous.Client.GetAsync($"{SandboxCallback}?code=sandbox-connect.x&state=garbage", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.Should().StartWith("/sign-in?returnUrl=");
    }

    [Fact]
    public async Task A_provider_that_is_not_configured_cannot_be_connected()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        foreach (var provider in new[] { "Google", "Microsoft" })
        {
            (await member.PostAsync("/v1/calendar/connect", new JsonObject { ["provider"] = provider, ["returnUrl"] = "/" }))
                .Expect(HttpStatusCode.NotFound);
        }

        (await member.PostAsync("/v1/calendar/connect", new JsonObject { ["provider"] = "Zoom", ["returnUrl"] = "/" }))
            .Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_return_url_is_a_local_path_only()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        (await ConnectAsync(member, "https://evil.example.com/steal")).PathAndQuery.Should().Be("/?calendar=connected");
    }

    [Fact]
    public async Task The_sandbox_consent_page_sends_the_browser_only_to_our_own_callback()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();

        using var response = await member.Session.Client.GetAsync(
            "/v1/calendar/sandbox/authorize?state=x&redirect_uri=" + Uri.EscapeDataString("https://evil.example.com/cb"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disconnecting_removes_the_calendar_and_doing_it_again_is_harmless()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(member);

        (await member.DeleteAsync("/v1/calendar")).Expect(HttpStatusCode.NoContent);

        (await member.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
        await using var db = await TenantDbAsync(member.OrganizationId);
        (await db.CalendarConnections.CountAsync(Ct)).Should().Be(0);
        (await member.DeleteAsync("/v1/calendar")).Expect(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Disconnecting_changes_only_the_callers_calendar()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await ConnectAsync(owner);
        await ConnectAsync(teammate);

        (await teammate.DeleteAsync("/v1/calendar")).Expect(HttpStatusCode.NoContent);

        (await owner.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Object);
        (await teammate.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Removing_a_member_deletes_their_calendar_connection()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var leaver = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await ConnectAsync(owner);
        await ConnectAsync(leaver);

        (await owner.DeleteAsync($"/v1/organization/members/{await owner.MemberIdAsync(leaver.UserId)}")).Expect(HttpStatusCode.NoContent);

        await using var db = await TenantDbAsync(owner.OrganizationId);
        (await db.CalendarConnections.ToListAsync(Ct)).Should().ContainSingle("only the owner's remains").Which.UserId.Should().Be(owner.UserId);
    }

    [Fact]
    public async Task One_organizations_calendars_are_invisible_to_another()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        await ConnectAsync(first);

        (await second.GetAsync("/v1/calendar")).At("connection").ValueKind.Should().Be(JsonValueKind.Null);
        await using var db = await TenantDbAsync(second.OrganizationId);
        (await db.CalendarConnections.CountAsync(Ct)).Should().Be(0, "row-level security shows a tenant only its own rows");
        await using var firstDb = await TenantDbAsync(first.OrganizationId);
        (await firstDb.CalendarConnections.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_sandbox_busy_block_is_added_for_the_caller_and_must_end_after_it_starts()
    {
        using var member = await fixture.SignedInWithOrganizationAsync();
        var start = DateTimeOffset.UtcNow.AddDays(3);

        (await member.PostAsync("/v1/calendar/sandbox/busy", new JsonObject { ["start"] = start, ["end"] = start.AddHours(2) }))
            .Expect(HttpStatusCode.NoContent);
        (await member.PostAsync("/v1/calendar/sandbox/busy", new JsonObject { ["start"] = start, ["end"] = start.AddHours(-1) }))
            .Expect(HttpStatusCode.BadRequest);

        await using var db = await TenantDbAsync(member.OrganizationId);
        var busy = await db.CalendarSandboxBusy.ToListAsync(Ct);
        busy.Should().ContainSingle().Which.UserId.Should().Be(member.UserId);
        busy[0].EndsAt.Should().Be(busy[0].StartsAt.AddHours(2));
    }

    // The sheet and the API both start from this: the connect URL, then the browser's trip through the sandbox's consent
    // page (none) and the callback, one hop at a time.
    private static async Task<Uri> ConnectAsync(Tenant who, string returnUrl = "/") =>
        await FollowAsync(who, await AuthorizeUrlAsync(who, returnUrl));

    private static async Task<string> AuthorizeUrlAsync(Tenant who, string returnUrl = "/") =>
        (await who.PostAsync("/v1/calendar/connect", new JsonObject { ["provider"] = "Sandbox", ["returnUrl"] = returnUrl }))
            .Expect(HttpStatusCode.OK).Text("authorizeUrl");

    // The URLs are the dashboard's host, which these tests do not run, so each hop is asked of the API under the same path.
    private static async Task<Uri> FollowAsync(Tenant who, string url)
    {
        var next = OnApi(who, new Uri(url));
        for (var hops = 0; hops < 6; hops++)
        {
            using var response = await who.Session.Client.GetAsync(next, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect, $"{next.AbsolutePath} should redirect");
            var location = response.Headers.Location!;
            var target = location.IsAbsoluteUri ? location : new Uri(who.Session.BaseAddress, location);
            if (target.AbsolutePath != SandboxCalendarProvider.AuthorizePath && !target.AbsolutePath.StartsWith(Callback, StringComparison.Ordinal))
            {
                return target;
            }

            next = OnApi(who, target);
        }

        throw new InvalidOperationException("The OAuth round trip did not end.");
    }

    private static async Task<Uri> CallbackAsync(Tenant who, string provider, string code, string state)
    {
        using var response = await who.Session.Client.GetAsync(
            $"{Callback}/{provider}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return response.Headers.Location!;
    }

    private static string CodeFor(Tenant who) => $"{SandboxCalendarProvider.ConnectCodePrefix}{who.UserId:N}";

    private static Uri OnApi(Tenant who, Uri url) => new(who.Session.BaseAddress, url.PathAndQuery);

    private static string StateOf(string authorizeUrl) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(authorizeUrl).Query)["state"].ToString();

    private static System.Net.Cookie? CookieOf(Tenant who) =>
        who.Session.Cookies.GetCookies(new Uri(who.Session.BaseAddress, Callback))[Cookie];

    private async Task<ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
