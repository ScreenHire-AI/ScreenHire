using System.Text;
using ScreenHire.Application.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// The calendar invitations a booking emails and the subscribe feed (docs/architecture/interview-scheduling.md, "Calendar
/// invitations"): RFC 5545's escaping and 75-octet lines, a request or a cancellation with the same UID and a rising
/// sequence so a calendar app updates the event it has, and a feed with no people in it.
/// </summary>
public sealed class IcsWriterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 14, 0, 0, TimeSpan.Zero);

    private static IcsEvent Event(bool cancelled = false, int sequence = 1, string summary = "Interview with Brightwater") => new(
        "interview-0123456789abcdef0123456789abcdef", sequence, Start, Start.AddMinutes(30), summary, "Line one\nLine two; with, commas",
        "https://meet.example.org/abc", Start.AddDays(-1), cancelled, new IcsPerson("team@reply.example.com"), [new IcsPerson("grace@example.com", "Grace \"G\" Adeyemi")]);

    // The logical lines: a folded line continues after a line break and a space.
    private static string[] Lines(string ics) => ics.Replace("\r\n ", "", StringComparison.Ordinal).Split("\r\n");

    [Fact]
    public void A_request_names_the_event_its_organizer_and_its_attendee_in_utc()
    {
        var lines = Lines(IcsWriter.Invitation(Event()));

        lines.Should().Contain(["BEGIN:VCALENDAR", "VERSION:2.0", "METHOD:REQUEST", "BEGIN:VEVENT", "END:VEVENT", "END:VCALENDAR"]);
        lines.Should().Contain("UID:interview-0123456789abcdef0123456789abcdef");
        lines.Should().Contain("SEQUENCE:1");
        lines.Should().Contain("DTSTART:20261103T140000Z");
        lines.Should().Contain("DTEND:20261103T143000Z");
        lines.Should().Contain("STATUS:CONFIRMED");
        lines.Should().Contain("ORGANIZER:mailto:team@reply.example.com");
        lines.Should().Contain(l => l.StartsWith("ATTENDEE;", StringComparison.Ordinal) && l.Contains("CN=\"Grace G Adeyemi\"", StringComparison.Ordinal)
            && l.EndsWith(":mailto:grace@example.com", StringComparison.Ordinal), "a quote cannot sit inside a quoted parameter");
        IcsWriter.InvitationContentType(Event()).Should().Be("text/calendar; charset=utf-8; method=REQUEST");
    }

    [Fact]
    public void A_cancellation_keeps_the_uid_and_says_cancel_with_a_higher_sequence()
    {
        var lines = Lines(IcsWriter.Invitation(Event(cancelled: true, sequence: 3)));

        lines.Should().Contain(["METHOD:CANCEL", "STATUS:CANCELLED", "SEQUENCE:3", "UID:interview-0123456789abcdef0123456789abcdef"]);
        IcsWriter.InvitationContentType(Event(cancelled: true)).Should().EndWith("method=CANCEL");
    }

    [Fact]
    public void Text_escapes_backslashes_semicolons_commas_and_line_breaks()
    {
        IcsWriter.Text("a\\b;c,d\ne\r\nf").Should().Be(@"a\\b\;c\,d\ne\nf");
        Lines(IcsWriter.Invitation(Event())).Should().Contain(@"DESCRIPTION:Line one\nLine two\; with\, commas");
    }

    [Fact]
    public void A_line_break_in_a_link_or_an_address_cannot_start_a_property_of_its_own()
    {
        var ev = Event() with
        {
            Url = "https://meet.example.org/abc\r\nATTENDEE:mailto:x@example.com",
            Organizer = new IcsPerson("team@reply.example.com\nMETHOD:CANCEL"),
            Attendees = [new IcsPerson("grace@example.com\r\nSTATUS:CANCELLED")],
        };

        var lines = Lines(IcsWriter.Invitation(ev));

        lines.Should().ContainSingle(l => l.StartsWith("METHOD:", StringComparison.Ordinal)).Which.Should().Be("METHOD:REQUEST");
        lines.Should().ContainSingle(l => l.StartsWith("STATUS:", StringComparison.Ordinal)).Which.Should().Be("STATUS:CONFIRMED");
        lines.Should().ContainSingle(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
    }

    [Fact]
    public void Long_lines_fold_at_75_octets_without_splitting_a_character()
    {
        var summary = string.Concat(Enumerable.Repeat("Entretien café ☕ ", 12));
        var ics = IcsWriter.Invitation(Event(summary: summary));

        ics.Split("\r\n").Should().OnlyContain(line => Encoding.UTF8.GetByteCount(line) <= 75);
        Lines(ics).Should().Contain($"SUMMARY:{IcsWriter.Text(summary)}", "folding is undone by removing each line break and the space after it");
    }

    [Fact]
    public void A_feed_publishes_every_event_and_names_nobody()
    {
        var ics = IcsWriter.Feed("Interviews", [Event(), Event(sequence: 2) with { Uid = "interview-other" }]);
        var lines = Lines(ics);

        lines.Should().Contain(["METHOD:PUBLISH", "X-WR-CALNAME:Interviews"]);
        lines.Count(l => l == "BEGIN:VEVENT").Should().Be(2);
        lines.Should().NotContain(l => l.StartsWith("ORGANIZER", StringComparison.Ordinal) || l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        ics.Should().NotContain("grace@example.com");
    }
}
