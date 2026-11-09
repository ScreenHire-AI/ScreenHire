using ScreenHire.Domain.Notifications;

namespace ScreenHire.UnitTests.Notifications;

/// <summary>
/// One person's notification in the bell (docs/architecture/notifications.md): a link can only point inside the dashboard, and
/// reading or dismissing it twice keeps the first time.
/// </summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Notification New(string link = "/settings/sending-domain") =>
        Notification.Create(
            OrganizationId, UserId, NotificationKinds.SendingDomainFailing, "abc",
            new Dictionary<string, string> { ["domain"] = "acme.example.org" }, link, First);

    [Theory]
    [InlineData("/")]
    [InlineData("/settings/team")]
    [InlineData("/settings/sending-domain")]
    [InlineData("/jobs/123?tab=activity")]
    public void A_path_inside_the_dashboard_is_accepted(string link)
    {
        Notification.IsDashboardPath(link).Should().BeTrue();

        New(link).Link.Should().Be(link);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/settings")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\host")]
    [InlineData("\\host")]
    [InlineData("settings/team")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/settings\nteam")]
    [InlineData("")]
    [InlineData(null)]
    public void A_link_that_could_leave_the_dashboard_is_refused(string? link)
    {
        Notification.IsDashboardPath(link).Should().BeFalse();

        var create = () => Notification.Create(OrganizationId, UserId, NotificationKinds.SendingDomainFailing, "abc", null, link!, First);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_link_over_the_longest_allowed_is_refused()
    {
        Notification.IsDashboardPath("/" + new string('a', Notification.MaxLinkLength - 1)).Should().BeTrue();
        Notification.IsDashboardPath("/" + new string('a', Notification.MaxLinkLength)).Should().BeFalse();
    }

    [Fact]
    public void A_notification_keeps_what_it_was_made_with_and_starts_unread()
    {
        var notification = New();

        notification.OrganizationId.Should().Be(OrganizationId);
        notification.UserId.Should().Be(UserId);
        notification.Kind.Should().Be("sending-domain-failing");
        notification.Key.Should().Be("abc");
        notification.Payload.Should().Equal(new Dictionary<string, string> { ["domain"] = "acme.example.org" });
        notification.CreatedAt.Should().Be(First);
        notification.IsUnread.Should().BeTrue();
        notification.ReadAt.Should().BeNull();
        notification.DismissedAt.Should().BeNull();
    }

    [Fact]
    public void A_kind_or_key_that_is_empty_or_too_long_is_refused()
    {
        var empty = () => Notification.Create(OrganizationId, UserId, "", "abc", null, "/", First);
        var noKey = () => Notification.Create(OrganizationId, UserId, "kind", " ", null, "/", First);
        var longKind = () => Notification.Create(OrganizationId, UserId, new string('k', Notification.MaxKindLength + 1), "abc", null, "/", First);
        var longKey = () => Notification.Create(OrganizationId, UserId, "kind", new string('k', Notification.MaxKeyLength + 1), null, "/", First);

        empty.Should().Throw<ArgumentException>();
        noKey.Should().Throw<ArgumentException>();
        longKind.Should().Throw<ArgumentException>();
        longKey.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reading_marks_it_read_once_and_keeps_the_first_time()
    {
        var notification = New();

        notification.MarkRead(First.AddMinutes(1)).Should().BeTrue();
        notification.MarkRead(First.AddMinutes(5)).Should().BeFalse("the second read changes nothing");

        notification.ReadAt.Should().Be(First.AddMinutes(1));
        notification.IsUnread.Should().BeFalse();
        notification.DismissedAt.Should().BeNull("reading does not hide it");
    }

    [Fact]
    public void Dismissing_hides_it_counts_as_reading_and_keeps_the_first_time()
    {
        var notification = New();

        notification.Dismiss(First.AddMinutes(2)).Should().BeTrue();
        notification.Dismiss(First.AddMinutes(9)).Should().BeFalse("the second dismiss changes nothing");

        notification.DismissedAt.Should().Be(First.AddMinutes(2));
        notification.ReadAt.Should().Be(First.AddMinutes(2), "dismissing implies read");
        notification.IsUnread.Should().BeFalse();
    }

    [Fact]
    public void Dismissing_after_reading_keeps_the_time_it_was_read()
    {
        var notification = New();
        notification.MarkRead(First.AddMinutes(1));

        notification.Dismiss(First.AddMinutes(3)).Should().BeTrue();

        notification.ReadAt.Should().Be(First.AddMinutes(1));
        notification.DismissedAt.Should().Be(First.AddMinutes(3));
    }

    [Fact]
    public void Reading_after_dismissing_changes_nothing()
    {
        var notification = New();
        notification.Dismiss(First.AddMinutes(1));

        notification.MarkRead(First.AddMinutes(2)).Should().BeFalse("dismissing already read it");

        notification.ReadAt.Should().Be(First.AddMinutes(1));
    }
}
