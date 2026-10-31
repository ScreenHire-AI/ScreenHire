using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.UnitTests.Organizations;

public sealed class NotificationSettingsTests
{
    private static OrganizationMember Member() =>
        OrganizationMember.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), OrganizationRole.Recruiter, new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_new_member_has_everything_on()
    {
        var settings = Member().Notifications;

        settings.Toasts.Should().BeTrue();
        settings.CloseToasts.Should().BeTrue();
        settings.Off.Should().BeEmpty();
    }

    [Fact]
    public void Saving_the_same_settings_again_changes_nothing()
    {
        var member = Member();

        member.SetNotifications(new NotificationSettings { Off = [ToastKind.Moves] }).Should().BeTrue();
        member.SetNotifications(new NotificationSettings { Off = [ToastKind.Moves] }).Should().BeFalse();
        member.SetNotifications(NotificationSettings.Default).Should().BeTrue();
    }

    [Fact]
    public void The_stored_json_reads_back_and_an_empty_object_is_the_defaults()
    {
        var settings = new NotificationSettings { Toasts = false, CloseToasts = false, Off = [ToastKind.Tasks, ToastKind.Templates] };

        JsonColumns.Deserialize<NotificationSettings>(JsonColumns.Serialize(settings)).Should().Be(settings);
        JsonColumns.Serialize(NotificationSettings.Default).Should().Be("""{"toasts":true,"closeToasts":true,"off":[]}""");
        JsonColumns.Deserialize<NotificationSettings>("{}").Should().Be(NotificationSettings.Default);
    }
}
