using System.Net.Http.Json;
using System.Text.Json;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// The bell's routes (docs/architecture/notifications.md): a member sees only their own notifications, the worker raises one per
/// Owner and clears every Owner's copy, and a repeat changes nothing. Messages are put on the running worker's queue the way a
/// handler of the worker returns them. Each test uses its own organization and its own key.
/// </summary>
public sealed class NotificationTests(AppHostFixture fixture)
{
    private const string Path = "/v1/notifications";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signed_out_callers_get_401_on_every_route()
    {
        using var anonymous = fixture.CreateHttpClient("api");
        var id = Guid.NewGuid();

        (await anonymous.GetAsync(Path, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Path}/read-all", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Path}/{id}/read", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Path}/{id}/dismiss", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_new_member_has_an_empty_bell()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var list = await owner.NotificationsAsync();

        list.Count("items").Should().Be(0);
        list.At("unreadCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_raised_notification_reaches_the_owner_with_its_facts_and_not_an_admin()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var key = NotificationSupport.NewKey();

        await fixture.RaiseAsync(owner.OrganizationId, key, domain: "acme.example.org");
        await fixture.WaitHandledAsync(key, "RaiseNotification");

        var list = await owner.WaitForItemsAsync(1);
        var item = list.At("items.0");
        item.GetProperty("kind").GetString().Should().Be("sending-domain-failing");
        item.GetProperty("payload").GetProperty("domain").GetString().Should().Be("acme.example.org");
        item.GetProperty("link").GetString().Should().Be("/settings/sending-domain");
        item.GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.String);
        list.At("unreadCount").GetInt32().Should().Be(1);

        var adminList = await admin.NotificationsAsync();
        adminList.Count("items").Should().Be(0, "an Admin does not hold ManageOwners, so the notification is not addressed to them");
        adminList.At("unreadCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Every_owner_gets_their_own_copy()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        var key = NotificationSupport.NewKey();

        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");

        var one = await first.WaitForItemsAsync(1);
        var two = await second.WaitForItemsAsync(1);
        one.At("items.0.id").GetGuid().Should().NotBe(two.At("items.0.id").GetGuid(), "each person has a row of their own");
    }

    [Fact]
    public async Task Raising_the_same_notification_twice_is_one_row()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();

        await fixture.RaiseAsync(owner.OrganizationId, key);
        await fixture.RaiseAsync(owner.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification", count: 2);

        var list = await owner.NotificationsAsync();
        list.Count("items").Should().Be(1);
        list.At("unreadCount").GetInt32().Should().Be(1);
        (await fixture.OwnerScalarAsync<long>("SELECT count(*) FROM notifications WHERE organization_id = @org AND key = @key", ("org", owner.OrganizationId), ("key", key)))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_different_key_is_a_second_notification()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var first = NotificationSupport.NewKey();
        var second = NotificationSupport.NewKey();

        await fixture.RaiseAsync(owner.OrganizationId, first, domain: "one.example.org");
        await fixture.WaitHandledAsync(first, "RaiseNotification");
        await fixture.RaiseAsync(owner.OrganizationId, second, domain: "two.example.org");
        await fixture.WaitHandledAsync(second, "RaiseNotification");

        var list = await owner.WaitForItemsAsync(2);
        list.At("unreadCount").GetInt32().Should().Be(2);
        list.At("items.0.payload.domain").GetString().Should().Be("two.example.org", "the newest comes first");
    }

    [Fact]
    public async Task Reading_one_keeps_it_listed_drops_the_unread_count_and_a_second_read_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(owner.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var id = (await owner.WaitForItemsAsync(1)).At("items.0.id").GetGuid();

        (await owner.PostAsync($"{Path}/{id}/read")).Expect(HttpStatusCode.NoContent);

        var read = await owner.NotificationsAsync();
        read.Count("items").Should().Be(1, "reading does not hide it");
        read.At("unreadCount").GetInt32().Should().Be(0);
        var readAt = read.At("items.0.readAt").GetString();
        readAt.Should().NotBeNull();

        (await owner.PostAsync($"{Path}/{id}/read")).Expect(HttpStatusCode.NoContent);
        (await owner.NotificationsAsync()).At("items.0.readAt").GetString().Should().Be(readAt, "a second read keeps the first time");
    }

    [Fact]
    public async Task Read_all_marks_every_unread_notification_of_the_caller_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.InviteAndJoinAsync(owner, "Owner");
        var one = NotificationSupport.NewKey();
        var two = NotificationSupport.NewKey();
        await fixture.RaiseAsync(owner.OrganizationId, one, domain: "one.example.org");
        await fixture.WaitHandledAsync(one, "RaiseNotification");
        await fixture.RaiseAsync(owner.OrganizationId, two, domain: "two.example.org");
        await fixture.WaitHandledAsync(two, "RaiseNotification");
        (await owner.WaitForItemsAsync(2)).At("unreadCount").GetInt32().Should().Be(2);
        await other.WaitForItemsAsync(2);

        (await owner.PostAsync($"{Path}/read-all")).Expect(HttpStatusCode.NoContent);

        var mine = await owner.NotificationsAsync();
        mine.At("unreadCount").GetInt32().Should().Be(0);
        mine.Count("items").Should().Be(2);
        mine.Body.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("readAt").ValueKind == JsonValueKind.String);
        (await other.NotificationsAsync()).At("unreadCount").GetInt32().Should().Be(2, "another person's bell is untouched");
        (await owner.PostAsync($"{Path}/read-all")).Expect(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Dismissing_hides_it_and_drops_the_unread_count_for_that_person_only()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var id = (await first.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await second.WaitForItemsAsync(1);

        (await first.PostAsync($"{Path}/{id}/dismiss")).Expect(HttpStatusCode.NoContent);

        var mine = await first.NotificationsAsync();
        mine.Count("items").Should().Be(0, "a dismissed notification leaves the list");
        mine.At("unreadCount").GetInt32().Should().Be(0);
        var theirs = await second.NotificationsAsync();
        theirs.Count("items").Should().Be(1, "the other Owner's copy stays");
        theirs.At("unreadCount").GetInt32().Should().Be(1);

        (await first.PostAsync($"{Path}/{id}/dismiss")).Expect(HttpStatusCode.NoContent);
        (await first.PostAsync($"{Path}/{id}/read")).Expect(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Another_members_notification_or_an_unknown_id_is_a_404_for_read_and_dismiss_and_changes_nothing()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var firstsId = (await first.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await second.WaitForItemsAsync(1);

        (await second.PostAsync($"{Path}/{firstsId}/read")).Expect(HttpStatusCode.NotFound);
        (await second.PostAsync($"{Path}/{firstsId}/dismiss")).Expect(HttpStatusCode.NotFound);
        (await second.PostAsync($"{Path}/{Guid.NewGuid()}/read")).Expect(HttpStatusCode.NotFound);
        (await second.PostAsync($"{Path}/{Guid.NewGuid()}/dismiss")).Expect(HttpStatusCode.NotFound);

        var untouched = await first.NotificationsAsync();
        untouched.Count("items").Should().Be(1);
        untouched.At("unreadCount").GetInt32().Should().Be(1, "the other Owner's attempts changed nothing");
    }

    [Fact]
    public async Task Another_organizations_notification_is_a_404_too()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(theirs.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var theirId = (await theirs.WaitForItemsAsync(1)).At("items.0.id").GetGuid();

        (await ours.PostAsync($"{Path}/{theirId}/read")).Expect(HttpStatusCode.NotFound);
        (await ours.PostAsync($"{Path}/{theirId}/dismiss")).Expect(HttpStatusCode.NotFound);
        (await ours.NotificationsAsync()).Count("items").Should().Be(0);
    }

    [Fact]
    public async Task Clearing_removes_every_owners_copy_and_raising_again_makes_a_fresh_unread_one()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.InviteAndJoinAsync(first, "Owner");
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var before = (await first.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        await second.WaitForItemsAsync(1);
        (await first.PostAsync($"{Path}/{before}/read")).Expect(HttpStatusCode.NoContent);

        await fixture.ClearAsync(first.OrganizationId, key);

        await first.WaitForItemsAsync(0);
        (await second.NotificationsAsync()).Count("items").Should().Be(0, "a clear finds every person's copy");

        await fixture.RaiseAsync(first.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification", count: 2);
        var again = await first.WaitForItemsAsync(1);
        again.At("items.0.id").GetGuid().Should().NotBe(before, "it is a new row");
        again.At("items.0.readAt").ValueKind.Should().Be(JsonValueKind.Null);
        again.At("unreadCount").GetInt32().Should().Be(1);
        (await second.WaitForItemsAsync(1)).At("unreadCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Clearing_something_never_raised_changes_nothing_and_leaves_other_notifications()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var kept = NotificationSupport.NewKey();
        var other = NotificationSupport.NewKey();
        await fixture.RaiseAsync(owner.OrganizationId, kept);
        await fixture.WaitHandledAsync(kept, "RaiseNotification");
        await owner.WaitForItemsAsync(1);

        await fixture.ClearAsync(owner.OrganizationId, other);
        await fixture.WaitHandledAsync(other, "ClearNotification");

        (await owner.NotificationsAsync()).Count("items").Should().Be(1);
    }

    [Fact]
    public async Task A_dismissed_notification_is_not_raised_again_while_its_row_exists()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(owner.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification");
        var id = (await owner.WaitForItemsAsync(1)).At("items.0.id").GetGuid();
        (await owner.PostAsync($"{Path}/{id}/dismiss")).Expect(HttpStatusCode.NoContent);

        await fixture.RaiseAsync(owner.OrganizationId, key);
        await fixture.WaitHandledAsync(key, "RaiseNotification", count: 2);

        (await owner.NotificationsAsync()).Count("items").Should().Be(0, "the person already dismissed it");
        (await fixture.OwnerScalarAsync<long>("SELECT count(*) FROM notifications WHERE organization_id = @org AND key = @key", ("org", owner.OrganizationId), ("key", key)))
            .Should().Be(1, "the dismissed row still holds the place");
    }

    [Fact]
    public async Task A_raise_with_a_link_outside_the_dashboard_is_refused_and_nothing_is_written()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();
        await fixture.RaiseAsync(owner.OrganizationId, key, link: "https://evil.example/settings");

        await fixture.WaitHandledAsync(key, "RaiseNotification");

        (await owner.NotificationsAsync()).Count("items").Should().Be(0);
    }

    [Fact]
    public async Task The_list_holds_the_newest_fifty_and_the_unread_count_covers_every_unread_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        // Far in the future, so the real worker's daily purge (another test runs it) never reaches these rows.
        var start = new DateTimeOffset(2099, 6, 1, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 55; i++)
        {
            await fixture.AddAsync(owner.OrganizationId, owner.UserId, $"bulk-{i:00}-{Guid.NewGuid():N}", start.AddMinutes(i), domain: $"d{i:00}.example.org");
        }

        var list = await owner.NotificationsAsync();

        list.Count("items").Should().Be(50);
        list.At("unreadCount").GetInt32().Should().Be(55, "the count is not the page");
        list.At("items.0.payload.domain").GetString().Should().Be("d54.example.org", "newest first");
        list.At("items.49.payload.domain").GetString().Should().Be("d05.example.org");
    }
}
