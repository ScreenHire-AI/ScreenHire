using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// An owner and a second interviewer signed in to a new organization with a job, a candidate's application and a sandbox
/// calendar each, for the scheduling tests that go through the API, the worker and Mailpit
/// (docs/architecture/interview-scheduling.md). Every test makes its own, so no test sees another's people. Dispose it.
/// </summary>
internal sealed partial class InterviewWorld : IDisposable
{
    private readonly AppHostFixture fixture;

    private InterviewWorld(AppHostFixture fixture, Tenant owner, Tenant second, string slug, Guid jobId, Guid applicationId, string candidateEmail)
    {
        this.fixture = fixture;
        Owner = owner;
        Second = second;
        Slug = slug;
        JobId = jobId;
        ApplicationId = applicationId;
        CandidateEmail = candidateEmail;
        Careers = fixture.CreateHttpClient("api");
    }

    public Tenant Owner { get; }

    /// <summary>A recruiter in the owner's organization: the second interviewer.</summary>
    public Tenant Second { get; }

    public string Slug { get; }

    public Guid JobId { get; }

    public Guid ApplicationId { get; }

    public string CandidateEmail { get; }

    /// <summary>An anonymous client, as the careers server calls the public routes.</summary>
    public HttpClient Careers { get; }

    public Guid OrganizationId => Owner.OrganizationId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<InterviewWorld> CreateAsync(AppHostFixture fixture, bool connectCalendars = true)
    {
        var owner = await fixture.SignedInWithOrganizationAsync();
        Tenant? second = null;
        try
        {
            second = await fixture.InviteAndJoinAsync(owner, "Recruiter");
            var slug = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
            var jobId = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
            var email = ApiSession.NewEmail("candidate");
            var application = (await owner.CreateApplicationAsync(jobId, email)).Id();
            if (connectCalendars)
            {
                await ConnectCalendarAsync(owner);
                await ConnectCalendarAsync(second);
            }

            return new InterviewWorld(fixture, owner, second, slug, jobId, application, email);
        }
        catch
        {
            second?.Dispose();
            owner.Dispose();
            throw;
        }
    }

    /// <summary>Connects the member's sandbox calendar through the real round trip (see <see cref="ApiSession.ConnectSandboxCalendarAsync"/>).</summary>
    public static Task ConnectCalendarAsync(Tenant member) => member.Session.ConnectSandboxCalendarAsync(Ct);

    /// <summary>Another application on the same job, from a new candidate. Returns its id and the candidate's email.</summary>
    public async Task<(Guid ApplicationId, string Email)> AddApplicationAsync()
    {
        var email = ApiSession.NewEmail("candidate");
        return ((await Owner.CreateApplicationAsync(JobId, email)).Id(), email);
    }

    /// <summary>Offers an interview: 30 minutes, 09:00 to 17:00 UTC on weekdays, from now for two weeks.</summary>
    public Task<ApiResult> ScheduleAsync(
        Tenant? by = null, Guid[]? interviewers = null, Guid? applicationId = null, int minutes = 30, Action<JsonObject>? change = null)
    {
        var now = DateTimeOffset.UtcNow;
        var body = new JsonObject
        {
            ["interviewerUserIds"] = new JsonArray([.. (interviewers ?? [Owner.UserId, Second.UserId]).Select(id => JsonValue.Create(id))]),
            ["durationMinutes"] = minutes,
            ["windowStart"] = now,
            ["windowEnd"] = now.AddDays(14),
            ["timeZone"] = "UTC",
        };
        change?.Invoke(body);
        return (by ?? Owner).PostAsync($"/v1/applications/{applicationId ?? ApplicationId}/interviews", body);
    }

    /// <summary>Offers an interview and returns its id and the link token the candidate's email carries.</summary>
    public async Task<(Guid InterviewId, string Token)> ScheduledAsync(Guid? applicationId = null, string? email = null, Guid[]? interviewers = null)
    {
        var address = email ?? CandidateEmail;
        var earlier = (await fixture.Mailpit.MessagesAsync(address, Ct)).Count(m => m.Subject.Contains("Pick a time", StringComparison.Ordinal));
        var created = (await ScheduleAsync(applicationId: applicationId, interviewers: interviewers)).Expect(HttpStatusCode.Created);
        var invitation = await WaitForEmailAsync(address, "Pick a time", earlier + 1);
        return (created.Id(), TokenIn(invitation.Text));
    }

    /// <summary>Waits for an email to the address whose subject holds <paramref name="subject"/> and returns it.</summary>
    public async Task<MailpitMessage> WaitForEmailAsync(string address, string subject, int count = 1)
    {
        IReadOnlyList<MailpitMessage> found = [];
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                found = [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.Contains(subject, StringComparison.Ordinal))];
                return found.Count >= count;
            },
            $"an email to {address} about \"{subject}\"",
            Ct);
        return found[0];
    }

    public static string TokenIn(string text)
    {
        var match = Link().Match(text);
        match.Success.Should().BeTrue($"the email carries the interview link: {text}");
        return match.Groups["token"].Value;
    }

    public string PublicPath(string token, string? slug = null, string action = "") =>
        $"/v1/public/organizations/{slug ?? Slug}/interviews/{token}{action}";

    public async Task<ApiResult> PublicGetAsync(string token, string? slug = null, string query = "") =>
        await ReadAsync(await Careers.GetAsync(PublicPath(token, slug) + query, Ct));

    public async Task<ApiResult> PublicPostAsync(string token, string action, JsonNode? body = null, string? slug = null) =>
        await ReadAsync(await Careers.PostAsJsonAsync(PublicPath(token, slug, action), body ?? new JsonObject(), Ct));

    public Task<ApiResult> BookAsync(string token, DateTimeOffset start) => PublicPostAsync(token, "/book", new JsonObject { ["start"] = start });

    public Task<ApiResult> RescheduleAsync(string token, DateTimeOffset start) => PublicPostAsync(token, "/reschedule", new JsonObject { ["start"] = start });

    public Task<ApiResult> CandidateCancelAsync(string token) => PublicPostAsync(token, "/cancel");

    /// <summary>The start times the candidate's page offers, in order.</summary>
    public static IReadOnlyList<DateTimeOffset> SlotsOf(ApiResult page) =>
        [.. page.At("slots").EnumerateArray().Select(s => s.GetDateTimeOffset())];

    /// <summary>The interviews the owner sees for the application, newest first.</summary>
    public async Task<IReadOnlyList<JsonElement>> InterviewsAsync(Guid? applicationId = null, Tenant? by = null) =>
        [.. (await (by ?? Owner).GetAsync($"/v1/applications/{applicationId ?? ApplicationId}/interviews")).Expect(HttpStatusCode.OK).Body.EnumerateArray()];

    public async Task<IReadOnlyList<string>> TimelineTypesAsync(Guid? applicationId = null) =>
        [.. (await Owner.GetAsync($"/v1/applications/{applicationId ?? ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()!)];

    public Task WaitForTimelineAsync(string type, Guid? applicationId = null, int count = 1) =>
        WolverineStore.WaitUntilAsync(
            async () => (await TimelineTypesAsync(applicationId)).Count(t => t == type) >= count,
            $"the timeline to show {count} x {type}",
            Ct);

    /// <summary>The sandbox calendar events a member has, including cancelled ones.</summary>
    public async Task<IReadOnlyList<CalendarSandboxEvent>> EventsOfAsync(Guid userId)
    {
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        return await db.CalendarSandboxEvents.AsNoTracking().Where(e => e.UserId == userId).OrderBy(e => e.StartsAt).ToListAsync(Ct);
    }

    /// <summary>Waits until each of these members has exactly <paramref name="live"/> events that are not cancelled, and returns them.</summary>
    public async Task<IReadOnlyList<CalendarSandboxEvent>> WaitForLiveEventsAsync(Guid userId, int live, Func<CalendarSandboxEvent, bool>? also = null)
    {
        IReadOnlyList<CalendarSandboxEvent> events = [];
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                events = [.. (await EventsOfAsync(userId)).Where(e => !e.Deleted)];
                return events.Count == live && events.All(e => also?.Invoke(e) ?? true);
            },
            $"{live} live calendar event(s) for the interviewer",
            Ct);
        return events;
    }

    /// <summary>Waits until the attendee rows of the interview all have this sync status, as the dashboard would read them.</summary>
    public Task WaitForSyncAsync(Guid interviewId, string status, Guid? applicationId = null) =>
        WolverineStore.WaitUntilAsync(
            async () =>
            {
                var interview = (await InterviewsAsync(applicationId)).Single(i => i.GetProperty("id").GetGuid() == interviewId);
                return interview.GetProperty("interviewers").EnumerateArray().All(a => a.GetProperty("syncStatus").GetString() == status);
            },
            $"every interviewer's calendar event to be {status}",
            Ct);

    /// <summary>Changes the stored time of a booked interview, as if it had been booked for then.</summary>
    public async Task SetTimeAsync(Guid interviewId, DateTimeOffset start, DateTimeOffset end)
    {
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        await db.Database.ExecuteSqlAsync($"UPDATE interview_requests SET starts_at = {start}, ends_at = {end} WHERE id = {interviewId}", Ct);
    }

    /// <summary>Clears the attendees' saved event ids, as if the transaction that saved them had rolled back.</summary>
    public async Task ForgetEventIdsAsync(Guid interviewId)
    {
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        await db.Database.ExecuteSqlAsync($"UPDATE interview_attendees SET provider_event_id = NULL WHERE interview_id = {interviewId}", Ct);
    }

    /// <summary>Moves the window into the past, as if the candidate never picked a time in it.</summary>
    public async Task PassWindowAsync(Guid interviewId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE interview_requests SET window_start = {now.AddDays(-15)}, window_end = {now.AddDays(-1)} WHERE id = {interviewId}", Ct);
    }

    public async Task<InterviewRequest> StoredAsync(Guid interviewId)
    {
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        return await db.InterviewRequests.AsNoTracking().SingleAsync(r => r.Id == interviewId, Ct);
    }

    public async Task<IReadOnlyList<InterviewAttendee>> AttendeesAsync(Guid interviewId)
    {
        await using var db = await fixture.ContextAsync(OrganizationId, Ct);
        return await db.InterviewAttendees.AsNoTracking().Where(a => a.InterviewId == interviewId).OrderByDescending(a => a.IsOrganizer).ToListAsync(Ct);
    }

    /// <summary>The first slot a page offers that is at least a day later than the first, so two bookings differ.</summary>
    public static DateTimeOffset NextDayAfter(IReadOnlyList<DateTimeOffset> slots, DateTimeOffset first) =>
        slots.First(s => s >= first.AddDays(1));

    public void Dispose()
    {
        Careers.Dispose();
        Second.Dispose();
        Owner.Dispose();
    }

    private static async Task<ApiResult> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            using var document = text.Length == 0 ? null : JsonDocument.Parse(text);
            return new ApiResult(response.StatusCode, document?.RootElement.Clone() ?? default);
        }
    }

    [GeneratedRegex(@"https?://\S+/interviews/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex Link();
}
