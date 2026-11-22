using System.Globalization;

namespace ScreenHire.Domain.Scheduling;

/// <summary>
/// One stretch of hours on one day of the week, in local wall-clock time: <c>from</c> and <c>to</c> are "HH:mm" (24 hour),
/// and <c>to</c> may be "24:00" for the end of the day.
/// </summary>
public sealed record DayHours(DayOfWeek Day, string From, string To);

/// <summary>
/// Hours in a week, in some time zone's wall-clock time (docs/architecture/interview-scheduling.md, "Hours"): the hours an
/// interview may be offered in (a request's hours, or the interview blocks a business sets, "Tue and Thu, 2 to 5"), and a
/// member's own interview hours. A day may have more than one stretch; a day with none has no hours.
/// </summary>
/// <remarks>
/// The times are strings, as the API has always sent working hours, so the stored JSON, the API and the hiring definition's
/// JSON Schema all read "09:00". Methods, not properties, so nothing computed joins the serialized shape.
/// </remarks>
public sealed record WeeklyHours(IReadOnlyList<DayHours> Days)
{
    /// <summary>The most stretches a week may have: two a day.</summary>
    public const int MaxStretches = 14;

    /// <summary>Every weekday from <paramref name="from"/> to <paramref name="to"/>: the working hours every request had before hours could be set per day.</summary>
    public static WeeklyHours Weekdays(TimeOnly from, TimeOnly to) =>
        new([.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new DayHours(day, Write(from), Write(to)))]);

    /// <summary>
    /// The first thing wrong with these hours in plain words, or null. <paramref name="minutes"/> is the length that must fit
    /// in at least one stretch, when there is one.
    /// </summary>
    public string? Problem(int? minutes = null)
    {
        if (Days is null || Days.Count == 0)
        {
            return "Choose at least one day with hours.";
        }

        if (Days.Count > MaxStretches)
        {
            return $"A week can have at most {MaxStretches} stretches of hours.";
        }

        var parsed = new List<(DayOfWeek Day, int From, int To)>();
        foreach (var stretch in Days)
        {
            if (stretch is null || !Enum.IsDefined(stretch.Day))
            {
                return "Choose a day for each stretch of hours.";
            }

            if (ParseMinutes(stretch.From, allowEndOfDay: false) is not { } from || ParseMinutes(stretch.To, allowEndOfDay: true) is not { } to)
            {
                return "Write each time as hours and minutes, such as 09:00.";
            }

            if (to <= from)
            {
                return "Hours must end after they start.";
            }

            parsed.Add((stretch.Day, from, to));
        }

        foreach (var day in parsed.GroupBy(p => p.Day))
        {
            var ordered = day.OrderBy(p => p.From).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].From < ordered[i - 1].To)
                {
                    return $"The hours on {day.Key} overlap.";
                }
            }
        }

        return minutes is { } length && parsed.All(p => p.To - p.From < length)
            ? "The hours are shorter than the interview."
            : null;
    }

    /// <summary>Whether a day of the week has any hours.</summary>
    public bool Has(DayOfWeek day) => Days.Any(d => d.Day == day);

    /// <summary>The stretches on <paramref name="date"/>, as local wall-clock times in order. Hours that fail <see cref="Problem"/> give none.</summary>
    public IReadOnlyList<(DateTime From, DateTime To)> On(DateOnly date)
    {
        var stretches = new List<(DateTime, DateTime)>();
        foreach (var stretch in Days.Where(d => d.Day == date.DayOfWeek))
        {
            if (ParseMinutes(stretch.From, allowEndOfDay: false) is { } from && ParseMinutes(stretch.To, allowEndOfDay: true) is { } to && to > from)
            {
                var midnight = date.ToDateTime(TimeOnly.MinValue);
                stretches.Add((midnight.AddMinutes(from), midnight.AddMinutes(to)));
            }
        }

        return [.. stretches.OrderBy(s => s.Item1)];
    }

    /// <summary>
    /// The time from <paramref name="from"/> to <paramref name="to"/> that falls outside these hours in <paramref name="zone"/>,
    /// as busy intervals (UTC, sorted). Busy time is how hours reach <see cref="SlotFinder"/>: a member's own hours and an
    /// interview's blocks both become time nobody may be booked in.
    /// </summary>
    public IReadOnlyList<BusyInterval> Outside(TimeZoneInfo zone, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var busy = new List<BusyInterval>();
        if (to <= from)
        {
            return busy;
        }

        var cursor = from;
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime).AddDays(1);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var (localFrom, localTo) in On(day))
            {
                var open = SlotFinder.InstantOf(localFrom, zone);
                var close = SlotFinder.InstantOf(localTo, zone);
                if (close <= cursor || open >= to)
                {
                    continue;
                }

                if (open > cursor)
                {
                    busy.Add(new BusyInterval(cursor, open));
                }

                cursor = close > cursor ? close : cursor;
            }
        }

        if (cursor < to)
        {
            busy.Add(new BusyInterval(cursor, to));
        }

        return busy;
    }

    /// <summary>Whether an interview from <paramref name="start"/> to <paramref name="end"/> lies inside one stretch, in <paramref name="zone"/>.</summary>
    public bool Contains(TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end) =>
        end > start && SlotFinder.IsFree(Outside(zone, start, end), start, end);

    /// <summary>Minutes after midnight for "HH:mm", or null. "24:00" is the end of the day where <paramref name="allowEndOfDay"/>.</summary>
    public static int? ParseMinutes(string? text, bool allowEndOfDay)
    {
        if (text is null || text.Length != 5 || text[2] != ':'
            || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes > 59)
        {
            return null;
        }

        if (hours == 24 && minutes == 0 && allowEndOfDay)
        {
            return 24 * 60;
        }

        return hours > 23 ? null : (hours * 60) + minutes;
    }

    /// <summary>A time as "HH:mm".</summary>
    public static string Write(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
