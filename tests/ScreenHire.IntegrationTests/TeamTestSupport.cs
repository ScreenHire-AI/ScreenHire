using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

internal static class TeamTestSupport
{
    /// <summary>
    /// Has <paramref name="inviter"/> invite a new address with <paramref name="role"/>, reads the invitation from Mailpit
    /// and accepts it in a fresh session, as the invited person would. Dispose the result.
    /// </summary>
    public static async Task<Tenant> InviteAndJoinAsync(this AppHostFixture fixture, Tenant inviter, string role, string? email = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        email ??= ApiSession.NewEmail(role.ToLowerInvariant());
        (await inviter.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = email, ["role"] = role }))
            .Expect(HttpStatusCode.Created);
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

    /// <summary>The membership id of <paramref name="userId"/> in the list <paramref name="viewer"/> sees.</summary>
    public static async Task<Guid> MemberIdAsync(this Tenant viewer, Guid userId)
    {
        var members = (await viewer.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK);
        return members.Body.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId).GetProperty("id").GetGuid();
    }
}
