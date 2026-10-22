using Npgsql;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The team tables' rules in the database itself, as the <c>screenhire_app</c> role on a raw connection, so the EF query
/// filter plays no part: another organization's invitations and membership log are invisible and cannot be written,
/// and the log is append-only.
/// </summary>
public sealed class TeamRowLevelSecurityTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_membership_log_can_be_added_and_read_but_never_changed_or_deleted()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        await AddInvitationAsync(organization.Id);

        var update = () => ExecuteAsync(organization.Id, "UPDATE membership_events SET occurred_at = now()");
        var delete = () => ExecuteAsync(organization.Id, "DELETE FROM membership_events");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await CountAsync(organization.Id, "membership_events")).Should().Be(1);
    }

    [Fact]
    public async Task Another_organizations_invitations_and_log_are_invisible_and_cannot_be_written()
    {
        var owner = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var other = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var invitationId = await AddInvitationAsync(owner.Id);

        (await CountAsync(owner.Id, "organization_invitations")).Should().Be(1);
        (await CountAsync(other.Id, "organization_invitations")).Should().Be(0);
        (await CountAsync(other.Id, "membership_events")).Should().Be(0);
        (await CountAsync(null, "organization_invitations")).Should().Be(0, "no tenant sees no rows");
        (await CountAsync(null, "membership_events")).Should().Be(0);

        var revoked = await ExecuteAsync(other.Id, "UPDATE organization_invitations SET revoked_at = now() WHERE id = @id", ("id", invitationId));
        revoked.Should().Be(0, "another organization's invitation matches no row");
        var insertInvitation = () => ExecuteAsync(other.Id, """
            INSERT INTO organization_invitations (id, organization_id, email, role, token_hash, invited_by_user_id, created_at, expires_at)
            VALUES (gen_random_uuid(), @org, 'planted@example.com', 'Owner', repeat('0', 64), gen_random_uuid(), now(), now() + interval '1 day')
            """, ("org", owner.Id));
        var insertEvent = () => ExecuteAsync(other.Id, """
            INSERT INTO membership_events (id, organization_id, kind, actor_user_id, occurred_at)
            VALUES (gen_random_uuid(), @org, 'Joined', gen_random_uuid(), now())
            """, ("org", owner.Id));

        (await insertInvitation.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await insertEvent.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await CountAsync(owner.Id, "organization_invitations")).Should().Be(1);
        (await CountAsync(owner.Id, "membership_events")).Should().Be(1);
    }

    // An invitation and its log entry, written as the organization through EF, as the API writes them.
    private async Task<Guid> AddInvitationAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var now = DateTimeOffset.UtcNow;
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = ApiSession.NewEmail("invitee"),
            Role = OrganizationRole.Viewer,
            TokenHash = new string('a', 64),
            InvitedByUserId = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
        };
        db.OrganizationInvitations.Add(invitation);
        db.MembershipEvents.Add(new MembershipEvent
        {
            OrganizationId = organizationId,
            Kind = MembershipEventKind.Invited,
            ActorUserId = invitation.InvitedByUserId,
            InvitationId = invitation.Id,
            Role = invitation.Role,
            OccurredAt = now,
        });
        await db.SaveChangesAsync(Ct);
        return invitation.Id;
    }

    private async Task<long> CountAsync(Guid? organizationId, string table)
    {
        await using var connection = await OpenAsync(organizationId);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection.Connection, connection.Transaction);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<int> ExecuteAsync(Guid organizationId, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(organizationId);
        await using var command = new NpgsqlCommand(sql, connection.Connection, connection.Transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var affected = await command.ExecuteNonQueryAsync(Ct);
        await connection.Transaction.CommitAsync(Ct);
        return affected;
    }

    // A transaction on a raw screenhire_app connection with app.tenant_id set transaction-locally, or not at all.
    private async Task<AppConnection> OpenAsync(Guid? organizationId)
    {
        var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        var transaction = await connection.BeginTransactionAsync(Ct);
        if (organizationId is { } id)
        {
            await using var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction);
            setTenant.Parameters.AddWithValue("tenant", id.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        return new AppConnection(connection, transaction);
    }

    private sealed record AppConnection(NpgsqlConnection Connection, NpgsqlTransaction Transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
