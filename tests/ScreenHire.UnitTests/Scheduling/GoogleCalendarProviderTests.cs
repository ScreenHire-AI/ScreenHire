using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The real Google provider against a stub standing in for Google's servers (Calendar API v3 and the OAuth 2.0 web server
/// flow): the authorize URL and its exact scopes, the token calls, free/busy, creating, moving and cancelling an event with
/// the ids that make a create idempotent, and how every kind of refusal maps to a <see cref="CalendarApiException"/>.
/// No network, no real client id or secret.
/// </summary>
public sealed class GoogleCalendarProviderTests : IDisposable
{
    private const string Secret = "fictional-client-secret";

    private const string Token = "fictional-access-token";

    private const string Email = "member@example.org";

    private static readonly Uri Redirect = new("https://app.example.org/v1/calendar/oauth/callback/google");

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly StubCalendarServer google = new();

    private readonly List<GoogleCalendarProvider> providers = [];

    public void Dispose()
    {
        foreach (var provider in providers)
        {
            provider.Dispose();
        }

        google.Dispose();
    }

    private GoogleCalendarProvider Provider()
    {
        var options = Options.Create(new CalendarOptions { Google = new CalendarOAuthApp { ClientId = "fictional-client-id", ClientSecret = Secret } });
        var provider = new GoogleCalendarProvider(options, new FixedTime(Now), google);
        providers.Add(provider);
        return provider;
    }

    private static EventSpec Spec(string id = "0123456789abcdef0123456789abcdef", bool meeting = true, params string[] invite) =>
        new(id, "Interview with Avery Quinn", "Candidate page: https://app.example.org/applications/1", Start, Start.AddHours(1), invite, meeting);

    [Fact]
    public void The_authorize_url_asks_for_the_two_calendar_scopes_and_nothing_else()
    {
        var url = Provider().AuthorizeUrl("state value/1", Redirect);

        url.Host.Should().Be("accounts.google.com");
        url.AbsolutePath.Should().Be("/o/oauth2/v2/auth");
        var query = QueryHelpers.ParseQuery(url.Query);
        query.Keys.Should().BeEquivalentTo("client_id", "redirect_uri", "response_type", "scope", "access_type", "prompt", "state");
        query["client_id"].ToString().Should().Be("fictional-client-id");
        query["redirect_uri"].ToString().Should().Be(Redirect.ToString());
        query["response_type"].ToString().Should().Be("code");
        query["access_type"].ToString().Should().Be("offline");
        query["prompt"].ToString().Should().Be("consent");
        query["state"].ToString().Should().Be("state value/1");
        query["scope"].ToString().Should().Be("https://www.googleapis.com/auth/calendar.freebusy https://www.googleapis.com/auth/calendar.events");
        url.Query.Should().NotContain(Secret, "the secret never goes through the browser");
    }

    [Fact]
    public void The_scopes_are_exactly_free_busy_and_events_so_no_identity_scope_can_slip_in()
    {
        GoogleCalendarProvider.Scopes.Should().Equal(
            "https://www.googleapis.com/auth/calendar.freebusy",
            "https://www.googleapis.com/auth/calendar.events");
        GoogleCalendarProvider.Scopes.Should().NotContain(s => s.Contains("openid") || s.Contains("email") || s.Contains("profile") || s.EndsWith("/calendar"));
    }

    [Fact]
    public async Task A_code_is_exchanged_with_a_form_post_and_the_account_address_is_read_from_the_calendar_title()
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3599,"refresh_token":"fictional-refresh","scope":"https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.freebusy","token_type":"Bearer"}""");
        google.Ok($$$$"""{"summary":"{{{{Email}}}}"}""");

        var grant = await Provider().ExchangeCodeAsync("the-code", Redirect, Ct);

        grant.Should().Be(new CalendarGrant(Token, "fictional-refresh", Now.AddSeconds(3599), Email));
        var exchange = google.Calls[0];
        exchange.Method.Should().Be(HttpMethod.Post);
        exchange.Url.ToString().Should().Be("https://oauth2.googleapis.com/token");
        exchange.Form.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["code"] = "the-code",
            ["client_id"] = "fictional-client-id",
            ["client_secret"] = Secret,
            ["redirect_uri"] = Redirect.ToString(),
            ["grant_type"] = "authorization_code",
        });
        exchange.Authorization.Should().BeNull();

        var title = google.Calls[1];
        title.Method.Should().Be(HttpMethod.Get);
        title.Path.Should().Be("/calendar/v3/calendars/primary/events");
        title.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["maxResults"] = "1", ["fields"] = "summary" });
        title.Authorization.Should().Be($"Bearer {Token}");
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("")]
    public async Task A_calendar_title_that_is_not_an_address_leaves_the_account_address_empty(string title)
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3600,"refresh_token":"r"}""");
        google.Ok($$$$"""{"summary":"{{{{title}}}}"}""");

        var grant = await Provider().ExchangeCodeAsync("c", Redirect, Ct);

        grant.AccountEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_failing_title_lookup_does_not_fail_the_connect()
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3600,"refresh_token":"r"}""");
        google.Answer(HttpStatusCode.Forbidden, """{"error":{"errors":[{"reason":"forbidden"}]}}""");

        var grant = await Provider().ExchangeCodeAsync("c", Redirect, Ct);

        grant.AccessToken.Should().Be(Token);
        grant.AccountEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_connect_without_a_refresh_token_is_refused()
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3600}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().ExchangeCodeAsync("c", Redirect, Ct));

        failure.Code.Should().Be("no_refresh_token");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task A_member_who_unticks_the_event_scope_on_the_consent_screen_has_not_connected()
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3600,"refresh_token":"r","scope":"https://www.googleapis.com/auth/calendar.freebusy"}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().ExchangeCodeAsync("c", Redirect, Ct));

        failure.Code.Should().Be("consent_required");
        failure.Permanent.Should().BeTrue();
        google.Calls.Should().ContainSingle("nothing is read from a calendar the member did not allow");
    }

    [Fact]
    public async Task A_refresh_posts_the_refresh_token_and_an_answer_without_a_new_one_keeps_it_null()
    {
        google.Ok($$$$"""{"access_token":"{{{{Token}}}}","expires_in":3600,"scope":"https://www.googleapis.com/auth/calendar.events","token_type":"Bearer"}""");

        var grant = await Provider().RefreshAsync("old-refresh", Ct);

        grant.Should().Be(new CalendarGrant(Token, null, Now.AddHours(1), null));
        google.Calls.Should().ContainSingle().Which.Form.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["refresh_token"] = "old-refresh",
            ["client_id"] = "fictional-client-id",
            ["client_secret"] = Secret,
            ["grant_type"] = "refresh_token",
        });
    }

    [Fact]
    public async Task A_revoked_refresh_token_is_a_permanent_failure_to_refresh()
    {
        google.Answer(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().RefreshAsync("old", Ct));

        failure.Code.Should().Be("invalid_grant");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task Revoking_posts_the_token_to_the_revoke_endpoint_and_treats_an_already_dropped_token_as_done()
    {
        google.Answer(HttpStatusCode.OK);
        google.Answer(HttpStatusCode.BadRequest, """{"error":"invalid_token"}""");

        var provider = Provider();
        await provider.RevokeAsync("to-revoke", Ct);
        await provider.RevokeAsync("already-gone", Ct);

        google.Calls[0].Url.ToString().Should().Be("https://oauth2.googleapis.com/revoke");
        google.Calls[0].Form.Should().BeEquivalentTo(new Dictionary<string, string> { ["token"] = "to-revoke" });
    }

    [Fact]
    public async Task A_revoke_that_fails_for_another_reason_throws()
    {
        google.Answer(HttpStatusCode.ServiceUnavailable);

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().RevokeAsync("t", Ct));

        failure.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task Free_busy_asks_for_the_primary_calendar_in_utc_and_reads_its_busy_intervals()
    {
        google.Ok("""
            {"kind":"calendar#freeBusy","calendars":{"primary":{"busy":[
              {"start":"2026-11-10T17:00:00Z","end":"2026-11-10T18:00:00Z"},
              {"start":"2026-11-10T09:30:00-05:00","end":"2026-11-10T10:00:00-05:00"}]}}}
            """);

        var busy = await Provider().FreeBusyAsync(Token, Start, Start.AddDays(7), Ct);

        busy.Should().Equal(
            new BusyInterval(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero)),
            new BusyInterval(new DateTimeOffset(2026, 11, 10, 17, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero)));
        var call = google.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.ToString().Should().Be("https://www.googleapis.com/calendar/v3/freeBusy");
        call.Authorization.Should().Be($"Bearer {Token}");
        call.Json["timeMin"]!.GetValue<string>().Should().Be("2026-11-10T15:00:00.0000000Z");
        call.Json["timeMax"]!.GetValue<string>().Should().Be("2026-11-17T15:00:00.0000000Z");
        call.Json["timeZone"]!.GetValue<string>().Should().Be("UTC");
        call.Json["items"]!.AsArray().Should().ContainSingle().Which!["id"]!.GetValue<string>().Should().Be("primary");
    }

    [Fact]
    public async Task A_calendar_with_no_busy_time_is_an_empty_list()
    {
        google.Ok("""{"calendars":{"primary":{"busy":[]}}}""");

        (await Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("notFound", true)]
    [InlineData("internalError", false)]
    public async Task A_calendar_google_could_not_compute_is_a_failure_not_a_free_calendar(string reason, bool permanent)
    {
        google.Ok($$$$"""{"calendars":{"primary":{"errors":[{"domain":"global","reason":"{{{{reason}}}}"}],"busy":[]}}}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Code.Should().Be(reason);
        failure.Permanent.Should().Be(permanent);
    }

    [Fact]
    public async Task Creating_an_organizer_event_asks_for_a_meet_link_and_emails_the_guest()
    {
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","status":"confirmed","hangoutLink":"https://meet.google.com/abc-defg-hij"}""");

        var result = await Provider().CreateEventAsync(Token, Spec(meeting: true, invite: "candidate@example.org"), Ct);

        result.Should().Be(new CalendarEventResult("0123456789abcdef0123456789abcdef", "https://meet.google.com/abc-defg-hij"));
        var call = google.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("/calendar/v3/calendars/primary/events");
        call.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["conferenceDataVersion"] = "1", ["sendUpdates"] = "all" });
        call.Authorization.Should().Be($"Bearer {Token}");
        var body = call.Json;
        body["id"]!.GetValue<string>().Should().Be("0123456789abcdef0123456789abcdef");
        body["summary"]!.GetValue<string>().Should().Be("Interview with Avery Quinn");
        body["description"]!.GetValue<string>().Should().Contain("Candidate page");
        body["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-10T15:00:00.0000000Z");
        body["start"]!["timeZone"]!.GetValue<string>().Should().Be("UTC");
        body["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-10T16:00:00.0000000Z");
        body["attendees"]!.AsArray().Should().ContainSingle().Which!["email"]!.GetValue<string>().Should().Be("candidate@example.org");
        var request = body["conferenceData"]!["createRequest"]!;
        request["requestId"]!.GetValue<string>().Should().Be("0123456789abcdef0123456789abcdef");
        request["conferenceSolutionKey"]!["type"]!.GetValue<string>().Should().Be("hangoutsMeet");
    }

    [Fact]
    public async Task Creating_a_plain_event_sends_no_conference_no_guests_and_no_update_emails()
    {
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","status":"confirmed"}""");

        var result = await Provider().CreateEventAsync(Token, Spec(meeting: false), Ct);

        result.MeetingUrl.Should().BeNull();
        var call = google.Calls.Should().ContainSingle().Which;
        call.Url.Query.Should().BeEmpty();
        call.Json.AsObject().ContainsKey("attendees").Should().BeFalse();
        call.Json.AsObject().ContainsKey("conferenceData").Should().BeFalse();
    }

    [Fact]
    public async Task A_guest_without_a_meeting_link_gets_the_update_email_but_no_conference_version()
    {
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef"}""");

        await Provider().CreateEventAsync(Token, Spec(meeting: false, invite: "candidate@example.org"), Ct);

        google.Calls.Single().Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["sendUpdates"] = "all" });
    }

    [Fact]
    public async Task A_meet_link_that_the_insert_has_not_made_yet_is_read_once_more()
    {
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","conferenceData":{"createRequest":{"status":{"statusCode":"pending"}}}}""");
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:+1-555-0100"},{"entryPointType":"video","uri":"https://meet.google.com/xyz-abcd-efg"}]}}""");

        var result = await Provider().CreateEventAsync(Token, Spec(meeting: true), Ct);

        result.MeetingUrl.Should().Be("https://meet.google.com/xyz-abcd-efg");
        google.Calls[1].Method.Should().Be(HttpMethod.Get);
        google.Calls[1].Path.Should().Be("/calendar/v3/calendars/primary/events/0123456789abcdef0123456789abcdef");
    }

    [Fact]
    public void The_event_id_is_stable_valid_and_different_for_different_idempotency_ids()
    {
        var first = GoogleCalendarProvider.EventId("0123456789abcdef0123456789abcdef");

        first.Should().Be("0123456789abcdef0123456789abcdef", "a valid id is used as it is");
        GoogleCalendarProvider.EventId("0123456789abcdef0123456789abcdef").Should().Be(first);
        GoogleCalendarProvider.EventId("fedcba9876543210fedcba9876543210").Should().NotBe(first);

        foreach (var odd in new[] { "Has-Upper_and punctuation", "x", "w0rld" })
        {
            var hashed = GoogleCalendarProvider.EventId(odd);
            hashed.Should().MatchRegex("^[a-v0-9]{5,1024}$");
            hashed.Should().Be(GoogleCalendarProvider.EventId(odd));
            hashed.Should().NotBe(odd);
        }

        GoogleCalendarProvider.EventId("Has-Upper_and punctuation").Should().NotBe(GoogleCalendarProvider.EventId("has-upper_and punctuation"));
    }

    [Fact]
    public async Task The_same_idempotency_id_posts_the_same_event_id_each_time()
    {
        google.Ok("""{"id":"x"}""").Ok("""{"id":"x"}""").Ok("""{"id":"x"}""");

        var provider = Provider();
        await provider.CreateEventAsync(Token, Spec(id: "interview-one", meeting: false), Ct);
        await provider.CreateEventAsync(Token, Spec(id: "interview-one", meeting: false), Ct);
        await provider.CreateEventAsync(Token, Spec(id: "interview-two", meeting: false), Ct);

        var ids = google.Calls.Select(c => c.Json["id"]!.GetValue<string>()).ToList();
        ids[0].Should().Be(ids[1]).And.NotBe(ids[2]);
    }

    [Fact]
    public async Task An_event_that_already_exists_is_returned_instead_of_created_again()
    {
        google.Answer(HttpStatusCode.Conflict, """{"error":{"errors":[{"domain":"global","reason":"duplicate"}],"code":409}}""");
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","status":"confirmed","hangoutLink":"https://meet.google.com/first-one"}""");

        var result = await Provider().CreateEventAsync(Token, Spec(), Ct);

        result.Should().Be(new CalendarEventResult("0123456789abcdef0123456789abcdef", "https://meet.google.com/first-one"));
        google.Calls[1].Method.Should().Be(HttpMethod.Get);
        google.Calls[1].Path.Should().Be("/calendar/v3/calendars/primary/events/0123456789abcdef0123456789abcdef");
    }

    [Fact]
    public async Task An_event_the_interviewer_deleted_keeps_its_id_so_a_create_brings_it_back()
    {
        google.Answer(HttpStatusCode.Conflict, """{"error":{"errors":[{"reason":"duplicate"}]}}""");
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","status":"cancelled"}""");
        google.Ok("""{"id":"0123456789abcdef0123456789abcdef","status":"confirmed","hangoutLink":"https://meet.google.com/back-again"}""");

        var result = await Provider().CreateEventAsync(Token, Spec(invite: "candidate@example.org"), Ct);

        result.MeetingUrl.Should().Be("https://meet.google.com/back-again");
        var revive = google.Calls[2];
        revive.Method.Should().Be(HttpMethod.Patch);
        revive.Path.Should().Be("/calendar/v3/calendars/primary/events/0123456789abcdef0123456789abcdef");
        revive.Json["status"]!.GetValue<string>().Should().Be("confirmed");
        revive.Json["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-10T15:00:00.0000000Z");
    }

    [Fact]
    public async Task Moving_an_event_patches_its_times_and_emails_the_guests()
    {
        google.Ok("""{"id":"evt1","status":"confirmed","hangoutLink":"https://meet.google.com/kept"}""");

        var result = await Provider().MoveEventAsync(Token, "evt1", Start.AddDays(1), Start.AddDays(1).AddHours(1), Ct);

        result.Should().Be(new CalendarEventResult("evt1", "https://meet.google.com/kept"));
        var call = google.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Patch);
        call.Path.Should().Be("/calendar/v3/calendars/primary/events/evt1");
        call.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["sendUpdates"] = "all" });
        call.Json["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-11T15:00:00.0000000Z");
        call.Json["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-11T16:00:00.0000000Z");
        call.Json.AsObject().Count.Should().Be(2, "only the times change");
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Moving_an_event_that_is_gone_is_a_retryable_event_not_found(int status)
    {
        google.Answer((HttpStatusCode)status, """{"error":{"errors":[{"reason":"deleted"}]}}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().MoveEventAsync(Token, "evt1", Start, Start.AddHours(1), Ct));

        failure.Code.Should().Be("event_not_found");
        failure.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task Moving_an_event_google_still_holds_as_cancelled_is_event_not_found_too()
    {
        google.Ok("""{"id":"evt1","status":"cancelled"}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().MoveEventAsync(Token, "evt1", Start, Start.AddHours(1), Ct));

        failure.Code.Should().Be("event_not_found");
    }

    [Fact]
    public async Task Cancelling_deletes_the_event_and_tells_the_guests()
    {
        google.Answer(HttpStatusCode.NoContent);

        await Provider().CancelEventAsync(Token, "evt1", Ct);

        var call = google.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Delete);
        call.Path.Should().Be("/calendar/v3/calendars/primary/events/evt1");
        call.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["sendUpdates"] = "all" });
    }

    [Fact]
    public async Task Removing_deletes_the_event_and_sends_the_guests_nothing()
    {
        google.Answer(HttpStatusCode.NoContent);

        await Provider().RemoveEventAsync(Token, "evt1", Ct);

        var call = google.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Delete);
        call.Path.Should().Be("/calendar/v3/calendars/primary/events/evt1");
        call.Query.Should().BeEquivalentTo(new Dictionary<string, string> { ["sendUpdates"] = "none" });
    }

    [Fact]
    public async Task Removing_an_event_that_is_already_gone_is_not_an_error()
    {
        google.Answer(HttpStatusCode.NotFound);

        await Provider().RemoveEventAsync(Token, "evt1", Ct);
    }

    [Fact]
    public async Task An_event_with_no_saved_id_is_found_by_the_id_its_create_asked_for_with_no_call()
    {
        var id = await Provider().FindEventIdAsync(Token, "0123456789abcdef0123456789abcdef", Start, Start.AddHours(1), Ct);

        id.Should().Be(GoogleCalendarProvider.EventId("0123456789abcdef0123456789abcdef"));
        google.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Cancelling_an_event_that_is_already_gone_is_not_an_error(int status)
    {
        google.Answer((HttpStatusCode)status);

        await Provider().CancelEventAsync(Token, "evt1", Ct);
    }

    [Fact]
    public async Task A_cancel_google_refuses_for_another_reason_throws()
    {
        google.Answer(HttpStatusCode.Forbidden, """{"error":{"errors":[{"reason":"forbiddenForNonOrganizer"}]}}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().CancelEventAsync(Token, "evt1", Ct));

        failure.Code.Should().Be("forbiddenForNonOrganizer");
        failure.Permanent.Should().BeTrue();
    }

    [Theory]
    [InlineData(401, """{"error":{"errors":[{"reason":"authError"}],"code":401}}""", "authError", true)]
    [InlineData(401, "", "http_401", true)]
    [InlineData(403, """{"error":{"errors":[{"domain":"usageLimits","reason":"rateLimitExceeded"}]}}""", "rateLimitExceeded", false)]
    [InlineData(403, """{"error":{"errors":[{"domain":"usageLimits","reason":"userRateLimitExceeded"}]}}""", "userRateLimitExceeded", false)]
    [InlineData(403, """{"error":{"errors":[{"reason":"quotaExceeded"}]}}""", "quotaExceeded", false)]
    [InlineData(403, """{"error":{"errors":[{"reason":"insufficientPermissions"}]}}""", "insufficientPermissions", true)]
    [InlineData(429, """{"error":{"errors":[{"reason":"rateLimitExceeded"}]}}""", "rateLimitExceeded", false)]
    [InlineData(429, "", "rate_limited", false)]
    [InlineData(500, "", "http_500", false)]
    [InlineData(503, """{"error":{"errors":[{"reason":"backendError"}]}}""", "backendError", false)]
    [InlineData(400, """{"error":{"errors":[{"reason":"badRequest"}]}}""", "badRequest", true)]
    [InlineData(404, """{"error":{"errors":[{"reason":"notFound"}]}}""", "notFound", true)]
    public async Task Every_refusal_maps_to_a_code_and_whether_a_retry_could_help(int status, string body, string code, bool permanent)
    {
        google.Answer((HttpStatusCode)status, body);

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Code.Should().Be(code);
        failure.Permanent.Should().Be(permanent);
    }

    [Theory]
    [InlineData(400, """{"error":"invalid_grant","error_description":"Bad Request"}""", "invalid_grant", true)]
    [InlineData(401, """{"error":"invalid_client"}""", "invalid_client", true)]
    [InlineData(503, "", "http_503", false)]
    public async Task The_token_endpoints_refusals_map_the_same_way(int status, string body, string code, bool permanent)
    {
        google.Answer((HttpStatusCode)status, body);

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().RefreshAsync("r", Ct));

        failure.Code.Should().Be(code);
        failure.Permanent.Should().Be(permanent);
    }

    [Fact]
    public async Task A_rate_limit_carries_the_retry_after_the_provider_gave()
    {
        google.Answer(HttpStatusCode.TooManyRequests, "", retryAfter: TimeSpan.FromSeconds(30));

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Permanent.Should().BeFalse();
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_timeout_and_an_unreachable_server_are_transient()
    {
        google.Throw(new TaskCanceledException("The request timed out.", new TimeoutException()));
        google.Throw(new HttpRequestException("No such host is known."));

        var provider = Provider();
        var timeout = await Assert.ThrowsAsync<CalendarApiException>(() => provider.FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));
        var unreachable = await Assert.ThrowsAsync<CalendarApiException>(() => provider.FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        (timeout.Code, timeout.Permanent).Should().Be(("timeout", false));
        (unreachable.Code, unreachable.Permanent).Should().Be(("unreachable", false));
    }

    [Fact]
    public async Task A_caller_who_cancels_gets_the_cancellation_not_a_provider_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        google.Throw(new TaskCanceledException("cancelled", null, cancelled.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), cancelled.Token));
    }

    [Fact]
    public async Task An_answer_that_is_not_the_expected_json_is_unreadable_and_transient()
    {
        google.Answer(HttpStatusCode.OK, "<html>Bad gateway</html>");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Code.Should().Be("unreadable_answer");
        failure.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task No_exception_carries_a_token_a_secret_or_an_address_even_when_the_provider_echoes_them()
    {
        var echo = $$$$"""{"error":"invalid_grant","error_description":"{{{{Token}}}} {{{{Secret}}}} {{{{Email}}}} candidate@example.org","error_uri":"{{{{Email}}}}"}""";
        var apiEcho = $$$$"""{"error":{"code":400,"message":"{{{{Token}}}} {{{{Email}}}}","errors":[{"reason":"{{{{Email}}}}","message":"{{{{Secret}}}}"}]}}""";
        google.Answer(HttpStatusCode.BadRequest, echo);
        google.Answer(HttpStatusCode.BadRequest, apiEcho);
        google.Answer(HttpStatusCode.InternalServerError, echo);
        google.Throw(new HttpRequestException($"connect failed for {Email}"));

        var provider = Provider();
        var failures = new List<CalendarApiException>
        {
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.RefreshAsync(Token, Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.CreateEventAsync(Token, Spec(invite: "candidate@example.org"), Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.CancelEventAsync(Token, "evt1", Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.FreeBusyAsync(Token, Start, Start.AddDays(1), Ct)),
        };

        foreach (var failure in failures)
        {
            failure.Message.Should().NotContain(Token).And.NotContain(Secret).And.NotContain("example.org");
            failure.Code.Should().NotContain(Token).And.NotContain(Secret).And.NotContain("@");
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
