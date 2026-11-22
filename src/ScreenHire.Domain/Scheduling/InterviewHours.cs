using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Scheduling;

/// <summary>
/// A member's own interview hours in one organization, from Your account's Calendar page
/// (docs/architecture/interview-scheduling.md, "Personal hours and ScreenHire's calendar"): the days and hours they
/// interview, a buffer between interviews, the most interviews in a day, and the time zone the hours are in. Every way of
/// scheduling respects them. With <see cref="Weekly"/> set they are also ScreenHire's own calendar for someone with no
/// Google or Outlook calendar: their interviews booked through ScreenHire are their busy time, and
/// <see cref="FeedSecret"/> is the secret behind their private subscribe link. One row per membership, made on the first save.
/// </summary>
public sealed class InterviewHours : ITenantOwned
{
    public const int MaxBufferMinutes = 120;

    public const int MaxPerDayLimit = 12;

    public const int FeedSecretLength = 32;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid MemberId { get; init; }

    public Guid UserId { get; init; }

    /// <summary>The IANA zone the hours are in.</summary>
    public string TimeZone { get; private set; } = "UTC";

    /// <summary>The hours the member interviews in; null when they have set none (a buffer or a daily limit may still apply).</summary>
    public WeeklyHours? Weekly { get; private set; }

    /// <summary>Minutes kept free before and after each interview booked through ScreenHire.</summary>
    public int BufferMinutes { get; private set; }

    /// <summary>The most interviews booked through ScreenHire in one local day; null for no limit.</summary>
    public int? MaxPerDay { get; private set; }

    /// <summary>The secret the subscribe link's token carries (hex); null while the link is off. A new one ends the old link.</summary>
    public string? FeedSecret { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether the member has hours, which makes them schedulable without a connected calendar.</summary>
    public bool HasHours => Weekly is not null;

    public TimeZoneInfo Zone => InterviewRequest.TryFindZone(TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    public static InterviewHours Create(Guid organizationId, Guid memberId, Guid userId, DateTimeOffset now) =>
        new() { OrganizationId = organizationId, MemberId = memberId, UserId = userId, UpdatedAt = now };

    /// <summary>The first thing wrong with these settings in plain words, or null.</summary>
    public static string? Problem(string? timeZone, WeeklyHours? weekly, int bufferMinutes, int? maxPerDay)
    {
        if (!InterviewRequest.TryFindZone(timeZone, out _))
        {
            return "Choose a time zone.";
        }

        if (weekly?.Problem(InterviewRequest.AllowedDurations[0]) is { } problem)
        {
            return problem;
        }

        if (bufferMinutes is < 0 or > MaxBufferMinutes)
        {
            return $"The time between interviews can be at most {MaxBufferMinutes} minutes.";
        }

        return maxPerDay is < 1 or > MaxPerDayLimit ? $"The most interviews in a day must be between 1 and {MaxPerDayLimit}." : null;
    }

    public void Set(string timeZone, WeeklyHours? weekly, int bufferMinutes, int? maxPerDay, DateTimeOffset now)
    {
        if (Problem(timeZone, weekly, bufferMinutes, maxPerDay) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        TimeZone = timeZone;
        Weekly = weekly;
        BufferMinutes = bufferMinutes;
        MaxPerDay = maxPerDay;
        UpdatedAt = now;
    }

    /// <summary>Turns the subscribe link on with <paramref name="secret"/>, or gives it a new one, which ends the old link.</summary>
    public void SetFeedSecret(string secret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        FeedSecret = secret;
        UpdatedAt = now;
    }

    /// <summary>Turns the subscribe link off.</summary>
    public void ClearFeed(DateTimeOffset now)
    {
        FeedSecret = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// The member's busy time from <paramref name="from"/> to <paramref name="to"/> that these settings make: everything
    /// outside their hours, <see cref="BufferMinutes"/> either side of each of <paramref name="bookings"/> (their interviews
    /// booked through ScreenHire), and the whole local day once a day holds <see cref="MaxPerDay"/> of them. The bookings
    /// themselves are counted by the caller.
    /// </summary>
    public IReadOnlyList<BusyInterval> BusyTime(IReadOnlyList<BusyInterval> bookings, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        var busy = new List<BusyInterval>();
        if (Weekly is not null)
        {
            busy.AddRange(Weekly.Outside(Zone, from, to));
        }

        if (BufferMinutes > 0)
        {
            var buffer = TimeSpan.FromMinutes(BufferMinutes);
            busy.AddRange(bookings.Select(b => new BusyInterval(b.Start - buffer, b.End + buffer)));
        }

        if (MaxPerDay is { } max)
        {
            var zone = Zone;
            foreach (var day in bookings.GroupBy(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.Start, zone).DateTime)).Where(d => d.Count() >= max))
            {
                busy.Add(new BusyInterval(
                    SlotFinder.InstantOf(day.Key.ToDateTime(TimeOnly.MinValue), zone),
                    SlotFinder.InstantOf(day.Key.AddDays(1).ToDateTime(TimeOnly.MinValue), zone)));
            }
        }

        return busy;
    }
}
