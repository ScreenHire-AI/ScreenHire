using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// What the tests of the three ways to pick a time share (docs/architecture/interview-scheduling.md, "Three ways to pick a
/// time"): request bodies for each option, the invitation each one sends, and reading an email's calendar attachment.
/// </summary>
internal static class SchedulingOptionSupport
{
    /// <summary>Subjects of the invitation each option sends (the built-in templates' wording).</summary>
    public const string OurTimesSubject = "Pick a time";

    public const string TheirTimesSubject = "When could you do";

    public const string OurSuggestionSubject = "A time for your";

    public const string OptionsUpdatedSubject = "New options for your";

    public const string MoreTimesSubject = "More times for your";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];

    /// <summary>Hours on every weekday, in the shape the API takes (<c>WeeklyHours</c>).</summary>
    public static JsonObject WeekdayHours(string from, string to) => new()
    {
        ["days"] = new JsonArray([.. Weekdays.Select(day => (JsonNode)new JsonObject { ["day"] = day, ["from"] = from, ["to"] = to })]),
    };

    /// <summary>Offers the interview with how the candidate picks set to <paramref name="option"/> (and anything else <paramref name="more"/> adds).</summary>
    public static Task<ApiResult> ScheduleAsync(
        this InterviewWorld world, string option, Guid? applicationId = null, Guid[]? interviewers = null, Action<JsonObject>? more = null) =>
        world.ScheduleAsync(applicationId: applicationId, interviewers: interviewers, change: body =>
        {
            body["option"] = option;
            more?.Invoke(body);
        });

    /// <summary>Offers an interview with this option, waits for its invitation and returns the interview's id and the link token.</summary>
    public static async Task<(Guid InterviewId, string Token)> OfferedAsync(
        this InterviewWorld world, string option, string subject, Action<JsonObject>? more = null, Guid[]? interviewers = null)
    {
        var created = (await world.ScheduleAsync(option, interviewers: interviewers, more: more)).Expect(HttpStatusCode.Created);
        var invitation = await world.WaitForEmailAsync(world.CandidateEmail, subject);
        return (created.Id(), InterviewWorld.TokenIn(invitation.Text));
    }

    /// <summary>A time two to four days out at 10:00 UTC: one the team might propose, in the interview's window and whole minutes.</summary>
    public static DateTimeOffset DaysOut(int days) => new(DateTimeOffset.UtcNow.Date.AddDays(days).AddHours(10), TimeSpan.Zero);

    /// <summary>The first offered time that is at least two days away, so the candidate may still change it.</summary>
    public static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));

    public static IReadOnlyList<DateTimeOffset> TimesOf(this ApiResult page, string path) =>
        [.. page.At(path).EnumerateArray().Select(t => t.GetDateTimeOffset())];

    /// <summary>The address a signed-in person's email goes to.</summary>
    public static async Task<string> EmailOfAsync(this Tenant tenant) =>
        (await tenant.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;

    /// <summary>Waits for an email to <paramref name="address"/> whose calendar attachment holds <paramref name="method"/> (REQUEST or CANCEL) and returns that attachment's text.</summary>
    public static async Task<string> WaitForInviteAsync(this AppHostFixture fixture, string address, string method, string? subject = null)
    {
        var found = "";
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                found = (await fixture.Mailpit.MessagesAsync(address, Ct))
                    .Where(m => subject is null || m.Subject.Contains(subject, StringComparison.Ordinal))
                    .SelectMany(m => m.Files)
                    .Where(f => f.ContentType.StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase) && f.Content.Contains($"METHOD:{method}", StringComparison.Ordinal))
                    .Select(f => f.Content)
                    .FirstOrDefault() ?? "";
                return found.Length > 0;
            },
            $"an email to {address} with a calendar attachment for METHOD:{method}",
            Ct);
        return found;
    }

    /// <summary>The interviews of the world's application, as the owner reads them, with the one asked for.</summary>
    public static async Task<JsonElement> InterviewAsync(this InterviewWorld world, Guid interviewId, Guid? applicationId = null) =>
        (await world.InterviewsAsync(applicationId)).Single(i => i.GetProperty("id").GetGuid() == interviewId);
}
