using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Account;

/// <summary>
/// A member's notification choices (<c>/v1/account/notifications</c>, docs/architecture/notifications.md, "Each person's
/// choices"): every row starts where the brief put it, verification and AI use start from the organization's alert settings
/// and follow a later change to them, a person's own choice wins, the toasts the Teammate changes sheet kept are the In the app
/// column, and each membership keeps its own. Session only: an API key is no person.
/// </summary>
public sealed class AccountNotificationsTests(AppHostFixture fixture)
{
    private const string Path = "/v1/account/notifications";
    private const string Alerts = "/v1/organization/verification-settings";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_member_starts_from_the_defaults_and_the_organizations_alert_settings()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var grid = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        grid.Text("scope").Should().Be("MyJobs");
        grid.At("pauseToasts").GetBoolean().Should().BeFalse();
        grid.At("closeToasts").GetBoolean().Should().BeTrue();
        Emails(grid).Should().Equal(new Dictionary<string, string>
        {
            ["Mentions"] = "Instantly",
            ["Replies"] = "Instantly",
            ["Interviews"] = "Instantly",
            ["Tasks"] = "Digest",
            ["NewApplications"] = "Digest",
            ["TeammateChanges"] = "Off",
            // The organization sends no verification alert by email yet, so its people start at Off.
            ["VerificationSuggested"] = "Off",
            // The AI use row follows the organization's AI usage email, which a new organization has off. The trial's reminders
            // still reach an Owner who never chose (NoticeChoicesTests).
            ["AiUseAndBilling"] = "Off",
        });

        // The organization turns the verification email on: everyone who never chose follows it.
        (await owner.PutAsync(Alerts, new JsonObject { ["alerts"] = new JsonObject { ["suggestedEmail"] = true } })).Expect(HttpStatusCode.OK);
        Emails((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK))["VerificationSuggested"].Should().Be("Instantly");
    }

    [Fact]
    public async Task A_persons_own_choice_wins_over_the_organizations_and_survives_a_change_to_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync(Alerts, new JsonObject { ["alerts"] = new JsonObject { ["suggestedEmail"] = true } })).Expect(HttpStatusCode.OK);
        var grid = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        var saved = (await owner.PutAsync(Path, Save(grid, rows: new() { ["VerificationSuggested"] = "Off", ["Tasks"] = "Instantly" }))).Expect(HttpStatusCode.OK);
        Emails(saved)["VerificationSuggested"].Should().Be("Off");

        (await owner.PutAsync(Alerts, new JsonObject { ["alerts"] = new JsonObject { ["suggestedEmail"] = false } })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync(Alerts, new JsonObject { ["alerts"] = new JsonObject { ["suggestedEmail"] = true } })).Expect(HttpStatusCode.OK);

        var read = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        Emails(read)["VerificationSuggested"].Should().Be("Off", "the person chose it");
        Emails(read)["Tasks"].Should().Be("Instantly");
    }

    [Fact]
    public async Task A_save_keeps_scope_and_toasts_and_another_member_keeps_the_defaults()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var grid = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        var body = Save(grid, rows: []);
        body["scope"] = "Everything";
        body["pauseToasts"] = true;
        body["closeToasts"] = false;
        body["toastsOff"] = new JsonArray("Moves", "Mentions");
        (await owner.PutAsync(Path, body)).Expect(HttpStatusCode.OK);

        var read = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        read.Text("scope").Should().Be("Everything");
        read.At("pauseToasts").GetBoolean().Should().BeTrue();
        read.At("closeToasts").GetBoolean().Should().BeFalse();
        read.Body.GetProperty("toastsOff").EnumerateArray().Select(k => k.GetString()).Should().Equal("Moves", "Mentions");

        var theirs = (await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK);
        theirs.Text("scope").Should().Be("MyJobs");
        theirs.At("pauseToasts").GetBoolean().Should().BeFalse();
        theirs.Count("toastsOff").Should().Be(0);
    }

    [Fact]
    public async Task A_role_without_verification_or_billing_sees_neither_row()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        var kinds = Emails((await viewer.GetAsync(Path)).Expect(HttpStatusCode.OK)).Keys;

        kinds.Should().NotContain(["VerificationSuggested", "AiUseAndBilling"]);
        kinds.Should().Contain("Mentions");
    }

    [Fact]
    public async Task The_same_person_in_a_second_organization_keeps_its_own_choices()
    {
        using var a = await fixture.SignedInWithOrganizationAsync();
        using var b = await fixture.SignedInWithOrganizationAsync();
        var email = (await a.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;
        (await b.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = email, ["role"] = "Recruiter" })).Expect(HttpStatusCode.Created);
        await a.Session.AcceptInvitationAsync(await fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct), Ct);
        // Joining made B the active organization.
        var inB = (await a.GetAsync(Path)).Expect(HttpStatusCode.OK);
        (await a.PutAsync(Path, Save(inB, rows: new() { ["Mentions"] = "Off" }))).Expect(HttpStatusCode.OK);

        (await a.PostAsync("/v1/auth/switch-organization", new JsonObject { ["organizationId"] = a.OrganizationId })).Expect(HttpStatusCode.OK);
        Emails((await a.GetAsync(Path)).Expect(HttpStatusCode.OK))["Mentions"].Should().Be("Instantly", "the choice made in B stays in B");
    }

    [Fact]
    public async Task A_row_or_a_toast_named_twice_is_refused_and_saves_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var grid = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        var twice = Save(grid, rows: []);
        twice["toastsOff"] = new JsonArray("Moves", "Moves");
        (await owner.PutAsync(Path, twice)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("toastsOff");

        var rows = Save(grid, rows: []);
        rows["rows"]!.AsArray().Add(new JsonObject { ["kind"] = "Mentions", ["email"] = "Off", ["slack"] = null });
        (await owner.PutAsync(Path, rows)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("rows");

        Emails((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK))["Mentions"].Should().Be("Instantly");
    }

    [Fact]
    public async Task Without_antiforgery_with_an_API_key_or_signed_out_the_choices_are_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var body = Save((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK), rows: new() { ["Mentions"] = "Off" });
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));
        using var signedOut = new ApiSession(fixture);

        using (var response = await owner.Session.Client.PutAsJsonAsync(Path, body, Ct))
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await key.GetAsync(Path)).Status.Should().Be(HttpStatusCode.Unauthorized, "a key is no person and has no choices");
        (await key.PutAsync(Path, body)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await signedOut.Client.GetAsync(Path, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Emails((await owner.GetAsync(Path)).Expect(HttpStatusCode.OK))["Mentions"].Should().Be("Instantly");
    }

    private static Dictionary<string, string> Emails(ApiResult grid) =>
        grid.Body.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!, r => r.GetProperty("email").GetString()!);

    /// <summary>The grid as it reads, with <paramref name="rows"/> changed, as the page saves it.</summary>
    internal static JsonObject Save(ApiResult grid, Dictionary<string, string> rows)
    {
        var body = grid.Body;
        return new JsonObject
        {
            ["scope"] = body.GetProperty("scope").GetString(),
            ["pauseToasts"] = body.GetProperty("pauseToasts").GetBoolean(),
            ["closeToasts"] = body.GetProperty("closeToasts").GetBoolean(),
            ["toastsOff"] = JsonNode.Parse(body.GetProperty("toastsOff").GetRawText()),
            ["rows"] = new JsonArray([.. body.GetProperty("rows").EnumerateArray().Select(r =>
            {
                var kind = r.GetProperty("kind").GetString()!;
                return (JsonNode)new JsonObject
                {
                    ["kind"] = kind,
                    ["email"] = rows.GetValueOrDefault(kind, r.GetProperty("email").GetString()!),
                    ["slack"] = r.GetProperty("slack").ValueKind == JsonValueKind.Null ? null : r.GetProperty("slack").GetBoolean(),
                };
            })]),
        };
    }
}
