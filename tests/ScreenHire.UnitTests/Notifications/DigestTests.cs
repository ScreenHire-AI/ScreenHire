using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;

namespace ScreenHire.UnitTests.Notifications;

/// <summary>
/// When the daily digest is due (docs/architecture/notifications.md, "The daily digest"): from 08:00 in the person's time zone,
/// once a local day, holding what arrived before that day's 08:00; and its lines, which never carry a note's or a reply's text,
/// nor a candidate's name for verification.
/// </summary>
public sealed class DigestTests
{
    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void It_is_due_at_eight_in_the_persons_zone_with_that_mornings_cutoff()
    {
        // 14:00 UTC is 08:00 in Chicago (UTC-6 in November).
        var due = DigestSchedule.Due(Utc(21, 14), "America/Chicago", lastDigestOn: null);

        due.Should().NotBeNull();
        due!.Value.Date.Should().Be(new DateOnly(2026, 11, 21));
        due.Value.Cutoff.Should().Be(Utc(21, 14));
    }

    [Fact]
    public void It_is_not_due_before_eight_there()
    {
        DigestSchedule.Due(Utc(21, 13, 30), "America/Chicago", lastDigestOn: null).Should().BeNull();
    }

    [Fact]
    public void A_day_that_had_its_digest_gets_no_second()
    {
        DigestSchedule.Due(Utc(21, 20), "America/Chicago", lastDigestOn: new DateOnly(2026, 11, 21)).Should().BeNull();
        DigestSchedule.Due(Utc(22, 14), "America/Chicago", lastDigestOn: new DateOnly(2026, 11, 21)).Should().NotBeNull("a new day");
    }

    [Fact]
    public void A_zone_a_quarter_hour_off_is_due_at_the_half_hour_tick_after_eight()
    {
        // Kathmandu is UTC+5:45: 02:30 UTC is 08:15 there.
        DigestSchedule.Due(Utc(21, 2, 30), "Asia/Kathmandu", null).Should().NotBeNull();
        DigestSchedule.Due(Utc(21, 2, 0), "Asia/Kathmandu", null).Should().BeNull();
    }

    [Fact]
    public void No_time_zone_or_an_unknown_one_reads_as_UTC()
    {
        DigestSchedule.Due(Utc(21, 8), null, null)!.Value.Cutoff.Should().Be(Utc(21, 8));
        DigestSchedule.Due(Utc(21, 8), "Not/AZone", null)!.Value.Cutoff.Should().Be(Utc(21, 8));
    }

    [Fact]
    public void The_lines_name_people_and_jobs_but_never_text_and_verification_never_names_the_candidate()
    {
        var application = Guid.CreateVersion7();
        var note = Guid.CreateVersion7();
        DigestItem Item(NoticeKind kind, string source, string? detail = null) =>
            DigestItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), kind, source, application, Guid.CreateVersion7(), null, detail, Utc(21, 9));

        DigestEmail.Line(Item(NoticeKind.Mentions, note.ToString("N")), "Taylor Brook", "Platform Engineer", "Jordan Ellis")
            .Should().Be("Jordan Ellis mentioned you in a note on Taylor Brook, Platform Engineer");
        DigestEmail.PathOf(Item(NoticeKind.Mentions, note.ToString("N"))).Should().Be($"/applications/{application}?tab=activity#note-{note}");
        DigestEmail.Line(Item(NoticeKind.Interviews, "x:1", "Moved"), "Taylor Brook", "Platform Engineer", null)
            .Should().Be("The interview with Taylor Brook, Platform Engineer, was moved");
        DigestEmail.Line(Item(NoticeKind.VerificationSuggested, "e"), "Taylor Brook", "Platform Engineer", null)
            .Should().NotContain("Taylor Brook").And.Contain("Platform Engineer");
    }

    [Fact]
    public void The_email_groups_lines_by_row_and_links_each()
    {
        var link = new Uri("https://app.example.com/applications/1");
        var email = DigestEmail.Compose(
            "avery@example.com", "Brightwater",
            [
                new DigestLine(NoticeKind.NewApplications, "Taylor Brook applied to Platform Engineer", link),
                new DigestLine(NoticeKind.Tasks, "A new task on Taylor Brook, Platform Engineer", link),
            ]);

        email.Subject.Should().Be("Your ScreenHire digest: 2 updates");
        email.TextBody.IndexOf("Tasks for you", StringComparison.Ordinal).Should().BeLessThan(email.TextBody.IndexOf("New applications", StringComparison.Ordinal));
        email.HtmlBody.Should().Contain("<a href=\"https://app.example.com/applications/1\">Taylor Brook applied to Platform Engineer</a>");
    }
}
