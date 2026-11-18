using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using ScreenHire.Infrastructure.Persistence.Migrations;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Sharing a view is an offer (docs/architecture/saved-views.md): a shared view is listed for everyone who may see it, and
/// sits in a teammate's sidebar only once they add it. Its owner always has it. Adding and removing is per person, a view
/// of a job follows job access, making a view private takes it out of every sidebar, and a migration kept today's shared
/// views in the sidebars of the members who had them.
/// </summary>
public sealed class ViewSharingTests(AppHostFixture fixture)
{
    private const string Path = "/v1/views";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Candidates(string name, bool shared = false) => new()
    {
        ["name"] = name,
        ["scope"] = "Candidates",
        ["jobId"] = null,
        ["shared"] = shared,
        ["filters"] = new JsonObject
        {
            ["baseView"] = null,
            ["jobIds"] = new JsonArray(),
            ["stages"] = new JsonArray(),
            ["authenticity"] = null,
            ["aiAssistance"] = null,
            ["tag"] = null,
            ["sortColumn"] = null,
            ["sortDescending"] = false,
        },
    };

    private static JsonElement View(ApiResult list, string name) =>
        list.Body.GetProperty("views").EnumerateArray().Single(v => v.GetProperty("name").GetString() == name);

    private static bool InSidebar(ApiResult list, string name) => View(list, name).GetProperty("inSidebar").GetBoolean();

    private static Guid[] SidebarOrder(ApiResult list) => [.. list.Body.GetProperty("sidebarOrder").EnumerateArray().Select(id => id.GetGuid())];

    private static async Task<ApiResult> ListAsync(Tenant who) => (await who.GetAsync(Path)).Expect(HttpStatusCode.OK);

    [Fact]
    public async Task Sharing_a_view_offers_it_to_the_team_and_adds_it_to_nobody_elses_sidebar()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var created = (await owner.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);
        created.At("inSidebar").GetBoolean().Should().BeTrue("a view is always in its owner's sidebar");
        created.At("sharedAt").ValueKind.Should().Be(JsonValueKind.String);

        var theirs = await ListAsync(recruiter);
        View(theirs, "Team queue").GetProperty("shared").GetBoolean().Should().BeTrue("it is on offer to browse and preview");
        InSidebar(theirs, "Team queue").Should().BeFalse("sharing is an offer, not a delivery");
        View(theirs, "Team queue").GetProperty("ownerName").GetString().Should().NotBeNullOrWhiteSpace();
        SidebarOrder(theirs).Should().BeEmpty();

        var ours = await ListAsync(owner);
        InSidebar(ours, "Team queue").Should().BeTrue();
        SidebarOrder(ours).Should().BeEmpty("the order lists only the shared views a person added");
    }

    [Fact]
    public async Task Each_person_adds_and_removes_a_shared_view_for_themselves_and_both_are_safe_to_repeat()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var first = (await owner.PostAsync(Path, Candidates("First", shared: true))).Expect(HttpStatusCode.Created);
        var second = (await owner.PostAsync(Path, Candidates("Second", shared: true))).Expect(HttpStatusCode.Created);

        (await recruiter.PutAsync($"{Path}/{second.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await recruiter.PutAsync($"{Path}/{first.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await recruiter.PutAsync($"{Path}/{first.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);

        var theirs = await ListAsync(recruiter);
        InSidebar(theirs, "First").Should().BeTrue();
        InSidebar(theirs, "Second").Should().BeTrue();
        SidebarOrder(theirs).Should().Equal([second.Id(), first.Id()], "views sit in the order they were added");
        InSidebar(await ListAsync(admin), "First").Should().BeFalse("another member's sidebar is theirs");

        (await recruiter.DeleteAsync($"{Path}/{second.Id()}/sidebar")).Expect(HttpStatusCode.NoContent);
        (await recruiter.DeleteAsync($"{Path}/{second.Id()}/sidebar")).Expect(HttpStatusCode.NoContent);
        var after = await ListAsync(recruiter);
        InSidebar(after, "Second").Should().BeFalse();
        View(after, "Second").GetProperty("shared").GetBoolean().Should().BeTrue("removing it from a sidebar leaves it shared");
        SidebarOrder(after).Should().Equal(first.Id());

        // Their own view needs no row: it is in their sidebar already.
        var own = (await recruiter.PostAsync(Path, Candidates("Mine", shared: true))).Expect(HttpStatusCode.Created);
        (await recruiter.PutAsync($"{Path}/{own.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await recruiter.DeleteAsync($"{Path}/{own.Id()}/sidebar")).Expect(HttpStatusCode.NoContent);
        var mine = await ListAsync(recruiter);
        InSidebar(mine, "Mine").Should().BeTrue("the owner always has the view");
        SidebarOrder(mine).Should().Equal(first.Id());
    }

    [Fact]
    public async Task A_private_view_cannot_be_added_and_making_a_view_private_takes_it_out_of_every_sidebar()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var view = (await owner.PostAsync(Path, Candidates("Quiet", shared: false))).Expect(HttpStatusCode.Created);
        (await recruiter.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NotFound);

        (await owner.PatchAsync($"{Path}/{view.Id()}", new JsonObject { ["name"] = null, ["shared"] = true })).Expect(HttpStatusCode.OK);
        (await recruiter.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        InSidebar(await ListAsync(recruiter), "Quiet").Should().BeTrue();

        (await owner.PatchAsync($"{Path}/{view.Id()}", new JsonObject { ["name"] = null, ["shared"] = false })).Expect(HttpStatusCode.OK);
        (await ListAsync(recruiter)).Body.GetProperty("views").GetArrayLength().Should().Be(0);

        // Sharing it again is a new offer: nobody has it in their sidebar until they add it.
        (await owner.PatchAsync($"{Path}/{view.Id()}", new JsonObject { ["name"] = null, ["shared"] = true })).Expect(HttpStatusCode.OK);
        InSidebar(await ListAsync(recruiter), "Quiet").Should().BeFalse();
    }

    [Fact]
    public async Task A_deleted_view_leaves_every_sidebar_and_Undo_puts_it_back_where_it_was()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var view = (await owner.PostAsync(Path, Candidates("Overdue", shared: true))).Expect(HttpStatusCode.Created);
        (await recruiter.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);

        (await owner.DeleteAsync($"{Path}/{view.Id()}")).Expect(HttpStatusCode.NoContent);
        (await ListAsync(recruiter)).Body.GetProperty("views").GetArrayLength().Should().Be(0);
        (await recruiter.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NotFound);

        (await owner.PostAsync($"{Path}/{view.Id()}/restore")).Expect(HttpStatusCode.OK);
        InSidebar(await ListAsync(recruiter), "Overdue").Should().BeTrue("Undo restores the same row, sidebars included");
    }

    [Fact]
    public async Task Browsing_follows_job_access_so_a_view_of_a_job_a_member_cannot_see_is_neither_listed_nor_added()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        using var restricted = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var colleague = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var shared = (await owner.PostAsync(Path, new JsonObject
        {
            ["name"] = "Screening",
            ["scope"] = "Job",
            ["jobId"] = job.Id().ToString(),
            ["shared"] = true,
            ["filters"] = Candidates("x")["filters"]!.DeepClone(),
        })).Expect(HttpStatusCode.Created);

        (await colleague.PutAsync($"{Path}/{shared.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await ListAsync(restricted)).Body.GetProperty("views").GetArrayLength().Should().Be(0);
        (await restricted.PutAsync($"{Path}/{shared.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NotFound);

        await owner.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(restricted.UserId));
        InSidebar(await ListAsync(restricted), "Screening").Should().BeFalse("it is on offer now, and added by nobody yet");
        (await restricted.PutAsync($"{Path}/{shared.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        InSidebar(await ListAsync(restricted), "Screening").Should().BeTrue();
    }

    [Fact]
    public async Task A_removed_member_takes_their_added_views_out_and_does_not_find_them_on_rejoining()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var view = (await owner.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);
        (await recruiter.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await HasRowAsync(owner.OrganizationId, recruiter.UserId, view.Id())).Should().BeTrue();

        var memberId = await owner.MemberIdAsync(recruiter.UserId);
        (await owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        (await HasRowAsync(owner.OrganizationId, recruiter.UserId, view.Id())).Should().BeFalse();
        View(await ListAsync(owner), "Team queue").GetProperty("inSidebar").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Another_organization_sees_and_adds_nothing_of_ours()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        var view = (await owner.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);

        (await ListAsync(outsider)).Body.GetProperty("views").GetArrayLength().Should().Be(0);
        (await outsider.PutAsync($"{Path}/{view.Id()}/sidebar", new JsonObject())).Expect(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"{Path}/{view.Id()}/sidebar")).Expect(HttpStatusCode.NoContent);
        (await HasRowAsync(owner.OrganizationId, outsider.UserId, view.Id())).Should().BeFalse();
    }

    [Fact]
    public async Task Opening_the_sheet_of_team_views_moves_the_date_a_view_is_new_after_and_keeps_the_hidden_built_ins()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        (await recruiter.PutAsync($"{Path}/hidden-built-ins", new JsonObject { ["hiddenBuiltIns"] = new JsonArray("overdue") })).Expect(HttpStatusCode.OK);

        var before = (await ListAsync(recruiter)).Body.GetProperty("teamViewsSeenAt").GetDateTimeOffset();
        (await owner.PostAsync(Path, Candidates("Team queue", shared: true))).Expect(HttpStatusCode.Created);
        View(await ListAsync(recruiter), "Team queue").GetProperty("sharedAt").GetDateTimeOffset()
            .Should().BeAfter(before, "a view shared after the member joined is New until they open the sheet");

        (await recruiter.PutAsync($"{Path}/team-views-opened", new JsonObject())).Expect(HttpStatusCode.NoContent);
        var after = await ListAsync(recruiter);
        after.Body.GetProperty("teamViewsSeenAt").GetDateTimeOffset().Should().BeAfter(View(after, "Team queue").GetProperty("sharedAt").GetDateTimeOffset());
        after.Body.GetProperty("hiddenBuiltIns").EnumerateArray().Select(id => id.GetString()).Should().Equal("overdue");

        // Hiding another built-in view afterwards keeps the date.
        (await recruiter.PutAsync($"{Path}/hidden-built-ins", new JsonObject { ["hiddenBuiltIns"] = new JsonArray("overdue", "new-today") })).Expect(HttpStatusCode.OK);
        (await ListAsync(recruiter)).Body.GetProperty("teamViewsSeenAt").GetDateTimeOffset().Should().Be(after.Body.GetProperty("teamViewsSeenAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_migration_keeps_each_shared_view_in_the_sidebar_of_the_members_who_had_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var older = (await owner.PostAsync(Path, Candidates("Older", shared: true))).Expect(HttpStatusCode.Created);
        var newer = (await admin.PostAsync(Path, Candidates("Newer", shared: true))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync(Path, Candidates("Private", shared: false))).Expect(HttpStatusCode.Created);
        InSidebar(await ListAsync(recruiter), "Older").Should().BeFalse("new shares start in nobody's sidebar");

        // The statement the migration runs over every organization, run for this one (the tenant is set, as the migration
        // lifts the force instead). Shared views today are the ones that were in sidebars before the migration.
        await RunBackfillAsync(owner.OrganizationId);

        var theirs = await ListAsync(recruiter);
        InSidebar(theirs, "Older").Should().BeTrue();
        InSidebar(theirs, "Newer").Should().BeTrue();
        SidebarOrder(theirs).Should().Equal([older.Id(), newer.Id()], "in the order the views were saved");
        InSidebar(await ListAsync(admin), "Older").Should().BeTrue();
        SidebarOrder(await ListAsync(owner)).Should().Equal([newer.Id()], "an owner has their own views without a row, and a teammate's like anyone");
        (await ListAsync(recruiter)).Body.GetProperty("views").EnumerateArray().Select(v => v.GetProperty("name").GetString())
            .Should().BeEquivalentTo(["Older", "Newer"], "a private view is not offered");
    }

    private async Task<bool> HasRowAsync(Guid organizationId, Guid userId, Guid viewId)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, organizationId);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM saved_view_members WHERE view_id = @view AND user_id = @user", connection, transaction);
        command.Parameters.AddWithValue("view", viewId);
        command.Parameters.AddWithValue("user", userId);
        return (long)(await command.ExecuteScalarAsync(Ct))! > 0;
    }

    private async Task RunBackfillAsync(Guid organizationId)
    {
        var sql = (string)typeof(AddSavedViewMembers)
            .GetField("KeepSharedViewsInSidebars", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        var start = sql.IndexOf("INSERT INTO saved_view_members", StringComparison.Ordinal);
        var insert = sql[start..(sql.IndexOf(';', start) + 1)];
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await SetTenantAsync(connection, transaction, organizationId);
        await using var command = new NpgsqlCommand(insert, connection, transaction);
        await command.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid organizationId)
    {
        await using var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true), set_config('app.job_scope', 'all', true)", connection, transaction);
        setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
        await setTenant.ExecuteNonQueryAsync(Ct);
    }
}
