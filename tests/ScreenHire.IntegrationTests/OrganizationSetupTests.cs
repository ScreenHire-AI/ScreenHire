using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The setup checklist's choices, saved for the organization (docs/architecture/notifications.md): a step marked Not needed
/// and a hidden checklist hold for every Owner and Admin, Show setup again brings both back, only Edit settings changes
/// them, and one organization's choices never reach another's. Whether a step is done is the dashboard's, from the
/// organization's own data, so nothing here reads it.
/// </summary>
public sealed class OrganizationSetupTests(AppHostFixture fixture)
{
    private static JsonObject Skip(string step) => new() { ["step"] = step };

    private static string[] Skipped(ApiResult result) => [.. result.At("skippedSteps").EnumerateArray().Select(s => s.GetString()!)];

    private static bool Hidden(ApiResult result) => result.At("hidden").GetBoolean();

    [Fact]
    public async Task A_skipped_step_and_a_hidden_checklist_hold_for_another_admin_until_someone_shows_setup_again()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var fresh = (await owner.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK);
        Hidden(fresh).Should().BeFalse();
        Skipped(fresh).Should().BeEmpty();

        (await owner.PostAsync("/v1/organization/setup/skip", Skip("Email"))).Expect(HttpStatusCode.OK);
        // Twice is still once, and a second step joins the first rather than replacing it.
        (await admin.PostAsync("/v1/organization/setup/skip", Skip("Email"))).Expect(HttpStatusCode.OK);
        Skipped((await admin.PostAsync("/v1/organization/setup/skip", Skip("Team"))).Expect(HttpStatusCode.OK)).Should().Equal("Email", "Team");
        Skipped((await owner.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK)).Should().Equal("Email", "Team");

        Hidden((await admin.PostAsync("/v1/organization/setup/hide")).Expect(HttpStatusCode.OK)).Should().BeTrue();
        Hidden((await owner.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK)).Should().BeTrue();

        var shown = (await owner.PostAsync("/v1/organization/setup/show")).Expect(HttpStatusCode.OK);
        Hidden(shown).Should().BeFalse();
        Skipped(shown).Should().BeEmpty();
        Skipped((await admin.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK)).Should().BeEmpty();

        // The choices are not settings anyone reviews: General's history and Last updated stay as they were.
        var general = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK);
        general.At("lastUpdated.created").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Skips_sent_at_the_same_time_all_stay()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");

        // Each skip rewrites the whole list, so without the row lock the later save drops the others.
        var responses = await Task.WhenAll(
            owner.PostAsync("/v1/organization/setup/skip", Skip("Branding")),
            admin.PostAsync("/v1/organization/setup/skip", Skip("Job")),
            owner.PostAsync("/v1/organization/setup/skip", Skip("Team")),
            admin.PostAsync("/v1/organization/setup/skip", Skip("Email")));
        foreach (var response in responses)
        {
            response.Expect(HttpStatusCode.OK);
        }

        Skipped((await owner.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK))
            .Should().BeEquivalentTo(["Branding", "Job", "Team", "Email"]);
    }

    [Fact]
    public async Task Only_edit_settings_changes_the_choices_and_an_unknown_step_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");

        // Any member may read them; only Edit settings changes them.
        (await recruiter.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK);
        (await recruiter.PostAsync("/v1/organization/setup/skip", Skip("Branding"))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/organization/setup/hide")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync("/v1/organization/setup/show")).Expect(HttpStatusCode.Forbidden);

        (await owner.PostAsync("/v1/organization/setup/skip", Skip("CareersPage"))).Expect(HttpStatusCode.BadRequest);
        // A body without a step is refused, not read as the first step.
        (await owner.PostAsync("/v1/organization/setup/skip", new JsonObject())).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("step");
        var setup = (await owner.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK);
        Skipped(setup).Should().BeEmpty();
        Hidden(setup).Should().BeFalse();
    }

    [Fact]
    public async Task One_organization_hiding_its_setup_leaves_another_organization_s_alone()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();

        (await first.PostAsync("/v1/organization/setup/skip", Skip("Job"))).Expect(HttpStatusCode.OK);
        (await first.PostAsync("/v1/organization/setup/hide")).Expect(HttpStatusCode.OK);

        var other = (await second.GetAsync("/v1/organization/setup")).Expect(HttpStatusCode.OK);
        Hidden(other).Should().BeFalse();
        Skipped(other).Should().BeEmpty();
    }
}
