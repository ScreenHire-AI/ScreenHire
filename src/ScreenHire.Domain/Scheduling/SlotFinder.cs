namespace ScreenHire.Domain.Scheduling;

/// <summary>A block of time a member is busy, as UTC instants. Half-open: it covers <see cref="Start"/> up to, not including, <see cref="End"/>.</summary>
public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// Which start times an interview can be offered at, given when each interviewer is busy (docs/architecture/interview-scheduling.md).
/// Pure: no clock, no database.
/// </summary>
/// <remarks>
/// <para>
/// The walk is over local days in the interview's time zone, so daylight saving time is right: working hours are wall-clock
/// hours, a slot never starts at a local time that does not exist (the hour skipped in spring), and a local time that
/// happens twice (the hour repeated in autumn) means its first occurrence. Only weekdays are offered.
/// </para>
/// <para>
/// Start times are <c>dayStart + k * step</c> in local wall-clock time, so with a 30 minute step and 09:00 working hours
/// the slots are on the hour and the half hour. A slot fits when its end, one duration of real time later, is not past
/// the day's end, the window's end or into any interviewer's busy time. Busy time is half-open: a meeting that ends at 10:00
/// leaves a 10:00 slot free.
/// </para>
/// </remarks>
public static class SlotFinder
{
    /// <summary>
    /// The start instants (UTC, ascending) at which every interviewer in <paramref name="busyPerInterviewer"/> is free for
    /// <paramref name="duration"/>, at most <paramref name="maxSlots"/> of them.
    /// </summary>
    /// <param name="duration">How long the interview lasts, in real time.</param>
    /// <param name="zone">The zone the working hours are in; days are walked in it.</param>
    /// <param name="dayStart">When working hours start, local time.</param>
    /// <param name="dayEnd">When working hours end, local time. A slot ends at or before it.</param>
    /// <param name="maxSlots">Stops after this many start times.</param>
    /// <param name="windowStart">No slot starts before this instant.</param>
    /// <param name="windowEnd">No slot ends after this instant.</param>
    /// <param name="earliestStart">No slot starts before this instant either: now plus the minimum notice.</param>
    /// <param name="step">The distance between start times, in minutes.</param>
    /// <param name="busyPerInterviewer">One list of busy intervals per interviewer, in any order, overlapping or not.</param>
    public static IReadOnlyList<DateTimeOffset> Find(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan duration,
        int step,
        TimeZoneInfo zone,
        TimeOnly dayStart,
        TimeOnly dayEnd,
        DateTimeOffset earliestStart,
        IReadOnlyList<IReadOnlyList<BusyInterval>> busyPerInterviewer,
        int maxSlots)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(busyPerInterviewer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, 0);
        var slots = new List<DateTimeOffset>();
        if (maxSlots <= 0 || windowEnd <= windowStart || dayEnd <= dayStart)
        {
            return slots;
        }

        var busy = busyPerInterviewer.Select(Merge).ToList();
        var notBefore = windowStart > earliestStart ? windowStart : earliestStart;
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(windowStart, zone).DateTime);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(windowEnd, zone).DateTime);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var dayEndsAt = InstantOf(day.ToDateTime(dayEnd), zone);
            for (var local = day.ToDateTime(dayStart); local < day.ToDateTime(dayEnd); local = local.AddMinutes(step))
            {
                if (zone.IsInvalidTime(local))
                {
                    continue;
                }

                var start = InstantOf(local, zone);
                var end = start + duration;
                if (start < notBefore || end > windowEnd || end > dayEndsAt)
                {
                    continue;
                }

                if (busy.All(intervals => IsFree(intervals, start, end)))
                {
                    slots.Add(start);
                    if (slots.Count >= maxSlots)
                    {
                        return slots;
                    }
                }
            }
        }

        return slots;
    }

    /// <summary>Whether nothing in <paramref name="sortedMerged"/> (from <see cref="Merge"/>) overlaps the time from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static bool IsFree(IReadOnlyList<BusyInterval> sortedMerged, DateTimeOffset start, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(sortedMerged);
        foreach (var interval in sortedMerged)
        {
            if (interval.Start >= end)
            {
                return true; // sorted: nothing later can overlap
            }

            if (interval.End > start)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The intervals sorted by start with overlapping and touching ones joined, and empty or reversed ones dropped.</summary>
    public static IReadOnlyList<BusyInterval> Merge(IEnumerable<BusyInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var merged = new List<BusyInterval>();
        foreach (var interval in intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start).ThenBy(i => i.End))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                if (interval.End > merged[^1].End)
                {
                    merged[^1] = merged[^1] with { End = interval.End };
                }
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    // The instant a local wall-clock time is. A time that happens twice means its first occurrence (daylight saving time,
    // the larger offset). A time that does not exist is read with the offset before the change, which puts it just after the gap.
    public static DateTimeOffset InstantOf(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
