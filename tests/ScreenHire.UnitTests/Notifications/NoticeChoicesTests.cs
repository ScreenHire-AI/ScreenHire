using ScreenHire.Api.Endpoints;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Notifications;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Notifications;

/// <summary>
/// Each person's notification choices (docs/architecture/notifications.md, "Each person's choices"): where each row starts,
/// with the organization's alert settings as the starting value for verification and AI use, and a person's own choice
/// winning; what is kept when they save; and the footer every notification email ends with.
/// </summary>
public sealed class NoticeChoicesTests
{
    private static readonly OrganizationAlertDefaults AllOn = new(true, true, true, true);

    [Theory]
    [InlineData(NoticeKind.Mentions, EmailChoice.Instantly)]
    [InlineData(NoticeKind.Replies, EmailChoice.Instantly)]
    [InlineData(NoticeKind.Interviews, EmailChoice.Instantly)]
    [InlineData(NoticeKind.Tasks, EmailChoice.Digest)]
    [InlineData(NoticeKind.NewApplications, EmailChoice.Digest)]
    [InlineData(NoticeKind.TeammateChanges, EmailChoice.Off)]
    [InlineData(NoticeKind.VerificationSuggested, EmailChoice.Off)]
    [InlineData(NoticeKind.AiUseAndBilling, EmailChoice.Off)]
    public void Each_row_starts_where_the_brief_put_it_while_the_organization_sends_no_alerts(NoticeKind kind, EmailChoice expected) =>
        NoticeDefaults.Email(kind, OrganizationAlertDefaults.None).Should().Be(expected);

    [Fact]
    public void The_organization_alert_settings_are_the_starting_value_for_verification_and_AI_use()
    {
        NoticeDefaults.Email(NoticeKind.VerificationSuggested, AllOn).Should().Be(EmailChoice.Instantly);
        NoticeDefaults.Email(NoticeKind.AiUseAndBilling, AllOn).Should().Be(EmailChoice.Instantly);
        NoticeDefaults.Slack(NoticeKind.VerificationSuggested, AllOn).Should().BeTrue();
        NoticeDefaults.Slack(NoticeKind.VerificationSuggested, OrganizationAlertDefaults.None).Should().BeFalse();
    }

    [Fact]
    public void The_trial_reminders_start_at_instantly_as_they_always_went_while_the_AI_alerts_follow_the_organization()
    {
        NoticeDefaults.Email(NoticeKind.AiUseAndBilling, OrganizationAlertDefaults.None, handlesBilling: true).Should().Be(EmailChoice.Instantly);
        NoticeDefaults.Email(NoticeKind.AiUseAndBilling, OrganizationAlertDefaults.None).Should().Be(EmailChoice.Off);
    }

    [Fact]
    public void A_persons_own_choice_wins_over_the_organization()
    {
        var mine = Preferences();
        mine.SetNotices(NoticeScope.MyJobs, new Domain.Notifications.NoticeChoices
        {
            Choices = [new NoticeChoice(NoticeKind.VerificationSuggested, EmailChoice.Off, Slack: false)],
        }, At);

        PersonNotices.Email(mine, NoticeKind.VerificationSuggested, AllOn).Should().Be(EmailChoice.Off);
        PersonNotices.Slack(mine, NoticeKind.VerificationSuggested, AllOn).Should().BeFalse();
        PersonNotices.Email(null, NoticeKind.VerificationSuggested, AllOn).Should().Be(EmailChoice.Instantly, "no row is all defaults");
    }

    [Fact]
    public void A_choice_equal_to_its_default_is_not_kept_so_a_later_organization_change_reaches_the_person()
    {
        var kept = PersonNotices.Normalize(
            [
                new NoticeChoice(NoticeKind.Mentions, EmailChoice.Instantly),
                new NoticeChoice(NoticeKind.Tasks, EmailChoice.Instantly),
                new NoticeChoice(NoticeKind.Replies, EmailChoice.Instantly, Slack: true),
            ],
            kind => NoticeDefaults.Email(kind, OrganizationAlertDefaults.None),
            kind => NoticeDefaults.Slack(kind, OrganizationAlertDefaults.None));

        kept.Choices.Should().Equal(new NoticeChoice(NoticeKind.Tasks, EmailChoice.Instantly));
    }

    [Theory]
    [InlineData(EmailChoice.Instantly)]
    [InlineData(EmailChoice.Off)]
    public void Someone_who_handles_billing_keeps_any_AI_use_choice_while_the_trial_and_the_AI_alerts_start_apart(EmailChoice choice)
    {
        // The organization's AI usage email is off: the trial's reminders start at Instantly and the AI alerts at Off, so a
        // choice equal to either default would leave the other reader on its own default.
        var kept = PersonNotices.Normalize(
            [new NoticeChoice(NoticeKind.AiUseAndBilling, choice)],
            kind => NoticeDefaults.SharedEmail(kind, OrganizationAlertDefaults.None, handlesBilling: true),
            kind => NoticeDefaults.Slack(kind, OrganizationAlertDefaults.None));

        var mine = Preferences();
        mine.SetNotices(NoticeScope.MyJobs, kept, DateTimeOffset.UnixEpoch);
        PersonNotices.Email(mine, NoticeKind.AiUseAndBilling, OrganizationAlertDefaults.None).Should().Be(choice, "the AI alerts read it");
        PersonNotices.Email(mine, NoticeKind.AiUseAndBilling, OrganizationAlertDefaults.None, handlesBilling: true).Should().Be(choice, "the trial's reminders read it");
    }

    [Fact]
    public void Every_notification_email_ends_with_a_link_to_change_what_we_email()
    {
        var links = new DashboardLinkOptions { Scheme = "https", AppHost = "app.example.com" };
        var message = new EmailMessage("avery@example.com", "Subject", "Line.", "<p>Line.</p>");

        var footed = TeamEmail.WithFooter(message, links);

        footed.TextBody.Should().EndWith("Change what we email you: https://app.example.com/account/notifications");
        footed.HtmlBody.Should().Contain("<a href=\"https://app.example.com/account/notifications\">Change what we email you</a>");
        TeamEmail.WithFooter(message, new DashboardLinkOptions()).Should().Be(message, "with no dashboard address there is nothing to link");
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 0 }, "image/webp")]
    [InlineData(new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' }, null)]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, null)]
    public void A_photo_is_PNG_JPEG_or_WebP_by_its_bytes(byte[] content, string? expected) =>
        AccountEndpoints.PhotoType(content).Should().Be(expected);

    private static readonly DateTimeOffset At = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static MemberPreferences Preferences() => MemberPreferences.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), At);
}
