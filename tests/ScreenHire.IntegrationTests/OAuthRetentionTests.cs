using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.OAuth;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The daily sweep of OAuth data (docs/architecture/oauth-server.md, "Retention"): registrations nobody approved go after a
/// day, and each organization's codes and tokens go a day after they expire. The worker has no tenant of its own, so the
/// daily message hands each organization to a tenant message, and these tests send both through the real worker. Rows are
/// aged through the owner connection, as state no endpoint can reach (docs/process/testing.md). Other tests expire their
/// rows by a minute, never by a day, so a sweep that reaches every organization takes nothing they use. The two tests
/// here do send that sweep, and with every test in parallel the second test's daily message reached the first test's
/// organizations while it had rows aged two days, deleting one before it asserted that it stayed. They share a collection
/// that xUnit runs alone, after the parallel tests.
/// </summary>
[Collection<OAuthRetention>]
public sealed class OAuthRetentionTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_organizations_sweep_deletes_codes_and_tokens_past_the_cutoff_and_keeps_unexpired_and_used_refresh_tokens()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);

        // First: a connection that refreshed once, so it holds a code, a used refresh token that has not expired, and two
        // access tokens. A second connection has an access token that expired an hour ago and a code that expired a minute ago.
        var refreshed = await first.ConnectAsync(app, "Sweep one", ["Read"]);
        var newer = (await app.RefreshAsync(refreshed.Request.ClientId, refreshed.RefreshToken, refreshed.Request.Resource)).Expect(HttpStatusCode.OK);
        newer.Text("refresh_token").Should().NotBe(refreshed.RefreshToken);
        var recent = await first.ConnectAsync(app, "Sweep two", ["Read"]);
        // Second organization: the same shape as the first connection, without the refresh.
        var neighbour = await second.ConnectAsync(app, "Sweep three", ["Read"]);

        var one = await RowsAsync(refreshed.ConnectionId);
        one.Codes.Should().ContainSingle();
        var accessTokens = one.Tokens.Where(t => t.Kind == "Access").OrderBy(t => t.CreatedAt).ToList();
        accessTokens.Should().HaveCount(2, "the connection holds the access token from the exchange and the one from the refresh");
        var oldAccess = accessTokens[0];
        var newAccess = accessTokens[1];
        var usedRefresh = one.Tokens.Single(t => t.Kind == "Refresh" && t.UsedAt is not null);
        var newRefresh = one.Tokens.Single(t => t.Kind == "Refresh" && t.UsedAt is null);
        var two = await RowsAsync(recent.ConnectionId);
        var other = await RowsAsync(neighbour.ConnectionId);
        other.Codes.Should().ContainSingle();
        var otherAccess = other.Tokens.Single(t => t.Kind == "Access");
        var otherRefresh = other.Tokens.Single(t => t.Kind == "Refresh");

        await ExpireAsync("oauth_codes", one.Codes[0].Id, "4 days");
        await ExpireAsync("oauth_tokens", oldAccess.Id, "4 days");
        await ExpireAsync("oauth_tokens", newAccess.Id, "2 days");
        await ExpireAsync("oauth_tokens", two.Tokens.Single(t => t.Kind == "Access").Id, "1 hour");
        await ExpireAsync("oauth_codes", two.Codes[0].Id, "1 minute");
        await ExpireAsync("oauth_codes", other.Codes[0].Id, "4 days");
        await ExpireAsync("oauth_tokens", otherAccess.Id, "4 days");

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        var bus = sender.Services.GetRequiredService<IMessageBus>();

        // What the daily sweep sends for the first organization, with a cutoff three days back: only the rows that expired
        // before that go. The cutoff is the message's own, so the grace period is proved in the daily sweep below.
        await bus.PublishAsync(new PurgeOrganizationOAuthData(first.OrganizationId, DateTimeOffset.UtcNow.AddDays(-3)));
        await WolverineStore.WaitUntilAsync(
            async () => !(await IdsAsync(refreshed.ConnectionId)).Contains(oldAccess.Id), "the sweep to delete the first organization's old access token", Ct);
        var afterOrganizationSweep = await IdsAsync(refreshed.ConnectionId);
        afterOrganizationSweep.Should().NotContain(one.Codes[0].Id, "its code expired four days ago");
        afterOrganizationSweep.Should().Contain([newAccess.Id, usedRefresh.Id, newRefresh.Id], "newer expiries and unexpired refresh tokens stay");
        (await IdsAsync(neighbour.ConnectionId)).Should().Contain([other.Codes[0].Id, otherAccess.Id, otherRefresh.Id],
            "another organization's rows are left to its own message");

        // The daily message: cutoff is a day before now, and it reaches every organization.
        await bus.PublishAsync(new PurgeExpiredOAuthData());
        await WolverineStore.WaitUntilAsync(
            async () => !(await IdsAsync(neighbour.ConnectionId)).Contains(otherAccess.Id), "the daily sweep to reach the other organization", Ct);
        (await IdsAsync(neighbour.ConnectionId)).Should().Equal([otherRefresh.Id], "the other organization's expired code and access token went, its refresh token did not");
        await WolverineStore.WaitUntilAsync(
            async () => !(await IdsAsync(refreshed.ConnectionId)).Contains(newAccess.Id), "the daily sweep to delete the access token that expired two days ago", Ct);
        (await IdsAsync(refreshed.ConnectionId)).Should().BeEquivalentTo([usedRefresh.Id, newRefresh.Id],
            "a used refresh token stays until it would have expired, because reuse detection needs it");

        // An hour past expiry is inside the day of grace; both of the second connection's rows survive both sweeps. They
        // would have gone in the statement that took the first organization's access token.
        var kept = await IdsAsync(recent.ConnectionId);
        kept.Should().Contain([two.Tokens.Single(t => t.Kind == "Access").Id, two.Codes[0].Id]);
    }

    [Fact]
    public async Task The_daily_sweep_deletes_registrations_nobody_approved_after_a_day_and_keeps_newer_and_approved_ones()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var app = new OAuthClientApp(fixture);
        var abandoned = await app.RegisterClientAsync("Never approved", OAuthSupport.ClaudeCallback);
        var young = await app.RegisterClientAsync("Registered just now", OAuthSupport.ClaudeCallback);
        // Approved but the code was never exchanged: LastUsedAt is set, which is what the sweep keeps a registration for.
        var request = await app.NewRequestAsync("Approved long ago");
        await owner.ApproveAsync(request, "Read");
        await AgeClientAsync(Guid.Parse(abandoned), "2 days");
        await AgeClientAsync(Guid.Parse(request.ClientId), "2 days");

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeExpiredOAuthData());

        await WolverineStore.WaitUntilAsync(async () => !await ClientExistsAsync(abandoned), "the sweep to delete the registration nobody approved", Ct);
        (await ClientExistsAsync(young)).Should().BeTrue("a registration under a day old may still be approved");
        (await ClientExistsAsync(request.ClientId)).Should().BeTrue("an approved client is kept, and a connection still names it");
        (await owner.GetAsync("/v1/organization/oauth-connections")).Expect(HttpStatusCode.OK).Body.GetArrayLength()
            .Should().Be(1, "the connection of the approved client is untouched");
    }

    private sealed record CodeRow(Guid Id);

    private sealed record TokenRow(Guid Id, string Kind, DateTimeOffset CreatedAt, DateTimeOffset? UsedAt);

    private sealed record ConnectionRows(IReadOnlyList<CodeRow> Codes, IReadOnlyList<TokenRow> Tokens);

    private async Task<ConnectionRows> RowsAsync(Guid connectionId)
    {
        await using var connection = await OpenOwnerAsync();
        List<CodeRow> codes = [];
        await using (var command = new NpgsqlCommand("SELECT id FROM oauth_codes WHERE api_key_id = @key", connection))
        {
            command.Parameters.AddWithValue("key", connectionId);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                codes.Add(new CodeRow(reader.GetGuid(0)));
            }
        }

        List<TokenRow> tokens = [];
        await using (var command = new NpgsqlCommand("SELECT id, kind, created_at, used_at FROM oauth_tokens WHERE api_key_id = @key", connection))
        {
            command.Parameters.AddWithValue("key", connectionId);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                tokens.Add(new TokenRow(
                    reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }

        return new ConnectionRows(codes, tokens);
    }

    // Every code and token id of one connection.
    private async Task<IReadOnlyList<Guid>> IdsAsync(Guid connectionId)
    {
        var rows = await RowsAsync(connectionId);
        return [.. rows.Codes.Select(c => c.Id), .. rows.Tokens.Select(t => t.Id)];
    }

    private async Task ExpireAsync(string table, Guid id, string ago)
    {
        await using var connection = await OpenOwnerAsync();
        // The table and the interval are this file's own constants, never input.
        await using var command = new NpgsqlCommand($"UPDATE {table} SET expires_at = now() - interval '{ago}' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteNonQueryAsync(Ct)).Should().Be(1);
    }

    private async Task AgeClientAsync(Guid clientId, string ago)
    {
        await using var connection = await OpenOwnerAsync();
        await using var command = new NpgsqlCommand($"UPDATE oauth_clients SET created_at = now() - interval '{ago}' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", clientId);
        (await command.ExecuteNonQueryAsync(Ct)).Should().Be(1);
    }

    private async Task<bool> ClientExistsAsync(string clientId)
    {
        await using var connection = await OpenOwnerAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM oauth_clients WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", Guid.Parse(clientId));
        return (long)(await command.ExecuteScalarAsync(Ct))! == 1;
    }

    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        return connection;
    }
}

/// <summary>The OAuth retention tests, which send the daily sweep to every organization. Run alone, after the parallel tests.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class OAuthRetention;
