using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Team members and invitations: an emailed invitation signs the person in and joins them with its role, once; Owners
/// and Admins change roles and remove members; nobody removes or demotes the last Owner; another organization's ids
/// answer 404; a member of two organizations switches between them; and every change lands in the membership log.
/// </summary>
public sealed class TeamTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invited_person_joins_with_the_role_they_were_invited_with()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var email = ApiSession.NewEmail("invitee");

        var invited = (await owner.PostAsync("/v1/organization/invitations", Invite(email, "Recruiter"))).Expect(HttpStatusCode.Created);
        invited.Text("email").Should().Be(email);
        invited.Text("role").Should().Be("Recruiter");
        (await owner.GetAsync("/v1/organization/invitations")).Expect(HttpStatusCode.OK).Count().Should().Be(1);

        var mail = (await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct))[0];
        mail.Subject.Should().StartWith("Join Org test-");
        mail.Text.Should().Contain("/invitations/accept#invitation=").And.Contain("as a recruiter");
        mail.Text.Should().NotContain("#token=", "an invitation link must never pass for a sign-in link");

        using var invitee = new ApiSession(fixture);
        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        var user = await invitee.AcceptInvitationAsync(token, Ct);

        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(owner.OrganizationId);
        user.GetProperty("organization").GetProperty("role").GetString().Should().Be("Recruiter");
        (await invitee.CurrentUserAsync(Ct))!.Value.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(owner.OrganizationId);

        var members = (await owner.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        members.Body.EnumerateArray().Select(m => (m.GetProperty("email").GetString(), m.GetProperty("role").GetString()))
            .Should().BeEquivalentTo([(OwnerEmail(members), "Owner"), (email, "Recruiter")]);
        (await owner.GetAsync("/v1/organization/invitations")).Count().Should().Be(0, "an accepted invitation is no longer pending");

        var log = await LogKindsAsync(owner);
        log.Should().Equal("Joined", "Invited");
    }

    [Fact]
    public async Task An_invitation_works_only_once()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var email = ApiSession.NewEmail("once");
        (await owner.PostAsync("/v1/organization/invitations", Invite(email, "Viewer"))).Expect(HttpStatusCode.Created);
        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        using var first = new ApiSession(fixture);
        await first.AcceptInvitationAsync(token, Ct);

        using var second = new ApiSession(fixture);
        using var reused = await second.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);

        await ExpectRefusedAsync(reused);
        (await second.CurrentUserAsync(Ct)).Should().BeNull("a refused invitation signs nobody in");
    }

    [Fact]
    public async Task An_expired_invitation_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var email = ApiSession.NewEmail("late");
        var invitationId = (await owner.PostAsync("/v1/organization/invitations", Invite(email, "Recruiter")))
            .Expect(HttpStatusCode.Created).Id();
        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            await db.OrganizationInvitations.Where(i => i.Id == invitationId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        }

        using var invitee = new ApiSession(fixture);
        using var response = await invitee.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);

        await ExpectRefusedAsync(response);
        (await owner.GetAsync("/v1/organization/invitations")).Count().Should().Be(0, "an expired invitation is not pending");
    }

    [Fact]
    public async Task A_revoked_or_replaced_invitation_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var revokedEmail = ApiSession.NewEmail("revoked");
        var revokedId = (await owner.PostAsync("/v1/organization/invitations", Invite(revokedEmail, "Viewer"))).Expect(HttpStatusCode.Created).Id();
        var revokedToken = await fixture.Mailpit.WaitForInvitationTokenAsync(revokedEmail, Ct);
        (await owner.DeleteAsync($"/v1/organization/invitations/{revokedId}")).Expect(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/v1/organization/invitations/{revokedId}")).Expect(HttpStatusCode.NotFound);

        var replacedEmail = ApiSession.NewEmail("replaced");
        (await owner.PostAsync("/v1/organization/invitations", Invite(replacedEmail, "Viewer"))).Expect(HttpStatusCode.Created);
        var replacedToken = await fixture.Mailpit.WaitForInvitationTokenAsync(replacedEmail, Ct);
        (await owner.PostAsync("/v1/organization/invitations", Invite(replacedEmail, "Recruiter"))).Expect(HttpStatusCode.Created);

        using var invitee = new ApiSession(fixture);
        await ExpectRefusedAsync(await invitee.PostAsync("/v1/auth/invitations/accept", new { token = revokedToken }, Ct));
        await ExpectRefusedAsync(await invitee.PostAsync("/v1/auth/invitations/accept", new { token = replacedToken }, Ct));
        await ExpectRefusedAsync(await invitee.PostAsync("/v1/auth/invitations/accept", new { token = "not-an-invitation" }, Ct));
        var pending = (await owner.GetAsync("/v1/organization/invitations")).Expect(HttpStatusCode.OK);
        pending.Count().Should().Be(1);
        pending.Text("0.role").Should().Be("Recruiter");
        // A replacement revokes and invites in one save, at one instant, so only the multiset is stable.
        (await LogKindsAsync(owner)).Should().BeEquivalentTo(["Invited", "InvitationRevoked", "Invited", "InvitationRevoked", "Invited"]);
    }

    [Fact]
    public async Task A_role_change_takes_effect_on_the_members_next_request()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        (await recruiter.PostAsync("/v1/jobs", Samples.RemoteJob("Allowed"))).Expect(HttpStatusCode.Created);
        var memberId = await owner.MemberIdAsync(recruiter.UserId);

        var changed = (await owner.PutAsync($"/v1/organization/members/{memberId}/role", Role("Viewer"))).Expect(HttpStatusCode.OK);

        changed.Text("role").Should().Be("Viewer");
        (await recruiter.PostAsync("/v1/jobs", Samples.RemoteJob("Refused"))).Expect(HttpStatusCode.Forbidden);
        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject())).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("role");
        (await LogKindsAsync(owner)).First().Should().Be("RoleChanged");
        var entry = (await owner.GetAsync("/v1/organization/membership-log")).At("0");
        entry.GetProperty("previousRole").GetString().Should().Be("Recruiter");
        entry.GetProperty("role").GetString().Should().Be("Viewer");
        entry.GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);
        entry.GetProperty("memberUserId").GetGuid().Should().Be(recruiter.UserId);
    }

    [Fact]
    public async Task A_removed_member_loses_the_organization_on_their_next_request()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await owner.CreateJobAsync(Samples.RemoteJob("Private"));
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        (await recruiter.GetAsync("/v1/jobs")).Count().Should().Be(1);
        var memberId = await owner.MemberIdAsync(recruiter.UserId);

        (await owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        var me = await recruiter.Session.CurrentUserAsync(Ct);
        me!.Value.GetProperty("organization").ValueKind.Should().Be(JsonValueKind.Null);
        (await recruiter.GetAsync("/v1/jobs")).Count().Should().Be(0);
        (await owner.GetAsync("/v1/organization/members")).Count().Should().Be(1);
        (await owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NotFound);
        (await LogKindsAsync(owner)).First().Should().Be("Removed");
    }

    [Fact]
    public async Task Nobody_can_remove_or_demote_the_last_owner()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var ownMemberId = await owner.MemberIdAsync(owner.UserId);

        var demoted = (await owner.PutAsync($"/v1/organization/members/{ownMemberId}/role", Role("Admin"))).Expect(HttpStatusCode.Conflict);
        demoted.Text("title").Should().Be("Last owner");
        (await owner.DeleteAsync($"/v1/organization/members/{ownMemberId}")).Expect(HttpStatusCode.Conflict);
        (await owner.GetAsync("/v1/organization/members")).Text("0.role").Should().Be("Owner");

        // With a second Owner, the first may step down; then the second is the last one.
        using var second = await fixture.InviteAndJoinAsync(owner, "Owner");
        (await owner.PutAsync($"/v1/organization/members/{ownMemberId}/role", Role("Admin"))).Expect(HttpStatusCode.OK);
        var secondMemberId = await second.MemberIdAsync(second.UserId);
        (await second.PutAsync($"/v1/organization/members/{secondMemberId}/role", Role("Viewer"))).Expect(HttpStatusCode.Conflict);
        (await second.DeleteAsync($"/v1/organization/members/{secondMemberId}")).Expect(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_admin_manages_the_team_but_not_its_owners()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var ownerMemberId = await admin.MemberIdAsync(owner.UserId);

        (await admin.PostAsync("/v1/organization/invitations", Invite(ApiSession.NewEmail("owner"), "Owner"))).Expect(HttpStatusCode.Forbidden)
            .Text("title").Should().Be("Owners only");
        (await admin.PutAsync($"/v1/organization/members/{ownerMemberId}/role", Role("Viewer"))).Expect(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/v1/organization/members/{ownerMemberId}")).Expect(HttpStatusCode.Forbidden);

        using var viewer = await fixture.InviteAndJoinAsync(admin, "Viewer");
        var viewerMemberId = await admin.MemberIdAsync(viewer.UserId);
        (await admin.PutAsync($"/v1/organization/members/{viewerMemberId}/role", Role("Owner"))).Expect(HttpStatusCode.Forbidden);
        (await admin.PutAsync($"/v1/organization/members/{viewerMemberId}/role", Role("Recruiter"))).Expect(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/v1/organization/members/{viewerMemberId}")).Expect(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("Recruiter")]
    [InlineData("Viewer")]
    public async Task Recruiters_and_viewers_cannot_manage_the_team(string role)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var pendingId = (await owner.PostAsync("/v1/organization/invitations", Invite(ApiSession.NewEmail("pending"), "Viewer")))
            .Expect(HttpStatusCode.Created).Id();
        using var member = await fixture.InviteAndJoinAsync(owner, role);
        var ownerMemberId = await member.MemberIdAsync(owner.UserId);

        ApiResult[] attempts =
        [
            await member.GetAsync("/v1/organization/invitations"),
            await member.GetAsync("/v1/organization/membership-log"),
            await member.PostAsync("/v1/organization/invitations", Invite(ApiSession.NewEmail("friend"), "Viewer")),
            await member.PutAsync($"/v1/organization/members/{ownerMemberId}/role", Role("Viewer")),
            await member.DeleteAsync($"/v1/organization/members/{ownerMemberId}"),
            await member.DeleteAsync($"/v1/organization/invitations/{pendingId}"),
        ];

        attempts.Select(a => a.Status).Should().AllBeEquivalentTo(HttpStatusCode.Forbidden);
        attempts.Select(a => a.Text("title")).Should().AllBe("Owners and admins only");
        (await member.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK).Count().Should().Be(2, "every member sees the team");
    }

    [Fact]
    public async Task Another_organizations_members_and_invitations_answer_not_found()
    {
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var bOwnerMemberId = await b.MemberIdAsync(b.UserId);
        var bInvitationId = (await b.PostAsync("/v1/organization/invitations", Invite(ApiSession.NewEmail("b-invitee"), "Viewer")))
            .Expect(HttpStatusCode.Created).Id();

        (await a.PutAsync($"/v1/organization/members/{bOwnerMemberId}/role", Role("Viewer"))).Expect(HttpStatusCode.NotFound);
        (await a.DeleteAsync($"/v1/organization/members/{bOwnerMemberId}")).Expect(HttpStatusCode.NotFound);
        (await a.DeleteAsync($"/v1/organization/invitations/{bInvitationId}")).Expect(HttpStatusCode.NotFound);
        (await a.GetAsync("/v1/organization/members")).Count().Should().Be(1);
        (await a.GetAsync("/v1/organization/invitations")).Count().Should().Be(0);
        (await a.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("invitationId").ValueKind == JsonValueKind.String && e.GetProperty("invitationId").GetGuid() == bInvitationId);
        (await b.GetAsync("/v1/organization/members")).Text("0.role").Should().Be("Owner");
    }

    [Fact]
    public async Task A_member_of_two_organizations_cannot_reach_their_other_membership_from_this_one()
    {
        // A's Owner also joins B as a Viewer, then works in A again. Their B membership is visible to them for sign-in,
        // but the team routes in A must not show it or act on it.
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var aEmail = (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;
        (await b.PostAsync("/v1/organization/invitations", Invite(aEmail, "Viewer"))).Expect(HttpStatusCode.Created);
        await a.Session.AcceptInvitationAsync(await fixture.Mailpit.WaitForInvitationTokenAsync(aEmail, Ct), Ct);
        var aInB = await b.MemberIdAsync(a.UserId);
        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = a.OrganizationId })).Expect(HttpStatusCode.OK);

        (await a.PutAsync($"/v1/organization/members/{aInB}/role", Role("Owner"))).Expect(HttpStatusCode.NotFound);
        (await a.DeleteAsync($"/v1/organization/members/{aInB}")).Expect(HttpStatusCode.NotFound);
        var members = (await a.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        members.Body.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().NotContain(aInB);
        members.Count().Should().Be(1);
        (await b.GetAsync("/v1/organization/members")).Body.EnumerateArray()
            .Single(m => m.GetProperty("userId").GetGuid() == a.UserId).GetProperty("role").GetString().Should().Be("Viewer");
    }

    [Fact]
    public async Task A_member_of_two_organizations_switches_between_them()
    {
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var aEmail = (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;
        (await b.PostAsync("/v1/organization/invitations", Invite(aEmail, "Recruiter"))).Expect(HttpStatusCode.Created);

        var joined = await a.Session.AcceptInvitationAsync(await fixture.Mailpit.WaitForInvitationTokenAsync(aEmail, Ct), Ct);

        joined.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(b.OrganizationId, "joining makes the new organization the active one");
        var mine = (await a.GetAsync("/v1/auth/organizations")).Expect(HttpStatusCode.OK);
        mine.Body.EnumerateArray().Select(o => (o.GetProperty("id").GetGuid(), o.GetProperty("role").GetString()))
            .Should().BeEquivalentTo([(a.OrganizationId, "Owner"), (b.OrganizationId, "Recruiter")]);

        var switched = (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = a.OrganizationId }))
            .Expect(HttpStatusCode.OK);
        switched.Id("organization.id").Should().Be(a.OrganizationId);
        (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("organization").GetProperty("role").GetString().Should().Be("Owner");

        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = Guid.NewGuid() })).Expect(HttpStatusCode.NotFound);
        using var stranger = await fixture.SignedInWithOrganizationAsync();
        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = stranger.OrganizationId }))
            .Expect(HttpStatusCode.NotFound);
        (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(a.OrganizationId);
    }

    [Fact]
    public async Task Inviting_a_member_is_a_conflict_and_bad_input_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var ownEmail = (await owner.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;

        (await owner.PostAsync("/v1/organization/invitations", Invite(ownEmail.ToUpperInvariant(), "Viewer"))).Expect(HttpStatusCode.Conflict)
            .Text("title").Should().Be("Already a member");
        (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = ApiSession.NewEmail() }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("role");
        (await owner.PostAsync("/v1/organization/invitations", Invite("Someone <someone@example.com>", "Viewer")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("email");
    }

    [Fact]
    public async Task The_team_routes_need_a_signed_in_user()
    {
        using var session = new ApiSession(fixture);

        using var members = await session.Client.GetAsync("/v1/organization/members", Ct);
        using var mine = await session.Client.GetAsync("/v1/auth/organizations", Ct);

        members.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mine.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_seeded_recruiter_signs_in_to_Brightwater_with_Dev_SSO()
    {
        using var session = new ApiSession(fixture);
        await session.SignInWithDevSsoAsync("brightwater-recruiter", Ct);

        var organization = (await session.CurrentUserAsync(Ct))!.Value.GetProperty("organization");
        organization.GetProperty("slug").GetString().Should().Be("brightwater");
        organization.GetProperty("role").GetString().Should().Be("Recruiter");
        var members = await session.Client.GetFromJsonAsync<JsonElement>("/v1/organization/members", Ct);
        members.EnumerateArray().Select(m => (m.GetProperty("displayName").GetString(), m.GetProperty("role").GetString()))
            .Should().Contain([("Avery Quinn", "Owner"), ("Jordan Ellis", "Recruiter")]);
    }

    private static JsonObject Invite(string email, string role) => new() { ["email"] = email, ["role"] = role };

    private static JsonObject Role(string role) => new() { ["role"] = role };

    private static string OwnerEmail(ApiResult members) =>
        members.Body.EnumerateArray().Single(m => m.GetProperty("role").GetString() == "Owner").GetProperty("email").GetString()!;

    // Newest first.
    private static async Task<List<string>> LogKindsAsync(Tenant admin) =>
        [.. (await admin.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Select(e => e.GetProperty("kind").GetString()!)];

    private static async Task ExpectRefusedAsync(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Equal("token");
        }
    }
}
