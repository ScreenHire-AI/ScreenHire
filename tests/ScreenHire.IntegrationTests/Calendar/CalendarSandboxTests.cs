using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Calendar;

/// <summary>
/// The sandbox calendar provider and the access that refreshes a member's token, against the real database and its
/// row-level security (docs/architecture/interview-scheduling.md). The provider is what the scheduling work calls where a
/// real one would call Google or Microsoft, so these tests pin what that work can rely on: busy time from the sandbox's
/// busy rows and live events, idempotent event creation, moving and cancelling, and a token for another organization
/// refused.
/// </summary>
public sealed class CalendarSandboxTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Day = new(2030, 6, 3, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Busy_time_is_the_busy_blocks_plus_live_events_and_nothing_cancelled()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var provider = Provider(db, organization);
        var token = SandboxCalendarProvider.AccessTokenFor(organization, user);
        db.CalendarSandboxBusy.Add(new CalendarSandboxBusy { OrganizationId = organization, UserId = user, StartsAt = Day.AddHours(9), EndsAt = Day.AddHours(10) });
        db.CalendarSandboxBusy.Add(new CalendarSandboxBusy { OrganizationId = organization, UserId = user, StartsAt = Day.AddDays(5), EndsAt = Day.AddDays(5).AddHours(1) });
        await db.SaveChangesAsync(Ct);
        await provider.CreateEventAsync(token, Spec("kept", Day.AddHours(13), Day.AddHours(14)), Ct);
        await provider.CreateEventAsync(token, Spec("dropped", Day.AddHours(15), Day.AddHours(16)), Ct);
        await provider.CancelEventAsync(token, "dropped", Ct);

        var busy = await provider.FreeBusyAsync(token, Day, Day.AddDays(1), Ct);

        busy.Should().Equal(
            new BusyInterval(Day.AddHours(9), Day.AddHours(10)),
            new BusyInterval(Day.AddHours(13), Day.AddHours(14)));
    }

    [Fact]
    public async Task One_members_busy_time_is_not_anothers()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user, colleague) = (owner.OrganizationId, Guid.CreateVersion7(), Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var provider = Provider(db, organization);
        db.CalendarSandboxBusy.Add(new CalendarSandboxBusy { OrganizationId = organization, UserId = user, StartsAt = Day.AddHours(9), EndsAt = Day.AddHours(10) });
        await db.SaveChangesAsync(Ct);

        (await provider.FreeBusyAsync(SandboxCalendarProvider.AccessTokenFor(organization, colleague), Day, Day.AddDays(1), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Creating_the_same_event_twice_gives_the_first_one_back_with_a_meeting_link_on_a_reserved_host()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var provider = Provider(db, organization);
        var token = SandboxCalendarProvider.AccessTokenFor(organization, user);

        var first = await provider.CreateEventAsync(token, Spec("interview-1", Day.AddHours(9), Day.AddHours(10)), Ct);
        var second = await provider.CreateEventAsync(token, Spec("interview-1", Day.AddHours(11), Day.AddHours(12)), Ct);

        second.Should().Be(first);
        first.EventId.Should().Be("interview-1");
        new Uri(first.MeetingUrl!).Host.Should().Be(SandboxCalendarProvider.MeetingHost).And.EndWith(".example.org");
        (await db.CalendarSandboxEvents.CountAsync(Ct)).Should().Be(1);
        (await provider.FreeBusyAsync(token, Day, Day.AddDays(1), Ct)).Should().ContainSingle()
            .Which.Start.Should().Be(Day.AddHours(9), "the second call changed nothing");
    }

    [Fact]
    public async Task An_event_without_a_meeting_link_has_none()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);

        var created = await Provider(db, organization).CreateEventAsync(
            SandboxCalendarProvider.AccessTokenFor(organization, user), Spec("plain", Day.AddHours(9), Day.AddHours(10)) with { CreateMeetingLink = false }, Ct);

        created.MeetingUrl.Should().BeNull();
    }

    [Fact]
    public async Task Moving_an_event_moves_its_busy_time_and_cancelling_it_frees_the_calendar()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var provider = Provider(db, organization);
        var token = SandboxCalendarProvider.AccessTokenFor(organization, user);
        var created = await provider.CreateEventAsync(token, Spec("interview-2", Day.AddHours(9), Day.AddHours(10)), Ct);

        var moved = await provider.MoveEventAsync(token, "interview-2", Day.AddHours(14), Day.AddHours(15), Ct);

        moved.Should().Be(created, "the id and the meeting link stay");
        (await provider.FreeBusyAsync(token, Day, Day.AddDays(1), Ct)).Should().ContainSingle().Which.Should().Be(new BusyInterval(Day.AddHours(14), Day.AddHours(15)));

        await provider.CancelEventAsync(token, "interview-2", Ct);
        await provider.CancelEventAsync(token, "interview-2", Ct);
        await provider.CancelEventAsync(token, "never-existed", Ct);
        (await provider.FreeBusyAsync(token, Day, Day.AddDays(1), Ct)).Should().BeEmpty();
        await provider.Invoking(p => p.MoveEventAsync(token, "never-existed", Day, Day.AddHours(1), Ct))
            .Should().ThrowAsync<CalendarApiException>().Where(e => !e.Permanent);
    }

    [Fact]
    public async Task A_token_for_another_organization_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var provider = Provider(db, organization);

        await provider.Invoking(p => p.FreeBusyAsync(SandboxCalendarProvider.AccessTokenFor(Guid.CreateVersion7(), user), Day, Day.AddDays(1), Ct))
            .Should().ThrowAsync<CalendarApiException>().Where(e => e.Permanent);
        await provider.Invoking(p => p.FreeBusyAsync("not-a-token", Day, Day.AddDays(1), Ct))
            .Should().ThrowAsync<CalendarApiException>().Where(e => e.Permanent);
    }

    [Fact]
    public async Task A_token_close_to_its_expiry_is_refreshed_and_stored_and_a_fresh_one_is_left_alone()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var (access, tokens) = Access(db, organization);
        var connection = AddConnection(db, tokens, organization, user, "stale-access", expiresAt: DateTimeOffset.UtcNow.AddSeconds(30));
        await db.SaveChangesAsync(Ct);

        var refreshed = await access.GetSessionAsync(user, Ct);

        refreshed.Should().NotBeNull();
        refreshed!.AccessToken.Should().Be(SandboxCalendarProvider.AccessTokenFor(organization, user));
        refreshed.Provider.Id.Should().Be(CalendarProvider.Sandbox);
        connection.AccessTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(30));
        tokens.Unprotect(organization, user, connection.ProtectedAccessToken).Should().Be(refreshed.AccessToken);
        tokens.Unprotect(organization, user, connection.ProtectedRefreshToken).Should().Be(RefreshToken(organization, user), "the sandbox does not rotate it");

        var expiry = connection.AccessTokenExpiresAt;
        (await access.GetSessionAsync(user, Ct))!.AccessToken.Should().Be(refreshed.AccessToken);
        connection.AccessTokenExpiresAt.Should().Be(expiry, "a token with time left is not refreshed again");
    }

    [Fact]
    public async Task A_refresh_the_provider_refuses_for_good_marks_the_connection_as_needing_a_reconnect()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var (access, tokens) = Access(db, organization);
        var connection = AddConnection(db, tokens, organization, user, "stale-access", expiresAt: DateTimeOffset.UtcNow.AddSeconds(30), refresh: "revoked");
        await db.SaveChangesAsync(Ct);

        (await access.GetSessionAsync(user, Ct)).Should().BeNull();

        connection.Problem.Should().Be(CalendarProblem.NeedsReconnect);
        (await access.GetSessionAsync(user, Ct)).Should().BeNull("a connection with a problem stays unusable until the member connects again");
    }

    [Fact]
    public async Task Tokens_that_no_longer_read_mark_the_connection_as_needing_a_reconnect()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (organization, user) = (owner.OrganizationId, Guid.CreateVersion7());
        await using var db = await OrganizationAsync(organization);
        var (access, _) = Access(db, organization);
        // Protected by a key ring this host does not have: the key ring lost its key.
        var foreign = new CalendarTokens(new EphemeralDataProtectionProvider());
        var connection = AddConnection(db, foreign, organization, user, SandboxCalendarProvider.AccessTokenFor(organization, user), DateTimeOffset.UtcNow.AddHours(1));
        await db.SaveChangesAsync(Ct);

        (await access.GetSessionAsync(user, Ct)).Should().BeNull();

        connection.Problem.Should().Be(CalendarProblem.NeedsReconnect);
    }

    [Fact]
    public async Task A_member_with_no_calendar_has_no_session_and_a_provider_that_is_off_is_not_found()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var organization = owner.OrganizationId;
        await using var db = await OrganizationAsync(organization);
        var (access, _) = Access(db, organization);

        (await access.GetSessionAsync(Guid.CreateVersion7(), Ct)).Should().BeNull();

        access.Find(CalendarProvider.Sandbox).Should().NotBeNull();
        access.Find(CalendarProvider.Google).Should().BeNull("a provider needs its client id and secret");
        access.Providers.Select(p => p.Id).Should().Equal(CalendarProvider.Sandbox);
    }

    private static string RefreshToken(Guid organization, Guid user) => $"sandbox-refresh.{organization:N}.{user:N}";

    private static EventSpec Spec(string id, DateTimeOffset start, DateTimeOffset end) =>
        new(id, "Interview", "Candidate and job", start, end, ["candidate@example.com"], CreateMeetingLink: true);

    private static CalendarConnection AddConnection(
        ScreenHireDbContext db, CalendarTokens tokens, Guid organization, Guid user, string access, DateTimeOffset expiresAt, string? refresh = null)
    {
        var connection = CalendarConnection.Connect(
            organization,
            user,
            CalendarProvider.Sandbox,
            "avery@example.com",
            tokens.Protect(organization, user, access),
            tokens.Protect(organization, user, refresh ?? RefreshToken(organization, user)),
            expiresAt,
            DateTimeOffset.UtcNow);
        db.CalendarConnections.Add(connection);
        return connection;
    }

    private static SandboxCalendarProvider Provider(ScreenHireDbContext db, Guid organization) => new(db, TenantOf(organization), TimeProvider.System);

    private static (CalendarAccess Access, CalendarTokens Tokens) Access(ScreenHireDbContext db, Guid organization)
    {
        var tokens = new CalendarTokens(new EphemeralDataProtectionProvider());
        var options = Options.Create(new CalendarOptions { Sandbox = new CalendarSandboxOptions { Enabled = true } });
        var access = new CalendarAccess(
            options, tokens, db, TimeProvider.System, new GoogleCalendarProvider(options, TimeProvider.System), new MicrosoftCalendarProvider(options, TimeProvider.System), Provider(db, organization));
        return (access, tokens);
    }

    private static TenantContext TenantOf(Guid organization)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organization);
        return tenant;
    }

    // A context bound to the organization, which exists in the database so the rows' foreign keys hold.
    private async Task<ScreenHireDbContext> OrganizationAsync(Guid organization) => await fixture.CreateDbContextAsync(TenantOf(organization), Ct);
}
