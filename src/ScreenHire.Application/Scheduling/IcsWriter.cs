using System.Globalization;
using System.Text;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// One event in an iCalendar file. <see cref="Uid"/> is the same for every copy of one interview, and <see cref="Sequence"/>
/// rises with each change, so a calendar app updates or removes the event it already has rather than adding another.
/// </summary>
public sealed record IcsEvent(
    string Uid,
    int Sequence,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Summary,
    string? Description,
    string? Url,
    DateTimeOffset Stamp,
    bool Cancelled = false,
    IcsPerson? Organizer = null,
    IReadOnlyList<IcsPerson>? Attendees = null);

/// <summary>A person in an event: an address and an optional name.</summary>
public sealed record IcsPerson(string Email, string? Name = null);

/// <summary>
/// Writes the small part of iCalendar (RFC 5545) that interview invitations and the subscribe link need: one calendar of
/// events in UTC, with text escaped and long lines folded at 75 octets.
/// </summary>
/// <remarks>
/// Written by hand rather than with a library: .NET has no iCalendar type, and these few properties do not justify a
/// dependency. An invitation is <c>METHOD:REQUEST</c> (or <c>CANCEL</c>) with an organizer and attendees, so mail apps
/// show it as a meeting to accept and later updates match it; the subscribe feed is <c>METHOD:PUBLISH</c> with neither.
/// </remarks>
public static class IcsWriter
{
    /// <summary>The media type of an invitation, as an email attachment carries it.</summary>
    public static string InvitationContentType(IcsEvent ev) =>
        $"text/calendar; charset=utf-8; method={(ev?.Cancelled == true ? "CANCEL" : "REQUEST")}";

    /// <summary>An invitation to one event: a request, or a cancellation when the event is cancelled.</summary>
    public static string Invitation(IcsEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return Calendar(ev.Cancelled ? "CANCEL" : "REQUEST", name: null, [ev], withPeople: true);
    }

    /// <summary>A calendar to subscribe to: every event as it stands, with no people in it.</summary>
    public static string Feed(string name, IReadOnlyList<IcsEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return Calendar("PUBLISH", name, events, withPeople: false);
    }

    private static string Calendar(string method, string? name, IReadOnlyList<IcsEvent> events, bool withPeople)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//ScreenHire//Interviews//EN",
            "CALSCALE:GREGORIAN",
            $"METHOD:{method}",
        };
        if (name is not null)
        {
            lines.Add($"X-WR-CALNAME:{Text(name)}");
        }

        foreach (var ev in events)
        {
            lines.Add("BEGIN:VEVENT");
            lines.Add($"UID:{Text(ev.Uid)}");
            lines.Add($"SEQUENCE:{ev.Sequence.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"DTSTAMP:{Utc(ev.Stamp)}");
            lines.Add($"DTSTART:{Utc(ev.Start)}");
            lines.Add($"DTEND:{Utc(ev.End)}");
            lines.Add($"SUMMARY:{Text(ev.Summary)}");
            if (!string.IsNullOrEmpty(ev.Description))
            {
                lines.Add($"DESCRIPTION:{Text(ev.Description)}");
            }

            if (!string.IsNullOrEmpty(ev.Url))
            {
                lines.Add($"URL:{Raw(ev.Url)}");
                lines.Add($"LOCATION:{Text(ev.Url)}");
            }

            if (withPeople && ev.Organizer is { } organizer)
            {
                lines.Add($"ORGANIZER{Name(organizer)}:mailto:{Raw(organizer.Email)}");
            }

            foreach (var attendee in withPeople ? ev.Attendees ?? [] : [])
            {
                lines.Add($"ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE{Name(attendee)}:mailto:{Raw(attendee.Email)}");
            }

            lines.Add(ev.Cancelled ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");
            lines.Add("TRANSP:OPAQUE");
            lines.Add("END:VEVENT");
        }

        lines.Add("END:VCALENDAR");
        var output = new StringBuilder();
        foreach (var line in lines)
        {
            Fold(line, output);
        }

        return output.ToString();
    }

    /// <summary>A UTC instant in the iCalendar form, <c>20261103T140000Z</c>.</summary>
    public static string Utc(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A text value with backslashes, semicolons, commas and line breaks escaped.</summary>
    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            escaped.Append(c switch
            {
                '\\' => @"\\",
                ';' => @"\;",
                ',' => @"\,",
                '\n' or '\r' => @"\n",
                _ when char.IsControl(c) => "",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    // A value written without text escaping (an address, a link), with the control characters that would end its line removed.
    private static string Raw(string value) => new([.. value.Where(c => !char.IsControl(c))]);

    // A person's name as a CN parameter: quoted, with the quotes and control characters a parameter cannot hold removed.
    private static string Name(IcsPerson person) =>
        string.IsNullOrWhiteSpace(person.Name)
            ? ""
            : $";CN=\"{new string([.. person.Name.Where(c => c != '"' && !char.IsControl(c))])}\"";

    // Lines are at most 75 octets: a longer one continues on the next line after a space, never inside a UTF-8 character.
    private static void Fold(string line, StringBuilder output)
    {
        var octets = 0;
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                output.Append("\r\n ");
                octets = 0;
                limit = 74; // the leading space counts
            }

            output.Append(rune.ToString());
            octets += size;
        }

        output.Append("\r\n");
    }
}
