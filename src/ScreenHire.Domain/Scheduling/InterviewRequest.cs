using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Scheduling;

public enum InterviewStatus
{
    /// <summary>Waiting for the candidate to pick a time inside the window.</summary>
    Open,

    /// <summary>The candidate picked a time; <see cref="InterviewRequest.StartsAt"/> holds it.</summary>
    Booked,

    /// <summary>The team or the candidate cancelled it.</summary>
    Cancelled,

    /// <summary>The window passed with no booking. Stored lazily: <see cref="InterviewRequest.EffectiveStatus"/> reads it without a timer.</summary>
    Expired,
}

/// <summary>
/// How the candidate picks a time (docs/architecture/interview-scheduling.md, "Three ways to pick a time"), chosen for each
/// interview before the candidate is told and changeable while it waits.
/// </summary>
public enum InterviewOption
{
    /// <summary>A. Pick from our times: open slots from the interviewers' calendars or the interview's blocks, booked at once.</summary>
    OurTimes,

    /// <summary>B. Tell us when you're free: the candidate suggests 3 to 5 times within the limits, and the team books one.</summary>
    TheirTimes,

    /// <summary>C. We'll suggest a time: the team picks one or two times, and the candidate accepts one or asks for another.</summary>
    OurSuggestion,
}

/// <summary>
/// The option an interview is offered with and what it needs. <paramref name="Hours"/> is the hours the times may fall in
/// (null keeps the request's working hours, weekdays <c>DayStart</c> to <c>DayEnd</c>). <paramref name="UseBlocks"/> (A only)
/// says the hours are interview blocks the business set, so the interviewers need no calendar. <paramref name="ProposedTimes"/>
/// are C's hand-picked times; <paramref name="ExtraTimes"/> are one-off times added to A's.
/// </summary>
public sealed record InterviewPick(
    InterviewOption Option,
    WeeklyHours? Hours = null,
    bool UseBlocks = false,
    IReadOnlyList<DateTimeOffset>? ProposedTimes = null,
    IReadOnlyList<DateTimeOffset>? ExtraTimes = null)
{
    /// <summary>Today's way, and every request's before there were options: A from the interviewers' calendars.</summary>
    public static InterviewPick FromCalendars { get; } = new(InterviewOption.OurTimes);
}

/// <summary>
/// One interview offered to one application's candidate (docs/architecture/interview-scheduling.md): who interviews,
/// how long, the window and working hours the candidate may pick from, and once booked, the time. There is at most one
/// Open or Booked request per application (a partial unique index); history stays as Cancelled or Expired rows.
/// </summary>
/// <remarks>
/// Methods take <c>now</c> and change only this row. The caller books under the interviewers' locks
/// (<c>InterviewBooking</c>), writes the attendees and publishes the messages that put events on calendars.
/// </remarks>
public sealed class InterviewRequest : IApplicationScoped
{
    /// <summary>The interview lengths a request may have, in minutes.</summary>
    public static readonly IReadOnlyList<int> AllowedDurations = [15, 30, 45, 60, 90, 120];

    /// <summary>The longest window a candidate may pick from.</summary>
    public const int MaxWindowDays = 90;

    public const int MaxTimeZoneLength = 64;

    public const int MaxMeetingUrlLength = 500;

    public const int MaxSourceKeyLength = 200;

    public static readonly TimeOnly DefaultDayStart = new(9, 0);

    public static readonly TimeOnly DefaultDayEnd = new(17, 0);

    /// <summary>The fewest and most times a candidate suggests (B).</summary>
    public const int MinSuggestedTimes = 3;

    public const int MaxSuggestedTimes = 5;

    /// <summary>The most times the team picks for the candidate (C).</summary>
    public const int MaxProposedTimes = 2;

    /// <summary>The most one-off times added to A's.</summary>
    public const int MaxExtraTimes = 3;

    public const int MaxNoteLength = 500;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid JobId { get; init; }

    public Guid ApplicationId { get; init; }

    public int DurationMinutes { get; init; }

    /// <summary>No interview starts before this instant. <see cref="OfferNewTimes"/> replaces it.</summary>
    public DateTimeOffset WindowStart { get; private set; }

    /// <summary>No interview ends after this instant. An Open request past it reads as Expired.</summary>
    public DateTimeOffset WindowEnd { get; private set; }

    /// <summary>
    /// The first instant any window of this request has covered. The windows can be replaced (<see cref="OfferNewTimes"/>), and a
    /// calendar event whose id was never saved is looked up across the whole span, so it is found at any time the
    /// interview has had.
    /// </summary>
    public DateTimeOffset LookupStart { get; private set; }

    /// <summary>The last instant any window of this request has covered (see <see cref="LookupStart"/>).</summary>
    public DateTimeOffset LookupEnd { get; private set; }

    /// <summary>
    /// When the team last offered new times for this booked interview, until the candidate picks one (or it is cancelled).
    /// While set, the booked time stays held, the candidate may move or cancel whatever the minimum notice, and the offered
    /// times come from the new window. It is not a change to the booking, so <see cref="Revision"/> does not move.
    /// </summary>
    public DateTimeOffset? NewTimesOfferedAt { get; private set; }

    /// <summary>The IANA time zone the working hours are in, and the candidate page's display default.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Working hours, local time in <see cref="TimeZone"/>, weekdays only.</summary>
    public TimeOnly DayStart { get; init; } = DefaultDayStart;

    public TimeOnly DayEnd { get; init; } = DefaultDayEnd;

    public InterviewStatus Status { get; private set; }

    public DateTimeOffset? StartsAt { get; private set; }

    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The Meet or Teams link, once the organizer's event has one.</summary>
    public string? MeetingUrl { get; private set; }

    /// <summary>
    /// Counts bookings, reschedules and cancellations. The idempotency keys of the messages that sync calendars and the
    /// provider event ids derive from it, so a redelivered message is a no-op and a later change is a new intent.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>The person who scheduled it; null when a workflow action did.</summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>The workflow action's key (run, action index), so running the action again finds this request.</summary>
    public string? SourceKey { get; init; }

    /// <summary>
    /// What the meeting is called, copied from the application's stage when the request was made (<see cref="WorkflowStage.MeetingName"/>),
    /// so a candidate who moves on keeps the name they were invited under. Null reads as "interview", which is every request made before stages could name one.
    /// </summary>
    public string? MeetingName { get; init; }

    /// <summary>How the candidate picks a time. Every request made before there were options is <see cref="InterviewOption.OurTimes"/>.</summary>
    public InterviewOption Option { get; private set; }

    /// <summary>The hours the times fall in, any day of the week; null reads as weekdays <see cref="DayStart"/> to <see cref="DayEnd"/> (<see cref="EffectiveHours"/>).</summary>
    public WeeklyHours? Hours { get; private set; }

    /// <summary>A only: the hours are interview blocks the business set, so an interviewer needs no calendar for times to be offered.</summary>
    public bool UseBlocks { get; private set; }

    /// <summary>C's times, picked by the team for the candidate to accept.</summary>
    public IReadOnlyList<DateTimeOffset> ProposedTimes { get; private set; } = [];

    /// <summary>One-off times added to A's offered times (the fallback when nothing is open).</summary>
    public IReadOnlyList<DateTimeOffset> ExtraTimes { get; private set; } = [];

    /// <summary>B's times, suggested by the candidate for the team to book one.</summary>
    public IReadOnlyList<DateTimeOffset> CandidateTimes { get; private set; } = [];

    public DateTimeOffset? CandidateTimesAt { get; private set; }

    /// <summary>C: the candidate asked for another time, with an optional note.</summary>
    public DateTimeOffset? AnotherTimeAskedAt { get; private set; }

    public string? CandidateNote { get; private set; }

    /// <summary>B: the team's message when it asked the candidate for more times.</summary>
    public string? TeamNote { get; private set; }

    /// <summary>When the team last changed how the candidate picks; null when it never has.</summary>
    public DateTimeOffset? OptionChangedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public bool CancelledByCandidate { get; private set; }

    /// <summary>What the meeting is called in words: <see cref="MeetingName"/>, or "interview".</summary>
    public string MeetingNameOrDefault() => string.IsNullOrWhiteSpace(MeetingName) ? WorkflowStage.DefaultMeetingName : MeetingName;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    /// <summary>The zone the working hours are in; UTC if this host no longer knows the id.</summary>
    public TimeZoneInfo Zone => TryFindZone(TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The hours the times fall in: <see cref="Hours"/>, or weekdays <see cref="DayStart"/> to <see cref="DayEnd"/>.</summary>
    public WeeklyHours EffectiveHours => Hours ?? WeeklyHours.Weekdays(DayStart, DayEnd);

    /// <summary>The option and what it needs, as it stands.</summary>
    public InterviewPick Pick => new(Option, Hours, UseBlocks, ProposedTimes, ExtraTimes);

    /// <summary>
    /// The first thing wrong with these inputs in plain words, or null when a request can be made from them. The API turns
    /// it into a validation problem; <see cref="Create"/> throws it.
    /// </summary>
    public static string? Problem(
        int durationMinutes, DateTimeOffset windowStart, DateTimeOffset windowEnd, string? timeZone, TimeOnly dayStart, TimeOnly dayEnd, DateTimeOffset now)
    {
        if (!AllowedDurations.Contains(durationMinutes))
        {
            return $"An interview lasts {string.Join(", ", AllowedDurations)} minutes.";
        }

        if (windowEnd <= windowStart)
        {
            return "The window must end after it starts.";
        }

        if (windowEnd - windowStart > TimeSpan.FromDays(MaxWindowDays))
        {
            return $"The window can be at most {MaxWindowDays} days.";
        }

        if (windowEnd <= now)
        {
            return "The window must end in the future.";
        }

        if (!TryFindZone(timeZone, out _))
        {
            return "Choose a time zone.";
        }

        if (dayEnd <= dayStart)
        {
            return "Working hours must end after they start.";
        }

        return dayEnd - dayStart < TimeSpan.FromMinutes(durationMinutes) ? "Working hours are shorter than the interview." : null;
    }

    /// <summary>
    /// The first thing wrong with offering <paramref name="pick"/> for an interview of <paramref name="durationMinutes"/> in the
    /// window, in plain words, or null. The window itself is <see cref="Problem"/>'s.
    /// </summary>
    public static string? PickProblem(InterviewPick pick, int durationMinutes, DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (!Enum.IsDefined(pick.Option))
        {
            return "Choose how the candidate picks a time.";
        }

        if (pick.Hours?.Problem(durationMinutes) is { } hours)
        {
            return hours;
        }

        if (pick.UseBlocks && (pick.Option != InterviewOption.OurTimes || pick.Hours is null))
        {
            return "Interview blocks need their days and hours.";
        }

        var proposed = pick.ProposedTimes ?? [];
        var extra = pick.ExtraTimes ?? [];
        if (pick.Option == InterviewOption.OurSuggestion)
        {
            if (proposed.Count is < 1 or > MaxProposedTimes)
            {
                return $"Pick one or {MaxProposedTimes} times for the candidate.";
            }
        }
        else if (proposed.Count > 0)
        {
            return "Only We'll suggest a time has picked times.";
        }

        if (pick.Option != InterviewOption.OurTimes && extra.Count > 0)
        {
            return "Only Pick from our times has one-off times.";
        }

        if (extra.Count > MaxExtraTimes)
        {
            return $"Add at most {MaxExtraTimes} one-off times.";
        }

        var length = TimeSpan.FromMinutes(durationMinutes);
        foreach (var times in new[] { proposed, extra })
        {
            if (times.Distinct().Count() != times.Count)
            {
                return "Each time can be picked once.";
            }

            if (times.Any(t => t.UtcTicks % TimeSpan.TicksPerMinute != 0))
            {
                return "Times are in whole minutes.";
            }

            if (times.Any(t => t <= now))
            {
                return "Each time must be in the future.";
            }

            if (times.Any(t => t < windowStart || t + length > windowEnd))
            {
                return "Each time must be inside the days the candidate picks from.";
            }
        }

        return null;
    }

    /// <summary>The zone for an IANA id, or false when this host does not know it.</summary>
    public static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxTimeZoneLength)
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>A new Open request for <paramref name="application"/>. Working hours default to 09:00 to 17:00.</summary>
    public static InterviewRequest Create(
        JobApplication application,
        int durationMinutes,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string timeZone,
        Guid? createdByUserId,
        DateTimeOffset now,
        TimeOnly? dayStart = null,
        TimeOnly? dayEnd = null,
        string? sourceKey = null,
        string? meetingName = null,
        InterviewPick? pick = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var start = dayStart ?? DefaultDayStart;
        var end = dayEnd ?? DefaultDayEnd;
        pick ??= InterviewPick.FromCalendars;
        if (Problem(durationMinutes, windowStart, windowEnd, timeZone, start, end, now) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        if (PickProblem(pick, durationMinutes, windowStart, windowEnd, now) is { } pickProblem)
        {
            throw new ArgumentException(pickProblem);
        }

        if (sourceKey is { Length: > MaxSourceKeyLength })
        {
            throw new ArgumentException("The source key is too long.", nameof(sourceKey));
        }

        if (meetingName is { Length: > WorkflowStage.MaxMeetingNameLength })
        {
            throw new ArgumentException("The meeting name is too long.", nameof(meetingName));
        }

        return new InterviewRequest
        {
            OrganizationId = application.OrganizationId,
            JobId = application.JobId,
            ApplicationId = application.Id,
            DurationMinutes = durationMinutes,
            WindowStart = windowStart.ToUniversalTime(),
            WindowEnd = windowEnd.ToUniversalTime(),
            LookupStart = windowStart.ToUniversalTime(),
            LookupEnd = windowEnd.ToUniversalTime(),
            TimeZone = timeZone,
            DayStart = start,
            DayEnd = end,
            Status = InterviewStatus.Open,
            CreatedByUserId = createdByUserId,
            SourceKey = sourceKey,
            MeetingName = string.IsNullOrWhiteSpace(meetingName) ? null : meetingName.Trim(),
            Option = pick.Option,
            Hours = pick.Hours,
            UseBlocks = pick.UseBlocks,
            ProposedTimes = Instants(pick.ProposedTimes),
            ExtraTimes = Instants(pick.ExtraTimes),
            CreatedAt = now,
        };
    }

    /// <summary>The status as it reads at <paramref name="now"/>: an Open request past its window is Expired, with no timer to say so.</summary>
    public InterviewStatus EffectiveStatus(DateTimeOffset now) =>
        Status == InterviewStatus.Open && now >= WindowEnd ? InterviewStatus.Expired : Status;

    /// <summary>Stores Expired for an Open request past its window (the partial unique index sees the stored status). False when nothing changed.</summary>
    public bool MarkExpired(DateTimeOffset now)
    {
        if (Status != InterviewStatus.Open || EffectiveStatus(now) != InterviewStatus.Expired)
        {
            return false;
        }

        Status = InterviewStatus.Expired;
        return true;
    }

    /// <summary>The candidate (or a person for them) picks <paramref name="start"/>. Only an Open request inside its window books.</summary>
    public void Book(DateTimeOffset start, DateTimeOffset now)
    {
        if (EffectiveStatus(now) != InterviewStatus.Open)
        {
            throw new InvalidOperationException("Only an open interview can be booked.");
        }

        Place(start);
        Status = InterviewStatus.Booked;
    }

    /// <summary>Moves a booked interview to <paramref name="start"/>.</summary>
    public void Reschedule(DateTimeOffset start, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Booked)
        {
            throw new InvalidOperationException("Only a booked interview can be rescheduled.");
        }

        if (start <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "An interview cannot be moved into the past.");
        }

        Place(start);
        NewTimesOfferedAt = null;
    }

    /// <summary>
    /// The first thing wrong with offering the new window <paramref name="windowStart"/> to <paramref name="windowEnd"/> for this
    /// interview, in plain words, or null. The window follows the same rules as a new request's, in this request's length,
    /// zone and working hours.
    /// </summary>
    public string? NewTimesProblem(DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now) =>
        Problem(DurationMinutes, windowStart, windowEnd, TimeZone, DayStart, DayEnd, now);

    /// <summary>
    /// The team offers the candidate new times for a booked interview: the window the candidate picks from is replaced, and
    /// the booked time stays as it is, so it stays held until the candidate picks one (<see cref="Reschedule"/>). Offering again
    /// replaces the window again. Not a booking change, so <see cref="Revision"/> stays.
    /// </summary>
    public void OfferNewTimes(DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Booked)
        {
            throw new InvalidOperationException("Only a booked interview can be offered new times.");
        }

        if (NewTimesProblem(windowStart, windowEnd, now) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        // Offering new days is A over them, in the interview's own hours.
        Apply(new InterviewPick(InterviewOption.OurTimes, Hours, UseBlocks), windowStart, windowEnd);
        NewTimesOfferedAt = now;
    }

    /// <summary>
    /// Whether the candidate is choosing a time at <paramref name="now"/>: an Open interview inside its window, or a booked one
    /// the team asked to move (<see cref="HasPendingNewTimes"/>).
    /// </summary>
    public bool IsPicking(DateTimeOffset now) => EffectiveStatus(now) == InterviewStatus.Open || HasPendingNewTimes(now);

    /// <summary>
    /// Changes how the candidate picks (Change how they pick, on an Open interview), or moves a booked one with any of the
    /// three options: the booked time stays held until a new one is booked, as <see cref="OfferNewTimes"/> does. The old
    /// option's state goes (the candidate's times and both notes); the candidate's link stays the same.
    /// </summary>
    public void ChangeOption(InterviewPick pick, DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pick);
        var status = EffectiveStatus(now);
        if (status is not (InterviewStatus.Open or InterviewStatus.Booked))
        {
            throw new InvalidOperationException("Only an open or booked interview can change how the candidate picks.");
        }

        if (ChangeProblem(pick, windowStart, windowEnd, now) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        Apply(pick, windowStart, windowEnd);
        OptionChangedAt = now;
        if (status == InterviewStatus.Booked)
        {
            NewTimesOfferedAt = now;
        }
    }

    /// <summary>The first thing wrong with <see cref="ChangeOption"/> to <paramref name="pick"/> over the window, in plain words, or null.</summary>
    public string? ChangeProblem(InterviewPick pick, DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset now) =>
        Problem(DurationMinutes, windowStart, windowEnd, TimeZone, DayStart, DayEnd, now)
            ?? PickProblem(pick, DurationMinutes, windowStart, windowEnd, now);

    /// <summary>
    /// The candidate suggests the times they are free (B). <paramref name="allowed"/> is every time within the limits (the
    /// window, the hours and the minimum notice, on the step grid), and each suggestion must be one of them.
    /// </summary>
    public void SuggestTimes(IReadOnlyList<DateTimeOffset> times, IReadOnlyCollection<DateTimeOffset> allowed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(allowed);
        if (Option != InterviewOption.TheirTimes || !IsPicking(now))
        {
            throw new InvalidOperationException("Only an interview waiting for the candidate's times takes them.");
        }

        if (SuggestionProblem(times, allowed) is { } problem)
        {
            throw new ArgumentException($"The suggested times were refused: {problem}.");
        }

        CandidateTimes = [.. times.Select(t => t.ToUniversalTime()).Order()];
        CandidateTimesAt = now;
        TeamNote = null;
    }

    /// <summary>
    /// Why <paramref name="times"/> are not a set of suggestions, or null. A candidate suggests <see cref="MinSuggestedTimes"/>
    /// to <see cref="MaxSuggestedTimes"/> times, each one allowed, or every allowed time when there are fewer than three.
    /// </summary>
    public static SuggestionFault? SuggestionProblem(IReadOnlyList<DateTimeOffset> times, IReadOnlyCollection<DateTimeOffset> allowed)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(allowed);
        if (times.Count < Math.Max(1, Math.Min(MinSuggestedTimes, allowed.Count)))
        {
            return SuggestionFault.TooFew;
        }

        if (times.Count > MaxSuggestedTimes)
        {
            return SuggestionFault.TooMany;
        }

        return times.Distinct().Count() != times.Count || times.Any(t => !allowed.Contains(t)) ? SuggestionFault.NotOffered : null;
    }

    /// <summary>B: none of the candidate's times works, so the team asks for more, with a short message.</summary>
    public void AskForMoreTimes(string? note, DateTimeOffset now)
    {
        if (Option != InterviewOption.TheirTimes || !IsPicking(now) || CandidateTimes.Count == 0)
        {
            throw new InvalidOperationException("Only an interview with the candidate's times can ask for more.");
        }

        TeamNote = CleanNote(note);
        CandidateTimes = [];
        CandidateTimesAt = null;
        OptionChangedAt = now;
    }

    /// <summary>C: the candidate cannot make the team's times and asks for another, with an optional note.</summary>
    public void AskForAnotherTime(string? note, DateTimeOffset now)
    {
        if (Option != InterviewOption.OurSuggestion || !IsPicking(now) || AnotherTimeAskedAt is not null)
        {
            throw new InvalidOperationException("Only an interview with the team's times can be asked for another.");
        }

        AnotherTimeAskedAt = now;
        CandidateNote = CleanNote(note);
    }

    /// <summary>
    /// Whether the next step is the team's at <paramref name="now"/>: the candidate sent their times (B) or asked for another
    /// time (C), and nothing is booked from it yet.
    /// </summary>
    public bool WaitsForTeam(DateTimeOffset now) =>
        IsPicking(now) && ((Option == InterviewOption.TheirTimes && CandidateTimes.Count > 0)
            || (Option == InterviewOption.OurSuggestion && AnotherTimeAskedAt is not null));

    /// <summary>
    /// The times the option itself names: the candidate's for B (the team books one), the team's for C (the candidate accepts
    /// one, until they ask for another), and the one-off times for A, whose other times come from calendars and hours.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> NamedTimes() => Option switch
    {
        InterviewOption.TheirTimes => CandidateTimes,
        InterviewOption.OurSuggestion => AnotherTimeAskedAt is null ? ProposedTimes : [],
        _ => ExtraTimes,
    };

    private void Apply(InterviewPick pick, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        Option = pick.Option;
        Hours = pick.Hours;
        UseBlocks = pick.UseBlocks;
        ProposedTimes = Instants(pick.ProposedTimes);
        ExtraTimes = Instants(pick.ExtraTimes);
        CandidateTimes = [];
        CandidateTimesAt = null;
        AnotherTimeAskedAt = null;
        CandidateNote = null;
        TeamNote = null;
        WindowStart = windowStart.ToUniversalTime();
        WindowEnd = windowEnd.ToUniversalTime();
        LookupStart = DateTimeOffset.Compare(LookupStart, WindowStart) <= 0 ? LookupStart : WindowStart;
        LookupEnd = DateTimeOffset.Compare(LookupEnd, WindowEnd) >= 0 ? LookupEnd : WindowEnd;
    }

    private static List<DateTimeOffset> Instants(IReadOnlyList<DateTimeOffset>? times) => [.. (times ?? []).Select(t => t.ToUniversalTime()).Order()];

    // A note from a person: trimmed, blank is none, and never longer than the column.
    private static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNoteLength ? throw new ArgumentException($"Keep the message to {MaxNoteLength} characters.") : trimmed;
    }

    /// <summary>Cancels an Open or Booked interview. A booked one keeps its time as history.</summary>
    public void Cancel(DateTimeOffset now, bool byCandidate)
    {
        if (EffectiveStatus(now) is not (InterviewStatus.Open or InterviewStatus.Booked))
        {
            throw new InvalidOperationException("Only an open or booked interview can be cancelled.");
        }

        Status = InterviewStatus.Cancelled;
        CancelledAt = now;
        CancelledByCandidate = byCandidate;
        NewTimesOfferedAt = null;
        Revision++;
    }

    /// <summary>
    /// Keeps the meeting link the organizer's event got. It comes from the provider and ends up as a link in the dashboard and
    /// the candidate's email, so anything but an absolute https address is dropped, as is an overlong one.
    /// </summary>
    public void SetMeetingUrl(string? url)
    {
        var trimmed = url?.Trim();
        MeetingUrl = trimmed is { Length: <= MaxMeetingUrlLength }
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
                ? trimmed
                : null;
    }

    /// <summary>
    /// Whether the team's offer of new times still waits for the candidate at <paramref name="now"/>. The offered days end it.
    /// After them there is nothing left to pick, and the booked time stands.
    /// </summary>
    public bool HasPendingNewTimes(DateTimeOffset now) =>
        Status == InterviewStatus.Booked && NewTimesOfferedAt is not null && now < WindowEnd;

    /// <summary>
    /// Whether the candidate may reschedule or cancel at <paramref name="now"/>: booked, and at least
    /// <paramref name="minNotice"/> before the start. While new times are pending (<see cref="HasPendingNewTimes"/>) the
    /// candidate may whatever the notice, because the team asked for the move, even after the old time has passed.
    /// </summary>
    public bool CanBeChangedByCandidate(DateTimeOffset now, TimeSpan minNotice) =>
        Status == InterviewStatus.Booked && (HasPendingNewTimes(now) || (StartsAt is { } starts && now <= starts - minNotice));

    /// <summary>Whether an interview starting at <paramref name="start"/> fits inside the window.</summary>
    public bool FitsWindow(DateTimeOffset start) => start >= WindowStart && start + Duration <= WindowEnd;

    private void Place(DateTimeOffset start)
    {
        if (!FitsWindow(start))
        {
            throw new ArgumentOutOfRangeException(nameof(start), "The interview must fit inside the window.");
        }

        StartsAt = start.ToUniversalTime();
        EndsAt = StartsAt + Duration;
        Revision++;
    }
}

/// <summary>Why a candidate's suggested times were refused.</summary>
public enum SuggestionFault
{
    TooFew,
    TooMany,

    /// <summary>A time is repeated, or not within the limits (taken out since the page was drawn, or never in them).</summary>
    NotOffered,
}

/// <summary>Where one interviewer's calendar event stands.</summary>
public enum InterviewSyncStatus
{
    /// <summary>The event is not on the calendar yet, or must be changed to match the booking.</summary>
    Pending,

    Synced,

    /// <summary>The calendar refused or could not be reached for good; <see cref="InterviewAttendee.SyncProblem"/> says so in plain words.</summary>
    Failed,
}

/// <summary>
/// One interviewer on an interview: their own event on their own calendar. The first one is the organizer, and the
/// candidate is invited on that event only. Relational, not JSON, so the double-booking check can query by interviewer.
/// </summary>
public sealed class InterviewAttendee : IApplicationScoped
{
    public const int MaxEventIdLength = 200;

    public const int MaxProblemLength = 200;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid InterviewId { get; init; }

    /// <summary>The interviewer's user id (the calendar connection's owner).</summary>
    public Guid UserId { get; init; }

    /// <summary>The one whose event invites the candidate and makes the meeting link.</summary>
    public bool IsOrganizer { get; private set; }

    /// <summary>The provider of the interviewer's calendar when the event was last written.</summary>
    public CalendarProvider Provider { get; private set; }

    public string? ProviderEventId { get; private set; }

    public InterviewSyncStatus SyncStatus { get; private set; }

    public string? SyncProblem { get; private set; }

    /// <summary>
    /// The interview start this interviewer was last reminded about (the day-before email). The reminder is for one booked time, so
    /// a move starts again, and a redelivered reminder for the same time finds this and sends nothing.
    /// </summary>
    public DateTimeOffset? RemindedForStart { get; private set; }

    /// <summary>
    /// The revision this interviewer was last emailed the calendar invitation (.ics) for, when their own calendar did not get
    /// the event. A redelivered message finds it and sends nothing; a move or a cancellation is a new revision.
    /// </summary>
    public int? InvitedRevision { get; private set; }

    public static InterviewAttendee Create(InterviewRequest interview, Guid userId, bool isOrganizer, CalendarProvider provider)
    {
        ArgumentNullException.ThrowIfNull(interview);
        return new InterviewAttendee
        {
            OrganizationId = interview.OrganizationId,
            ApplicationId = interview.ApplicationId,
            InterviewId = interview.Id,
            UserId = userId,
            IsOrganizer = isOrganizer,
            Provider = provider,
        };
    }

    /// <summary>The organizer left before the interview was booked, so this interviewer's event will invite the candidate instead.</summary>
    public void MakeOrganizer() => IsOrganizer = true;

    /// <summary>The booking changed: the event must be written again.</summary>
    public void MarkPending()
    {
        SyncStatus = InterviewSyncStatus.Pending;
        SyncProblem = null;
    }

    public void MarkSynced(CalendarProvider provider, string eventId)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventId);
        Provider = provider;
        ProviderEventId = eventId.Length > MaxEventIdLength ? eventId[..MaxEventIdLength] : eventId;
        SyncStatus = InterviewSyncStatus.Synced;
        SyncProblem = null;
    }

    /// <summary>Records that the reminder for the interview starting at <paramref name="start"/> was sent.</summary>
    public void MarkReminded(DateTimeOffset start) => RemindedForStart = start;

    /// <summary>Records that the invitation for <paramref name="revision"/> was emailed.</summary>
    public void MarkInvited(int revision) => InvitedRevision = revision;

    /// <summary>
    /// The interviewer has no calendar to write to (ScreenHire's calendar, <see cref="CalendarProvider.ScreenHire"/>): the
    /// invitation emailed to them is their event, so there is nothing to sync.
    /// </summary>
    public void MarkOnScreenHireCalendar() => MarkSynced(CalendarProvider.ScreenHire, IcsUid(InterviewId));

    /// <summary>The UID every calendar invitation for an interview carries, and the event id of an interviewer on ScreenHire's calendar.</summary>
    public static string IcsUid(Guid interviewId) => $"interview-{interviewId:N}";

    public void MarkFailed(string problem)
    {
        ArgumentException.ThrowIfNullOrEmpty(problem);
        SyncStatus = InterviewSyncStatus.Failed;
        SyncProblem = problem.Length > MaxProblemLength ? problem[..MaxProblemLength] : problem;
    }
}
