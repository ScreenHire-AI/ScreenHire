using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

internal static class TeamTestSupport
{
    /// <summary>
    /// Has <paramref name="inviter"/> invite a new address with <paramref name="role"/> (and <paramref name="jobAccess"/>,
    /// All when null), reads the invitation from Mailpit and accepts it in a fresh session, as the invited person would.
    /// Dispose the result.
    /// </summary>
    public static async Task<Tenant> InviteAndJoinAsync(
        this AppHostFixture fixture, Tenant inviter, string role, string? email = null, string? jobAccess = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        email ??= ApiSession.NewEmail(role.ToLowerInvariant());
        var body = new JsonObject { ["email"] = email, ["role"] = role };
        if (jobAccess is not null)
        {
            body["jobAccess"] = jobAccess;
        }

        (await inviter.PostAsync("/v1/organization/invitations", body)).Expect(HttpStatusCode.Created);
        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(email, cancellationToken);

        var session = new ApiSession(fixture);
        try
        {
            var user = await session.AcceptInvitationAsync(token, cancellationToken);
            user.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(inviter.OrganizationId);
            return new Tenant(session, inviter.OrganizationId, user.GetProperty("id").GetGuid());
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Accepts an invitation and expects 200 with the signed-in user.</summary>
    public static async Task<JsonElement> AcceptInvitationAsync(this ApiSession session, string token, CancellationToken cancellationToken)
    {
        using var response = await session.PostAsync("/v1/auth/invitations/accept", new { token }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>
    /// Makes the "Interviewer" custom role as <paramref name="owner"/>: copied from Viewer, then given Add notes and tags
    /// and Fill in scorecards. Returns its id.
    /// </summary>
    public static async Task<Guid> CreateInterviewerAsync(this Tenant owner)
    {
        var id = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Interviewer", ["copyFrom"] = "Viewer" }))
            .Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{id}", new JsonObject
        {
            ["name"] = "Interviewer",
            ["permissions"] = new JsonArray("ViewHiring", "AddNotesAndTags", "FillInScorecards"),
        })).Expect(HttpStatusCode.OK);
        return id;
    }

    /// <summary>Has <paramref name="admin"/> give <paramref name="member"/> the custom role <paramref name="roleId"/>.</summary>
    public static async Task GiveRoleAsync(this Tenant admin, Tenant member, Guid roleId)
    {
        var memberId = await admin.MemberIdAsync(member.UserId);
        (await admin.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Custom", ["customRoleId"] = roleId.ToString() }))
            .Expect(HttpStatusCode.OK);
    }

    /// <summary>The membership id of <paramref name="userId"/> in the list <paramref name="viewer"/> sees.</summary>
    public static async Task<Guid> MemberIdAsync(this Tenant viewer, Guid userId)
    {
        var members = (await viewer.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        return members.Body.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId).GetProperty("id").GetGuid();
    }

    /// <summary>Has <paramref name="editor"/> set <paramref name="jobId"/>'s hiring team to these members, and expects 200.</summary>
    public static async Task<ApiResult> SetHiringTeamAsync(this Tenant editor, Guid jobId, params Guid[] memberIds) =>
        (await editor.PutAsync($"/v1/jobs/{jobId}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray([.. memberIds.Select(id => JsonValue.Create(id))]),
        })).Expect(HttpStatusCode.OK);
}
