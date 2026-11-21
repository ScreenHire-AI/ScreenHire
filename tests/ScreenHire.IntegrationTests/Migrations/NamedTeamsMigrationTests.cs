using Npgsql;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Hiring templates lose their default hiring team, and named teams take their place. The migration keeps every own
/// template's list of people who are still members as a team named after the template, so nobody's setup is lost: a
/// second template of the same name, in any case, gets a number, a list of people who all left makes no team, and the
/// tables keep row-level security forced.
/// </summary>
public sealed class NamedTeamsMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddOrganizationSetupChoices";
    private const string After = "AddNamedTeams";

    [Fact]
    public async Task Each_templates_default_team_of_current_members_becomes_a_team_named_after_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var studio = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var (sam, jo, kim) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (samMember, joMember, kimMember) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var left = Guid.CreateVersion7(); // a membership that no longer exists

        string Organization(Guid id, string name) => $"""
            INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in, setup_hidden)
            VALUES ('{id}', '{name}', 'org-{id:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false, false);
            """;
        string User(Guid id) => $"""
            INSERT INTO users (id, access_failed_count, created_at, email_confirmed, lockout_enabled, phone_number_confirmed, two_factor_enabled)
            VALUES ('{id}', 0, now(), true, false, false, false);
            """;
        string Member(Guid id, Guid organization, Guid user) =>
            $"INSERT INTO organization_members (id, organization_id, user_id, role, created_at) VALUES ('{id}', '{organization}', '{user}', 'Recruiter', now());";
        string Template(Guid organization, string name, int minutesAgo, Guid[] team, bool archived = false) => $"""
            INSERT INTO hiring_templates (id, organization_id, name, created_at, archived_at, default_hiring_team)
            VALUES ('{Guid.CreateVersion7()}', '{organization}', '{name}', now() - interval '{minutesAgo} minutes', {(archived ? "now()" : "NULL")},
                    ARRAY[{string.Join(", ", team.Select(id => $"'{id}'"))}]::uuid[]);
            """;

        // The owner is a superuser here, which row-level security does not bind.
        await scratch.ExecuteAsync($"""
            {Organization(studio, "Harbour Lane Studio")}
            {Organization(other, "Northgate Bakery")}
            {User(sam)} {User(jo)} {User(kim)}
            {Member(samMember, studio, sam)}
            {Member(joMember, studio, jo)}
            {Member(kimMember, other, kim)}
            {Template(studio, "Kitchen", 30, [samMember, joMember, left, samMember])}
            {Template(studio, "kitchen", 20, [joMember])}
            {Template(studio, "Old menu", 10, [samMember], archived: true)}
            {Template(studio, "Bar", 5, [left])}
            {Template(studio, "Front of house", 5, [])}
            {Template(studio, "Borrowed", 5, [kimMember])}
            {Template(other, "Kitchen", 5, [kimMember])}
            """, ct);

        await scratch.MigrateAsync(After, ct);

        var teams = await TeamsAsync(scratch.ConnectionString, ct);
        teams.Where(t => t.Organization == studio).Select(t => (t.Name, t.NormalizedName, t.People)).Should().BeEquivalentTo(
            [
                ("Kitchen team", "KITCHEN TEAM", new[] { samMember, joMember }),
                ("kitchen team 2", "KITCHEN TEAM 2", new[] { joMember }),
                ("Old menu team", "OLD MENU TEAM", new[] { samMember }),
            ],
            options => options.WithoutStrictOrdering(),
            "people who left, empty lists and another organization's members make no team, and a name taken in any case gets a number");
        teams.Where(t => t.Organization == other).Select(t => t.Name).Should().Equal("Kitchen team");
        (await scratch.ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_name = 'hiring_templates' AND column_name = 'default_hiring_team'", ct))
            .Should().Be(0, "the template field is gone");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE comes back on the tables the copy lifted it from");

        await scratch.MigrateAsync(Before, ct);

        (await scratch.ScalarAsync<long>("SELECT count(*) FROM information_schema.tables WHERE table_name = 'named_teams'", ct)).Should().Be(0);
        (await scratch.ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_name = 'hiring_templates' AND column_name = 'default_hiring_team'", ct))
            .Should().Be(1, "undoing the migration gives templates back their column");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
    }

    private sealed record ConvertedTeam(Guid Organization, string Name, string NormalizedName, Guid[] People);

    private static async Task<List<ConvertedTeam>> TeamsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT organization_id, name, normalized_name, member_ids FROM named_teams", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var teams = new List<ConvertedTeam>();
        while (await reader.ReadAsync(ct))
        {
            teams.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<Guid[]>(3)));
        }

        return teams;
    }
}
