using ScreenHire.IntegrationTests.Realtime;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// A change to a bell reaches the organization's open dashboards as <c>NotificationsChanged</c>, which carries the organization
/// and nothing about the notification, so a teammate's bell refetches; another organization hears nothing
/// (docs/architecture/notifications.md).
/// </summary>
public sealed class NotificationRealtimeTests(AppHostFixture fixture)
{
    private const string EventType = "NotificationsChanged";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Raising_and_dismissing_reach_the_organizations_dashboards_and_never_another_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ownerClient = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, Ct);
        var key = NotificationSupport.NewKey();

        await fixture.RaiseAsync(owner.OrganizationId, key, domain: "private-name.example.org");

        var raised = await ownerClient.WaitForAsync(EventType, "organizationId", owner.OrganizationId, Ct);
        raised.ToString().Should().NotContain("private-name.example.org", "the event carries the organization only, never the words");
        var id = (await owner.WaitForItemsAsync(1)).At("items.0.id").GetGuid();

        (await owner.PostAsync($"/v1/notifications/{id}/dismiss")).Expect(HttpStatusCode.NoContent);

        // The raise already delivered one event of this type, so a second one can only be the dismiss's.
        await WolverineStoreWait(() => ownerClient.Received.Count(e => e.Type == EventType) >= 2, "a second NotificationsChanged after the dismiss");
        ownerClient.Received.Where(e => e.Type == EventType).Should().OnlyContain(e => e.Data.GetProperty("organizationId").GetGuid() == owner.OrganizationId);

        // Our last event has arrived, so an event of ours sent to them would have too.
        theirClient.ReceivedAnythingAbout(owner.OrganizationId).Should().BeFalse("another organization never hears about our bell");
        theirClient.Received.Should().NotContain(e => e.Type == EventType);
    }

    [Fact]
    public async Task Reading_a_notification_reaches_a_teammates_open_dashboard_too()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var id = (await first.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await using var secondClient = await EventsConnection.ConnectAsync(second.Session, Ct);

        (await first.PostAsync($"/v1/notifications/{id}/read")).Expect(HttpStatusCode.NoContent);

        await secondClient.WaitForAsync(EventType, "organizationId", first.OrganizationId, Ct);
    }

    // Polls a condition on the client's own record of events, as EventsConnection.WaitForAsync does for one event.
    private static Task WolverineStoreWait(Func<bool> condition, string because) =>
        Messaging.WolverineStore.WaitUntilAsync(() => Task.FromResult(condition()), because, Ct);
}
