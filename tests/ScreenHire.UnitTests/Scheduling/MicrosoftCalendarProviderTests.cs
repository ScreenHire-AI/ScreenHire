using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The real Microsoft provider against a stub standing in for Entra and Microsoft Graph: the authorize URL and its exact
/// scopes, the token calls (refresh tokens rotate), when the member is busy from calendarView with its paging and
/// <c>showAs</c> rules, creating, moving and cancelling an event with the transaction id that makes a create idempotent,
/// and how every kind of refusal maps to a <see cref="CalendarApiException"/>. No network, no real client id or secret.
/// </summary>
public sealed class MicrosoftCalendarProviderTests : IDisposable
{
    private const string Secret = "fictional-client-secret";

    private const string Token = "fictional-access-token";

    private const string Email = "member@example.org";

    private const string EventId = "AAMkAGI2fictional-event-id=";

    private static readonly Uri Redirect = new("https://app.example.org/v1/calendar/oauth/callback/microsoft");

    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly StubCalendarServer graph = new();

    private readonly List<MicrosoftCalendarProvider> providers = [];

    public void Dispose()
    {
        foreach (var provider in providers)
        {
            provider.Dispose();
        }

        graph.Dispose();
    }

    private MicrosoftCalendarProvider Provider()
    {
        var options = Options.Create(new CalendarOptions { Microsoft = new CalendarOAuthApp { ClientId = "fictional-client-id", ClientSecret = Secret } });
        var provider = new MicrosoftCalendarProvider(options, new FixedTime(Now), graph);
        providers.Add(provider);
        return provider;
    }

    private static EventSpec Spec(string id = "0123456789abcdef0123456789abcdef", bool meeting = true, params string[] invite) =>
        new(id, "Interview with Avery Quinn", "Candidate page: https://app.example.org/applications/1", Start, Start.AddHours(1), invite, meeting);

    private static string View(params string[] events) => $$$$"""{"value":[{{{{string.Join(',', events)}}}}]}""";

    private static string Block(string start, string end, string showAs = "busy", bool cancelled = false) =>
        $$$$"""{"start":{"dateTime":"{{{{start}}}}","timeZone":"UTC"},"end":{"dateTime":"{{{{end}}}}","timeZone":"UTC"},"showAs":"{{{{showAs}}}}","isCancelled":{{{{(cancelled ? "true" : "false")}}}}}""";

    [Fact]
    public void The_authorize_url_asks_for_calendar_read_write_and_offline_access_and_nothing_else()
    {
        var url = Provider().AuthorizeUrl("state value/1", Redirect);

        url.Host.Should().Be("login.microsoftonline.com");
        url.AbsolutePath.Should().Be("/common/oauth2/v2.0/authorize");
        var query = QueryHelpers.ParseQuery(url.Query);
        query.Keys.Should().BeEquivalentTo("client_id", "response_type", "redirect_uri", "response_mode", "scope", "state");
        query["client_id"].ToString().Should().Be("fictional-client-id");
        query["response_type"].ToString().Should().Be("code");
        query["response_mode"].ToString().Should().Be("query");
        query["redirect_uri"].ToString().Should().Be(Redirect.ToString());
        query["state"].ToString().Should().Be("state value/1");
        query["scope"].ToString().Should().Be("https://graph.microsoft.com/Calendars.ReadWrite offline_access");
        url.Query.Should().NotContain(Secret, "the secret never goes through the browser");
    }

    [Fact]
    public void The_scopes_are_exactly_calendars_read_write_and_offline_access_so_no_identity_scope_can_slip_in()
    {
        MicrosoftCalendarProvider.Scopes.Should().Equal("https://graph.microsoft.com/Calendars.ReadWrite", "offline_access");
        MicrosoftCalendarProvider.Scopes.Should().NotContain(s => s.Contains("User.Read") || s.Contains("openid") || s.Contains("email") || s.Contains("profile"));
    }

    [Fact]
    public async Task A_code_is_exchanged_with_a_form_post_and_the_account_address_is_the_calendar_owners()
    {
        graph.Ok($$$$"""{"token_type":"Bearer","scope":"Calendars.ReadWrite","expires_in":3599,"access_token":"{{{{Token}}}}","refresh_token":"fictional-refresh"}""");
        graph.Ok($$$$"""{"name":"Calendar","owner":{"name":"Avery Quinn","address":"{{{{Email}}}}"}}""");

        var grant = await Provider().ExchangeCodeAsync("the-code", Redirect, Ct);

        grant.Should().Be(new CalendarGrant(Token, "fictional-refresh", Now.AddSeconds(3599), Email));
        var exchange = graph.Calls[0];
        exchange.Method.Should().Be(HttpMethod.Post);
        exchange.Url.ToString().Should().Be("https://login.microsoftonline.com/common/oauth2/v2.0/token");
        exchange.Form.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["client_id"] = "fictional-client-id",
            ["client_secret"] = Secret,
            ["code"] = "the-code",
            ["redirect_uri"] = Redirect.ToString(),
            ["grant_type"] = "authorization_code",
            ["scope"] = "https://graph.microsoft.com/Calendars.ReadWrite offline_access",
        });
        exchange.Authorization.Should().BeNull();

        var calendar = graph.Calls[1];
        calendar.Method.Should().Be(HttpMethod.Get);
        calendar.Path.Should().Be("/v1.0/me/calendar");
        calendar.Url.Query.Should().Be("?$select=owner");
        calendar.Authorization.Should().Be($"Bearer {Token}");
    }

    [Fact]
    public async Task A_failing_owner_lookup_does_not_fail_the_connect()
    {
        graph.Ok($$$$"""{"scope":"Calendars.ReadWrite","expires_in":3600,"access_token":"{{{{Token}}}}","refresh_token":"r"}""");
        graph.Answer(HttpStatusCode.Forbidden, """{"error":{"code":"ErrorAccessDenied"}}""");

        var grant = await Provider().ExchangeCodeAsync("c", Redirect, Ct);

        grant.AccessToken.Should().Be(Token);
        grant.AccountEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_member_who_allowed_sign_in_but_refused_the_calendar_permission_has_not_connected()
    {
        graph.Ok($$$$"""{"scope":"openid profile","expires_in":3600,"access_token":"{{{{Token}}}}","refresh_token":"r"}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().ExchangeCodeAsync("c", Redirect, Ct));

        failure.Code.Should().Be("consent_required");
        failure.Permanent.Should().BeTrue();
        graph.Calls.Should().ContainSingle("nothing is read from a calendar the member did not allow");
    }

    [Fact]
    public async Task A_consent_error_from_the_token_endpoint_is_the_same_failed_connect()
    {
        graph.Answer(HttpStatusCode.BadRequest, """{"error":"consent_required","error_description":"AADSTS65001: The user or administrator has not consented."}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().ExchangeCodeAsync("c", Redirect, Ct));

        failure.Code.Should().Be("consent_required");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task A_connect_without_offline_access_has_no_refresh_token_and_is_refused()
    {
        graph.Ok($$$$"""{"scope":"Calendars.ReadWrite","expires_in":3600,"access_token":"{{{{Token}}}}"}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().ExchangeCodeAsync("c", Redirect, Ct));

        failure.Code.Should().Be("no_refresh_token");
    }

    [Fact]
    public async Task A_refresh_returns_the_rotated_refresh_token()
    {
        graph.Ok($$$$"""{"scope":"Calendars.ReadWrite","expires_in":"3600","access_token":"{{{{Token}}}}","refresh_token":"rotated-refresh"}""");

        var grant = await Provider().RefreshAsync("spent-refresh", Ct);

        grant.Should().Be(new CalendarGrant(Token, "rotated-refresh", Now.AddHours(1), null));
        graph.Calls.Should().ContainSingle().Which.Form.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["client_id"] = "fictional-client-id",
            ["client_secret"] = Secret,
            ["refresh_token"] = "spent-refresh",
            ["grant_type"] = "refresh_token",
            ["scope"] = "https://graph.microsoft.com/Calendars.ReadWrite offline_access",
        });
    }

    [Theory]
    [InlineData("""{"error":"invalid_grant","error_description":"AADSTS70008: The provided authorization code or refresh token has expired."}""")]
    [InlineData("""{"error":"invalid_grant","error_description":"AADSTS700082: The refresh token has expired due to inactivity."}""")]
    [InlineData("""{"error":"interaction_required","error_description":"AADSTS50076: Multi-factor authentication is required."}""")]
    public async Task An_expired_or_revoked_refresh_token_is_a_permanent_failure_to_refresh(string body)
    {
        graph.Answer(HttpStatusCode.BadRequest, body);

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().RefreshAsync("old", Ct));

        failure.Code.Should().BeOneOf("invalid_grant", "interaction_required");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task An_expiry_named_only_in_the_description_is_still_invalid_grant()
    {
        graph.Answer(HttpStatusCode.BadRequest, """{"error":"unknown_error","error_description":"AADSTS700082: The refresh token has expired due to inactivity."}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().RefreshAsync("old", Ct));

        failure.Code.Should().Be("invalid_grant");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task Revoking_does_nothing_and_asks_graph_nothing()
    {
        await Provider().RevokeAsync("a-token", Ct);

        graph.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Free_busy_reads_calendar_view_in_utc_and_maps_show_as()
    {
        graph.Ok(View(
            Block("2026-11-10T17:00:00.0000000", "2026-11-10T18:00:00.0000000", "busy"),
            Block("2026-11-10T14:00:00.0000000", "2026-11-10T14:30:00.0000000", "tentative"),
            Block("2026-11-11T09:00:00.0000000", "2026-11-11T17:00:00.0000000", "oof"),
            Block("2026-11-12T09:00:00.0000000", "2026-11-12T17:00:00.0000000", "workingElsewhere"),
            Block("2026-11-13T09:00:00.0000000", "2026-11-13T10:00:00.0000000", "free"),
            Block("2026-11-13T11:00:00.0000000", "2026-11-13T12:00:00.0000000", "busy", cancelled: true),
            Block("2026-11-13T13:00:00.0000000", "2026-11-13T14:00:00.0000000", "unknown")));

        var busy = await Provider().FreeBusyAsync(Token, Start, Start.AddDays(7), Ct);

        busy.Select(b => (b.Start.Day, b.Start.Hour, b.End.Hour)).Should().Equal((10, 14, 14), (10, 17, 18), (11, 9, 17), (12, 9, 17), (13, 13, 14));
        busy.Should().OnlyContain(b => b.Start.Offset == TimeSpan.Zero);
        var call = graph.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be("/v1.0/me/calendarView");
        call.Query["startDateTime"].Should().Be("2026-11-10T15:00:00.0000000Z");
        call.Query["endDateTime"].Should().Be("2026-11-17T15:00:00.0000000Z");
        call.Query["$select"].Should().Be("start,end,showAs,isCancelled");
        call.Authorization.Should().Be($"Bearer {Token}");
        call.Headers["Prefer"].Should().Be("outlook.timezone=\"UTC\"");
    }

    [Fact]
    public async Task Free_busy_follows_the_next_link_across_pages()
    {
        graph.Ok($$$$"""{"value":[{{{{Block("2026-11-10T17:00:00.0000000", "2026-11-10T18:00:00.0000000")}}}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/calendarView?startDateTime=x&$skiptoken=page2"}""");
        graph.Ok(View(Block("2026-11-11T17:00:00.0000000", "2026-11-11T18:00:00.0000000")));

        var busy = await Provider().FreeBusyAsync(Token, Start, Start.AddDays(7), Ct);

        busy.Should().HaveCount(2);
        graph.Calls.Should().HaveCount(2);
        graph.Calls[1].Url.Query.Should().Contain("skiptoken=page2");
        graph.Calls[1].Authorization.Should().Be($"Bearer {Token}");
        graph.Calls[1].Headers["Prefer"].Should().Be("outlook.timezone=\"UTC\"");
    }

    [Fact]
    public async Task A_next_link_on_another_host_is_not_followed_so_the_token_stays_with_graph()
    {
        graph.Ok("""{"value":[],"@odata.nextLink":"https://evil.example.org/steal"}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(7), Ct));

        failure.Code.Should().Be("unreadable_answer");
        graph.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Creating_an_organizer_event_asks_for_a_teams_meeting_and_invites_the_guest()
    {
        graph.Answer(HttpStatusCode.Created, $$$$"""{"id":"{{{{EventId}}}}","isOnlineMeeting":true,"onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/meetup-join/fictional"}}""");

        var result = await Provider().CreateEventAsync(Token, Spec(meeting: true, invite: "candidate@example.org"), Ct);

        result.Should().Be(new CalendarEventResult(EventId, "https://teams.microsoft.com/l/meetup-join/fictional"));
        var call = graph.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.ToString().Should().Be("https://graph.microsoft.com/v1.0/me/events");
        call.Authorization.Should().Be($"Bearer {Token}");
        var body = call.Json;
        body["subject"]!.GetValue<string>().Should().Be("Interview with Avery Quinn");
        body["body"]!["contentType"]!.GetValue<string>().Should().Be("text");
        body["body"]!["content"]!.GetValue<string>().Should().Contain("Candidate page");
        body["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-10T15:00:00.0000000");
        body["start"]!["timeZone"]!.GetValue<string>().Should().Be("UTC");
        body["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-10T16:00:00.0000000");
        body["transactionId"]!.GetValue<string>().Should().Be("0123456789abcdef0123456789abcdef");
        body["isOnlineMeeting"]!.GetValue<bool>().Should().BeTrue();
        body["onlineMeetingProvider"]!.GetValue<string>().Should().Be("teamsForBusiness");
        var attendee = body["attendees"]!.AsArray().Should().ContainSingle().Which!;
        attendee["emailAddress"]!["address"]!.GetValue<string>().Should().Be("candidate@example.org");
        attendee["type"]!.GetValue<string>().Should().Be("required");
    }

    [Fact]
    public async Task Creating_a_plain_event_sends_no_online_meeting_and_no_attendees()
    {
        graph.Answer(HttpStatusCode.Created, $$$$"""{"id":"{{{{EventId}}}}","onlineMeeting":null}""");

        var result = await Provider().CreateEventAsync(Token, Spec(meeting: false), Ct);

        result.MeetingUrl.Should().BeNull();
        var body = graph.Calls.Single().Json.AsObject();
        body.ContainsKey("attendees").Should().BeFalse();
        body.ContainsKey("isOnlineMeeting").Should().BeFalse();
        body.ContainsKey("onlineMeetingProvider").Should().BeFalse();
        body["transactionId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_transaction_id_is_the_idempotency_id_so_it_is_stable_and_differs_per_event()
    {
        graph.Answer(HttpStatusCode.Created, """{"id":"x"}""").Answer(HttpStatusCode.Created, """{"id":"x"}""").Answer(HttpStatusCode.Created, """{"id":"x"}""");

        var provider = Provider();
        await provider.CreateEventAsync(Token, Spec(id: "interview-one", meeting: false), Ct);
        await provider.CreateEventAsync(Token, Spec(id: "interview-one", meeting: false), Ct);
        await provider.CreateEventAsync(Token, Spec(id: "interview-two", meeting: false), Ct);

        var ids = graph.Calls.Select(c => c.Json["transactionId"]!.GetValue<string>()).ToList();
        ids[0].Should().Be(ids[1]).And.NotBe(ids[2]);
    }

    [Fact]
    public async Task A_repeat_that_graph_answers_with_the_existing_event_is_the_same_result()
    {
        graph.Ok($$$$"""{"id":"{{{{EventId}}}}","transactionId":"0123456789abcdef0123456789abcdef","onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/meetup-join/first"}}""");

        var result = await Provider().CreateEventAsync(Token, Spec(), Ct);

        result.Should().Be(new CalendarEventResult(EventId, "https://teams.microsoft.com/l/meetup-join/first"));
    }

    [Theory]
    [InlineData(409, """{"error":{"code":"ErrorItemExists"}}""")]
    [InlineData(400, """{"error":{"code":"ErrorInvalidTransactionId"}}""")]
    public async Task A_repeat_that_graph_refuses_is_answered_by_finding_the_event_with_that_transaction_id(int status, string body)
    {
        graph.Answer((HttpStatusCode)status, body);
        graph.Ok($$$$"""
            {"value":[
              {"id":"other","transactionId":"someone-else","isCancelled":false},
              {"id":"{{{{EventId}}}}","transactionId":"0123456789abcdef0123456789abcdef","isCancelled":false,"onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/meetup-join/first"}}]}
            """);

        var result = await Provider().CreateEventAsync(Token, Spec(), Ct);

        result.Should().Be(new CalendarEventResult(EventId, "https://teams.microsoft.com/l/meetup-join/first"));
        var lookup = graph.Calls[1];
        lookup.Path.Should().Be("/v1.0/me/calendarView");
        lookup.Query["startDateTime"].Should().Be("2026-11-10T15:00:00.0000000Z");
        lookup.Query["endDateTime"].Should().Be("2026-11-10T16:00:00.0000000Z");
    }

    [Fact]
    public async Task An_event_with_no_saved_id_is_found_by_its_transaction_id_in_the_interviews_window()
    {
        graph.Ok($$$$"""
            {"value":[
              {"id":"other","transactionId":"someone-else","isCancelled":false},
              {"id":"{{{{EventId}}}}","transactionId":"0123456789abcdef0123456789abcdef","isCancelled":false}]}
            """);

        var id = await Provider().FindEventIdAsync(Token, "0123456789abcdef0123456789abcdef", Start, Start.AddHours(1), Ct);

        id.Should().Be(EventId);
        graph.Calls.Should().ContainSingle().Which.Path.Should().Be("/v1.0/me/calendarView");
    }

    [Fact]
    public async Task An_event_that_was_never_made_is_not_found()
    {
        graph.Ok(View());

        (await Provider().FindEventIdAsync(Token, "0123456789abcdef0123456789abcdef", Start, Start.AddHours(1), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Removing_deletes_the_event_and_an_event_already_gone_is_not_an_error()
    {
        graph.Answer(HttpStatusCode.NoContent);
        graph.Answer(HttpStatusCode.NotFound);

        await Provider().RemoveEventAsync(Token, EventId, Ct);
        await Provider().RemoveEventAsync(Token, EventId, Ct);

        graph.Calls.Should().HaveCount(2).And.OnlyContain(c => c.Method == HttpMethod.Delete);
        graph.Calls[0].Url.AbsolutePath.Should().Be("/v1.0/me/events/AAMkAGI2fictional-event-id%3D");
    }

    [Fact]
    public async Task A_refused_repeat_whose_event_cannot_be_found_is_a_permanent_failure_not_a_second_event()
    {
        graph.Answer(HttpStatusCode.Conflict, """{"error":{"code":"ErrorItemExists"}}""");
        graph.Ok(View());

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().CreateEventAsync(Token, Spec(), Ct));

        failure.Code.Should().Be("duplicate_not_found");
        failure.Permanent.Should().BeTrue();
    }

    [Fact]
    public async Task Moving_an_event_patches_its_times_and_keeps_the_meeting_link()
    {
        graph.Ok($$$$"""{"id":"{{{{EventId}}}}","isCancelled":false,"onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/meetup-join/kept"}}""");

        var result = await Provider().MoveEventAsync(Token, EventId, Start.AddDays(1), Start.AddDays(1).AddHours(1), Ct);

        result.Should().Be(new CalendarEventResult(EventId, "https://teams.microsoft.com/l/meetup-join/kept"));
        var call = graph.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Patch);
        call.Url.AbsolutePath.Should().Be("/v1.0/me/events/AAMkAGI2fictional-event-id%3D");
        call.Json["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-11T15:00:00.0000000");
        call.Json["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-11-11T16:00:00.0000000");
        call.Json.AsObject().Count.Should().Be(2, "only the times change");
    }

    [Fact]
    public async Task Moving_an_event_that_is_gone_is_a_retryable_event_not_found()
    {
        graph.Answer(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound"}}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().MoveEventAsync(Token, EventId, Start, Start.AddHours(1), Ct));

        failure.Code.Should().Be("event_not_found");
        failure.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task Moving_an_event_that_is_cancelled_is_event_not_found_too()
    {
        graph.Ok($$$$"""{"id":"{{{{EventId}}}}","isCancelled":true}""");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().MoveEventAsync(Token, EventId, Start, Start.AddHours(1), Ct));

        failure.Code.Should().Be("event_not_found");
    }

    [Fact]
    public async Task Cancelling_uses_the_cancel_action_so_the_guests_are_told()
    {
        graph.Answer(HttpStatusCode.Accepted);

        await Provider().CancelEventAsync(Token, EventId, Ct);

        var call = graph.Calls.Should().ContainSingle().Which;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.AbsolutePath.Should().Be("/v1.0/me/events/AAMkAGI2fictional-event-id%3D/cancel");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(405)]
    public async Task An_event_that_cannot_be_cancelled_as_a_meeting_is_deleted(int status)
    {
        graph.Answer((HttpStatusCode)status, """{"error":{"code":"ErrorCannotDeleteOrCancel"}}""");
        graph.Answer(HttpStatusCode.NoContent);

        await Provider().CancelEventAsync(Token, EventId, Ct);

        graph.Calls.Should().HaveCount(2);
        graph.Calls[1].Method.Should().Be(HttpMethod.Delete);
        graph.Calls[1].Url.AbsolutePath.Should().Be("/v1.0/me/events/AAMkAGI2fictional-event-id%3D");
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Cancelling_an_event_that_is_already_gone_is_not_an_error(int status)
    {
        graph.Answer((HttpStatusCode)status, """{"error":{"code":"ErrorItemNotFound"}}""");

        await Provider().CancelEventAsync(Token, EventId, Ct);

        graph.Calls.Should().ContainSingle("a missing event needs no delete after the cancel");
    }

    [Fact]
    public async Task A_delete_of_an_event_that_vanished_in_between_is_not_an_error_either()
    {
        graph.Answer(HttpStatusCode.BadRequest, """{"error":{"code":"ErrorCannotDeleteOrCancel"}}""");
        graph.Answer(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound"}}""");

        await Provider().CancelEventAsync(Token, EventId, Ct);
    }

    [Fact]
    public async Task A_cancel_that_fails_for_another_reason_throws_without_deleting()
    {
        graph.Answer(HttpStatusCode.ServiceUnavailable, "");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().CancelEventAsync(Token, EventId, Ct));

        failure.Permanent.Should().BeFalse();
        graph.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData(401, """{"error":{"code":"InvalidAuthenticationToken","message":"Access token has expired."}}""", "InvalidAuthenticationToken", true)]
    [InlineData(401, "", "http_401", true)]
    [InlineData(403, """{"error":{"code":"ErrorAccessDenied"}}""", "ErrorAccessDenied", true)]
    [InlineData(429, """{"error":{"code":"activityLimitReached"}}""", "activityLimitReached", false)]
    [InlineData(429, "", "rate_limited", false)]
    [InlineData(500, "", "http_500", false)]
    [InlineData(503, """{"error":{"code":"serviceNotAvailable"}}""", "serviceNotAvailable", false)]
    [InlineData(504, "", "http_504", false)]
    [InlineData(400, """{"error":{"code":"ErrorInvalidRequest"}}""", "ErrorInvalidRequest", true)]
    [InlineData(404, """{"error":{"code":"ErrorItemNotFound"}}""", "ErrorItemNotFound", true)]
    public async Task Every_refusal_maps_to_a_code_and_whether_a_retry_could_help(int status, string body, string code, bool permanent)
    {
        graph.Answer((HttpStatusCode)status, body);

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Code.Should().Be(code);
        failure.Permanent.Should().Be(permanent);
    }

    [Fact]
    public async Task A_throttled_call_carries_the_retry_after_graph_gave()
    {
        graph.Answer(HttpStatusCode.TooManyRequests, """{"error":{"code":"activityLimitReached"}}""", retryAfter: TimeSpan.FromSeconds(45));

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().CreateEventAsync(Token, Spec(), Ct));

        failure.Permanent.Should().BeFalse();
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task A_timeout_and_an_unreachable_server_are_transient()
    {
        graph.Throw(new TaskCanceledException("The request timed out.", new TimeoutException()));
        graph.Throw(new HttpRequestException("No such host is known."));

        var provider = Provider();
        var timeout = await Assert.ThrowsAsync<CalendarApiException>(() => provider.FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));
        var unreachable = await Assert.ThrowsAsync<CalendarApiException>(() => provider.RefreshAsync("r", Ct));

        (timeout.Code, timeout.Permanent).Should().Be(("timeout", false));
        (unreachable.Code, unreachable.Permanent).Should().Be(("unreachable", false));
    }

    [Fact]
    public async Task An_answer_that_is_not_the_expected_json_is_unreadable_and_transient()
    {
        graph.Answer(HttpStatusCode.OK, "<html>Bad gateway</html>");

        var failure = await Assert.ThrowsAsync<CalendarApiException>(() => Provider().FreeBusyAsync(Token, Start, Start.AddDays(1), Ct));

        failure.Code.Should().Be("unreadable_answer");
        failure.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task No_exception_carries_a_token_a_secret_or_an_address_even_when_the_provider_echoes_them()
    {
        var oauthEcho = $$$$"""{"error":"invalid_grant","error_description":"{{{{Token}}}} {{{{Secret}}}} {{{{Email}}}} candidate@example.org"}""";
        var graphEcho = $$$$"""{"error":{"code":"{{{{Email}}}}","message":"{{{{Token}}}} {{{{Email}}}} {{{{Secret}}}}"}}""";
        graph.Answer(HttpStatusCode.BadRequest, oauthEcho);
        graph.Answer(HttpStatusCode.BadRequest, graphEcho);
        graph.Answer(HttpStatusCode.InternalServerError, graphEcho);
        graph.Throw(new HttpRequestException($"connect failed for {Email}"));

        var provider = Provider();
        var failures = new List<CalendarApiException>
        {
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.RefreshAsync(Token, Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.CreateEventAsync(Token, Spec(invite: "candidate@example.org"), Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.FreeBusyAsync(Token, Start, Start.AddDays(1), Ct)),
            await Assert.ThrowsAsync<CalendarApiException>(() => provider.MoveEventAsync(Token, EventId, Start, Start.AddHours(1), Ct)),
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
