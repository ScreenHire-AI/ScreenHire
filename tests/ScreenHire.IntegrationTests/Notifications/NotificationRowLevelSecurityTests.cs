using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Notifications;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Notifications;

/// <summary>
/// The notifications table in the database itself, as the <c>screenhire_app</c> role the API and worker use: another
/// organization sees none of the rows, even with the query filters off, and cannot write one into this organization. The catalogue
/// check in <c>RowLevelSecurityTests</c> covers that the table has its policy; this proves the policy holds.
/// </summary>
public sealed class NotificationRowLevelSecurityTests(AppHostFixture fixture)
{
    // Far in the future, so the real worker's daily purge never reaches these rows.
    private static readonly DateTimeOffset Now = new(2099, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_stays_with_its_organization_even_with_the_query_filters_off()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await fixture.AddAsync(ours.OrganizationId, ours.UserId, NotificationSupport.NewKey(), Now);

        await using (var own = await ContextAsync(ours.OrganizationId))
        {
            (await own.Notifications.CountAsync(Ct)).Should().Be(1);
        }

        await using var outside = await ContextAsync(theirs.OrganizationId);
        (await outside.Notifications.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0, "row-level security hides the row even without the query filter");
        (await outside.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == ours.UserId, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task With_no_tenant_set_no_notification_is_visible()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.AddAsync(owner.OrganizationId, owner.UserId, NotificationSupport.NewKey(), Now);

        await using var db = await fixture.CreateDbContextAsync(new TenantContext(), Ct);

        (await db.Notifications.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Another_organizations_context_cannot_write_a_notification_into_this_one()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var key = NotificationSupport.NewKey();

        await using (var outside = await ContextAsync(theirs.OrganizationId))
        {
            outside.Notifications.Add(Notification.Create(
                ours.OrganizationId, ours.UserId, NotificationKinds.SendingDomainFailing, key, null, NotificationSupport.Link, Now));
            var write = async () => await outside.SaveChangesAsync(Ct);
            await write.Should().ThrowAsync<DbUpdateException>("the policy refuses a row for another organization");
        }

        (await fixture.OwnerScalarAsync<long>("SELECT count(*) FROM notifications WHERE key = @key", ("key", key))).Should().Be(0);
    }

    private async Task<ScreenHireDbContext> ContextAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
