using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Custom roles and permission checks (ADR-0014): a role made by copying another, what it lets its members do, the
/// Owner-only permissions it never holds, the subset rule that keeps anyone from giving more access than they have,
/// another organization's isolation, and the built-in roles keeping exactly the access they had.
/// </summary>
public sealed class CustomRolesTests(AppHostFixture fixture)
{
    [Fact]
    public async Task An_interviewer_sees_candidates_adds_notes_and_fills_in_scorecards_but_cannot_move_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var application = await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        (await owner.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "interview" }))
            .Expect(HttpStatusCode.OK);
        var interviewer = await owner.CreateInterviewerAsync();
        using var robin = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(robin, interviewer);

        (await robin.GetAsync($"/v1/applications/{application.Id()}")).Expect(HttpStatusCode.OK);
        (await robin.PostAsync($"/v1/applications/{application.Id()}/notes", new JsonObject { ["note"] = "Strong on systems design." }))
            .Expect(HttpStatusCode.Created);
        (await robin.PostAsync($"/v1/applications/{application.Id()}/scorecards", new JsonObject
        {
            ["stageId"] = "interview", ["ratings"] = new JsonArray(), ["recommendation"] = "Yes",
        })).Expect(HttpStatusCode.Created);

        var move = (await robin.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "offer" }))
            .Expect(HttpStatusCode.Forbidden);
        move.Text("title").Should().Be("Not in your role");
        move.Text("detail").Should().Contain("Move candidates");
        (await owner.GetAsync($"/v1/applications/{application.Id()}")).Text("stage.id").Should().Be("interview");

        var me = (await robin.GetAsync("/v1/auth/me")).Expect(HttpStatusCode.OK);
        me.Text("organization.role").Should().Be("Custom");
        me.Id("organization.customRoleId").Should().Be(interviewer);
        me.Text("organization.roleName").Should().Be("Interviewer");
        me.At("organization.permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal("ViewHiring", "AddNotesAndTags", "FillInScorecards");
    }

    [Fact]
    public async Task Owner_only_permissions_never_go_into_a_custom_role()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var copy = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Co-owner", ["copyFrom"] = "Owner" }))
            .Expect(HttpStatusCode.Created);
        copy.At("permissions").EnumerateArray().Select(p => p.GetString()).Should().NotContain("ManageOwners").And.Contain("ManageTeam");
        copy.Text("copiedFrom").Should().Be("Owner");

        var refused = (await owner.PutAsync($"/v1/organization/roles/{copy.Id()}", Update("Co-owner", "ViewHiring", "ManageOwners")))
            .Expect(HttpStatusCode.BadRequest);
        refused.ProblemKeys().Should().Equal("permissions");
        (await owner.PutAsync($"/v1/organization/roles/{copy.Id()}", Update("Co-owner", "AddNotesAndTags")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("permissions");

        // A member with the copy still cannot touch an Owner.
        using var member = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(member, copy.Id());
        var ownerMemberId = await member.MemberIdAsync(owner.UserId);
        (await member.PutAsync($"/v1/organization/members/{ownerMemberId}/role", new JsonObject { ["role"] = "Viewer" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Owners only");

        // Deleting anyone's notes goes through the note routes, which need Add notes and tags.
        (await owner.PutAsync($"/v1/organization/roles/{copy.Id()}", Update("Co-owner", "ViewHiring", "DeleteAnyNote")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("permissions");
        (await owner.PutAsync($"/v1/organization/roles/{copy.Id()}", Update("Co-owner", "ViewHiring", "AddNotesAndTags", "DeleteAnyNote")))
            .Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_organization_cannot_see_or_use_the_role()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interviewer = await owner.CreateInterviewerAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var otherViewer = await fixture.InviteAndJoinAsync(other, "Viewer");
        var otherMemberId = await other.MemberIdAsync(otherViewer.UserId);

        (await other.GetAsync("/v1/organization/roles")).Expect(HttpStatusCode.OK).At("roles").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()).Should().Equal("Owner", "Admin", "Recruiter", "Viewer");
        (await other.GetAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.NotFound);
        (await other.GetAsync($"/v1/organization/roles/{interviewer}/history")).Expect(HttpStatusCode.NotFound);
        (await other.PutAsync($"/v1/organization/roles/{interviewer}", Update("Taken", "ViewHiring"))).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Copy", ["copyFromRoleId"] = interviewer.ToString() }))
            .Expect(HttpStatusCode.BadRequest);
        (await other.PutAsync($"/v1/organization/members/{otherMemberId}/role", Custom(interviewer)))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("customRoleId");
        (await other.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = ApiSession.NewEmail("invitee"), ["role"] = "Custom", ["customRoleId"] = interviewer.ToString(),
        })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("customRoleId");

        (await owner.GetAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.OK).Text("name").Should().Be("Interviewer");
    }

    [Fact]
    public async Task Existing_members_keep_exactly_their_access()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        var application = await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));

        // Every membership row is a built-in role with no custom role, as every row from before custom roles is.
        string[] viewing = ["ViewHiring"];
        string[] recruiting =
        [
            .. viewing, "EditJobs", "AddCandidates", "MoveCandidates", "AddNotesAndTags", "CompleteTasks", "EmailCandidates",
            "FillInScorecards", "ReviewIntegrity", "RunAutomations", "EditHiringTemplates",
        ];
        string[] administering = [.. recruiting, "DeleteAnyNote", "EditEmailTemplates", "EditSettings", "ManageApiKeys", "ManageWebhooks", "ManageTeam"];
        string[] owning = [.. administering, "ManageOwners"];
        foreach (var (member, role, permissions) in new[] { (owner, "Owner", owning), (admin, "Admin", administering), (recruiter, "Recruiter", recruiting), (viewer, "Viewer", viewing) })
        {
            var me = (await member.GetAsync("/v1/auth/me")).Expect(HttpStatusCode.OK);
            me.Text("organization.role").Should().Be(role);
            me.At("organization.customRoleId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            me.At("organization.permissions").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo(permissions, role);
        }

        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/notes", new JsonObject { ["note"] = "Recruiter note." }))
            .Expect(HttpStatusCode.Created);
        (await recruiter.PutAsync("/v1/organization", new JsonObject { ["name"] = "Renamed" })).Expect(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"/v1/applications/{application.Id()}/notes", new JsonObject { ["note"] = "Viewer note." }))
            .Expect(HttpStatusCode.Forbidden);
        (await admin.PutAsync("/v1/organization", new JsonObject { ["name"] = "Renamed by the admin" })).Expect(HttpStatusCode.OK);
        (await admin.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = ApiSession.NewEmail("owner"), ["role"] = "Owner" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Owners only");
        (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = ApiSession.NewEmail("owner"), ["role"] = "Owner" }))
            .Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_role_someone_has_cannot_be_deleted()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interviewer = await owner.CreateInterviewerAsync();
        using var member = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(member, interviewer);

        var refused = (await owner.DeleteAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.Conflict);
        refused.Text("title").Should().Be("Role in use");
        refused.Text("detail").Should().Be("1 person has this role. Give them another role first.");
        (await owner.GetAsync("/v1/organization/roles")).At("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == "Interviewer").GetProperty("people").GetInt32().Should().Be(1);

        // An open invitation that offers it blocks the deletion too.
        var memberId = await owner.MemberIdAsync(member.UserId);
        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Viewer" })).Expect(HttpStatusCode.OK);
        var invitation = (await owner.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = ApiSession.NewEmail("invitee"), ["role"] = "Custom", ["customRoleId"] = interviewer.ToString(),
        })).Expect(HttpStatusCode.Created);
        (await owner.DeleteAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.Conflict)
            .Text("detail").Should().Be("1 open invitation offers this role. Revoke it first.");

        (await owner.DeleteAsync($"/v1/organization/invitations/{invitation.Id()}")).Expect(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Role_names_are_unique_in_an_organization_and_never_a_built_in_name()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interviewer = await owner.CreateInterviewerAsync();

        (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = " interviewer ", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");
        (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "viewer", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");
        (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Screener\nBcc: x", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");
        (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Screener" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("copyFrom");
        var screener = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Screener", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created);
        (await owner.PutAsync($"/v1/organization/roles/{screener.Id()}", Update("INTERVIEWER", "ViewHiring")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("name");

        // The same name in another organization is fine.
        using var other = await fixture.SignedInWithOrganizationAsync();
        (await other.CreateInterviewerAsync()).Should().NotBe(interviewer);
    }

    [Fact]
    public async Task Nobody_gives_or_makes_a_role_with_more_access_than_their_own()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var lead = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Team lead", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{lead}", Update("Team lead", "ViewHiring", "AddNotesAndTags", "ManageTeam", "ManageApiKeys")))
            .Expect(HttpStatusCode.OK);
        var interviewer = await owner.CreateInterviewerAsync();
        using var member = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(member, lead);
        using var other = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var otherId = await owner.MemberIdAsync(other.UserId);

        (await member.PutAsync($"/v1/organization/members/{otherId}/role", new JsonObject { ["role"] = "Recruiter" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("More access than your role");
        // Interviewer fills in scorecards, which Team lead cannot.
        (await member.PutAsync($"/v1/organization/members/{otherId}/role", Custom(interviewer))).Expect(HttpStatusCode.Forbidden);
        (await member.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Deputy", ["copyFrom"] = "Admin" }))
            .Expect(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.Forbidden);
        (await member.PutAsync($"/v1/organization/roles/{lead}", Update("Team lead", "ViewHiring", "AddNotesAndTags", "ManageTeam", "ManageApiKeys", "EmailCandidates")))
            .Expect(HttpStatusCode.Forbidden);

        var noteTaker = (await member.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Note taker", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created).Id();
        (await member.PutAsync($"/v1/organization/members/{otherId}/role", Custom(noteTaker))).Expect(HttpStatusCode.OK)
            .Text("roleName").Should().Be("Note taker");

        // An API key never carries more than its creator's role: Write needs every hiring write.
        (await member.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Sync", ["scopes"] = new JsonArray("Read", "Write") }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("More access than your role");
        (await member.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Export", ["scopes"] = new JsonArray("Read") }))
            .Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_invitation_can_offer_a_custom_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interviewer = await owner.CreateInterviewerAsync();
        var email = ApiSession.NewEmail("interviewer");

        var invited = (await owner.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = email, ["role"] = "Custom", ["customRoleId"] = interviewer.ToString(),
        })).Expect(HttpStatusCode.Created);
        invited.Text("roleName").Should().Be("Interviewer");
        (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = ApiSession.NewEmail("x"), ["role"] = "Custom" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("customRoleId");

        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(email, cancellationToken);
        using var session = new ApiSession(fixture);
        var user = await session.AcceptInvitationAsync(token, cancellationToken);

        user.GetProperty("organization").GetProperty("roleName").GetString().Should().Be("Interviewer");
        var members = (await owner.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        var joined = members.Body.EnumerateArray().Single(m => m.GetProperty("email").GetString() == email);
        joined.GetProperty("role").GetString().Should().Be("Custom");
        joined.GetProperty("customRoleId").GetGuid().Should().Be(interviewer);
        joined.GetProperty("roleName").GetString().Should().Be("Interviewer");
    }

    [Fact]
    public async Task Every_change_lands_in_the_membership_log_and_the_role_s_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interviewer = await owner.CreateInterviewerAsync();
        using var member = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(member, interviewer);
        (await owner.PutAsync($"/v1/organization/roles/{interviewer}", Update("Panel interviewer", "ViewHiring", "FillInScorecards")))
            .Expect(HttpStatusCode.OK);

        var log = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        log.Select(e => e.GetProperty("kind").GetString()).Should().StartWith(["RoleUpdated", "RoleChanged", "Joined", "Invited"]);
        log.Select(e => e.GetProperty("kind").GetString()).Should().Contain(["RoleCreated"]);
        log[0].GetProperty("roleName").GetString().Should().Be("Panel interviewer");
        log[0].GetProperty("previousRoleName").GetString().Should().Be("Interviewer");
        log[1].GetProperty("previousRole").GetString().Should().Be("Viewer");
        log[1].GetProperty("role").GetString().Should().Be("Custom");
        log[1].GetProperty("roleName").GetString().Should().Be("Interviewer", "the log keeps the name the role had then");

        var history = (await owner.GetAsync($"/v1/organization/roles/{interviewer}/history")).Expect(HttpStatusCode.OK);
        var lines = history.At("entries").EnumerateArray()
            .Select(e => string.Join(" | ", e.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("text").GetString())))
            .ToList();
        lines.Should().Equal(
            "renamed the role from Interviewer to Panel interviewer | took away Add notes and tags",
            "allowed Add notes and tags | allowed Fill in scorecards",
            "created the role from Viewer");
        var role = (await owner.GetAsync($"/v1/organization/roles/{interviewer}")).Expect(HttpStatusCode.OK);
        role.Text("lastUpdated.by").Should().Be("Member");
        role.Id("lastUpdated.userId").Should().Be(owner.UserId);
        role.At("people").GetInt32().Should().Be(1);
    }

    private static JsonObject Update(string name, params string[] permissions) =>
        new() { ["name"] = name, ["permissions"] = new JsonArray([.. permissions.Select(p => (JsonNode)p)]) };

    private static JsonObject Custom(Guid roleId) => new() { ["role"] = "Custom", ["customRoleId"] = roleId.ToString() };
}
