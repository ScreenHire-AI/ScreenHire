using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A member's notification settings (<c>/v1/notification-settings</c>): which teammates' changes show as toasts. Everything
/// starts on, a save replaces the lot, and each membership keeps its own, so another member, or the same person in
/// another organization, is untouched. Session only: an API key is no member.
/// </summary>
public sealed class NotificationSettingsTests(AppHostFixture fixture)
{
    private const string Path = "/v1/notification-settings";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_member_has_everything_on()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var settings = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        settings.At("toasts").GetBoolean().Should().BeTrue();
        settings.At("closeToasts").GetBoolean().Should().BeTrue();
        settings.Count("off").Should().Be(0);
    }

    [Fact]
    public async Task A_save_is_returned_by_the_next_read_and_replaces_the_previous_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var saved = (await owner.PutAsync(Path, Settings(toasts: true, closeToasts: false, "Notes", "Moves"))).Expect(HttpStatusCode.OK);
        saved.At("closeToasts").GetBoolean().Should().BeFalse();

        var read = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        read.At("toasts").GetBoolean().Should().BeTrue();
        read.At("closeToasts").GetBoolean().Should().BeFalse();
        read.Body.GetProperty("off").EnumerateArray().Select(k => k.GetString()).Should().Equal("Notes", "Moves");

        (await owner.PutAsync(Path, Settings(toasts: false, closeToasts: true))).Expect(HttpStatusCode.OK);
        var replaced = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        replaced.At("toasts").GetBoolean().Should().BeFalse();
        replaced.At("closeToasts").GetBoolean().Should().BeTrue();
        replaced.Count("off").Should().Be(0, "a save replaces the list of kinds turned off");
    }

    [Fact]
    public async Task All_eight_kinds_can_be_turned_off()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        string[] kinds = ["Moves", "NewCandidates", "Notes", "Tags", "Emails", "Scorecards", "Tasks", "Templates"];

        (await owner.PutAsync(Path, Settings(toasts: true, closeToasts: true, kinds))).Expect(HttpStatusCode.OK);

        (await owner.GetAsync(Path)).Body.GetProperty("off").EnumerateArray().Select(k => k.GetString()).Should().Equal(kinds);
    }

    [Fact]
    public async Task Another_member_of_the_same_organization_keeps_the_defaults()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");

        (await owner.PutAsync(Path, Settings(toasts: false, closeToasts: false, "Moves"))).Expect(HttpStatusCode.OK);

        var theirs = (await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK);
        theirs.At("toasts").GetBoolean().Should().BeTrue();
        theirs.At("closeToasts").GetBoolean().Should().BeTrue();
        theirs.Count("off").Should().Be(0);
    }

    [Fact]
    public async Task The_same_person_in_a_second_organization_keeps_its_own_settings()
    {
        // A's owner also joins B, so one user has two memberships; the settings belong to the membership.
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var email = (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;
        (await b.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = email, ["role"] = "Recruiter" })).Expect(HttpStatusCode.Created);
        await a.Session.AcceptInvitationAsync(await fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct), Ct);
        // Joining made B the active organization.
        (await a.PutAsync(Path, Settings(toasts: false, closeToasts: false, "Tasks"))).Expect(HttpStatusCode.OK);

        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = a.OrganizationId })).Expect(HttpStatusCode.OK);
        var inA = (await a.GetAsync(Path)).Expect(HttpStatusCode.OK);
        inA.At("toasts").GetBoolean().Should().BeTrue();
        inA.At("closeToasts").GetBoolean().Should().BeTrue();
        inA.Count("off").Should().Be(0, "the settings made in B stay in B");

        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = b.OrganizationId })).Expect(HttpStatusCode.OK);
        var inB = (await a.GetAsync(Path)).Expect(HttpStatusCode.OK);
        inB.At("toasts").GetBoolean().Should().BeFalse();
        inB.Body.GetProperty("off").EnumerateArray().Select(k => k.GetString()).Should().Equal("Tasks");
    }

    [Fact]
    public async Task Naming_a_kind_twice_an_unknown_kind_or_no_list_is_refused_and_saves_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        (await owner.PutAsync(Path, Settings(toasts: false, closeToasts: true, "Moves", "Moves"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("off");
        (await owner.PutAsync(Path, Settings(toasts: false, closeToasts: true, "Gossip"))).Expect(HttpStatusCode.BadRequest);
        (await owner.PutAsync(Path, new JsonObject { ["toasts"] = false, ["closeToasts"] = true })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("off");
        (await owner.PutAsync(Path, new JsonObject { ["toasts"] = false, ["closeToasts"] = true, ["off"] = null })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("off");

        (await owner.GetAsync(Path)).At("toasts").GetBoolean().Should().BeTrue("a refused save changed nothing");
    }

    [Fact]
    public async Task A_save_without_the_antiforgery_header_is_rejected()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        using var response = await owner.Session.Client.PutAsJsonAsync(Path, Settings(toasts: false, closeToasts: true), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync(Path)).At("toasts").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Signed_out_and_with_an_API_key_the_settings_are_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));
        using var signedOut = new ApiSession(fixture);

        (await key.GetAsync(Path)).Status.Should().Be(HttpStatusCode.Unauthorized, "a key is no member and has no settings");
        (await key.PutAsync(Path, Settings(toasts: false, closeToasts: true))).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await signedOut.Client.GetAsync(Path, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.GetAsync(Path)).At("toasts").GetBoolean().Should().BeTrue("the refused save changed nothing");
    }

    private static JsonObject Settings(bool toasts, bool closeToasts, params string[] off) => new()
    {
        ["toasts"] = toasts,
        ["closeToasts"] = closeToasts,
        ["off"] = new JsonArray([.. off.Select(k => JsonValue.Create(k))]),
    };
}
