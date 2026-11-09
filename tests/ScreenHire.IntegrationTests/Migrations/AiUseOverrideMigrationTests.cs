#nullable enable

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// The AI-use policy moved from every job's workflow copy to the organization, with a job's own override beside it. The
/// migration keeps each existing job asking what it asked before: a job whose stored policy differs from what its
/// organization's policy is now gets that policy as its override, and one that matches follows the organization.
/// </summary>
public sealed class AiUseOverrideMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddStartingPointTemplates";
    private const string After = "MoveAiUseToOrganizationAndJob";

    [Fact]
    public async Task Jobs_keep_the_policy_they_asked_under_as_an_override_only_where_it_differs_from_the_organizations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var strict = Guid.CreateVersion7();
        var unset = Guid.CreateVersion7();
        var differs = Guid.CreateVersion7();
        var matches = Guid.CreateVersion7();
        var noSettings = Guid.CreateVersion7();
        var noSettingsAllowed = Guid.CreateVersion7();
        var noPolicy = Guid.CreateVersion7();

        string Workflow(string? aiUse) => aiUse is null ? "{}" : "{\"verificationPolicy\":{\"aiUse\":\"" + aiUse + "\"}}";
        string Job(Guid id, Guid organization, string slug, string? aiUse) => $"""
            INSERT INTO jobs (id, organization_id, applicant_location_requirements, created_at, description, employment_type, locations, published_version, slug, status, title, workflow, workflow_version, workplace_type, hiring_team)
            VALUES ('{id}', '{organization}', ARRAY[]::text[], now(), 'Keeps the books.', 'FullTime', '[]', 0, '{slug}', 'Draft', 'Bookkeeper', '{Workflow(aiUse)}', 1, 'Remote', ARRAY[]::uuid[]);
            """;

        // The owner is a superuser here, which row-level security does not bind.
        await scratch.ExecuteAsync($"""
            INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in)
            VALUES ('{strict}', 'Strict Studio', 'strict-{strict:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false),
                   ('{unset}', 'Plain Studio', 'plain-{unset:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false);
            INSERT INTO verification_settings (organization_id, llm_consistency_check_enabled, default_ai_use, updated_at)
            VALUES ('{strict}', true, 'NotAllowed', now());
            {Job(differs, strict, "differs", "AllowedWithDisclosure")}
            {Job(matches, strict, "matches", "NotAllowed")}
            {Job(noSettings, unset, "no-settings", "NotAllowed")}
            {Job(noSettingsAllowed, unset, "no-settings-same", "AllowedWithDisclosure")}
            {Job(noPolicy, unset, "no-policy", null)}
            """, ct);

        await scratch.MigrateAsync(After, ct);

        async Task<string?> OverrideAsync(Guid id) =>
            await scratch.ScalarAsync<object>($"SELECT ai_use_override FROM jobs WHERE id = '{id}'", ct) is string value ? value : null;

        (await OverrideAsync(differs)).Should().Be("AllowedWithDisclosure", "it asked under a policy the organization no longer has");
        (await OverrideAsync(matches)).Should().BeNull("it follows an organization that already has its policy");
        (await OverrideAsync(noSettings)).Should().Be("NotAllowed", "an organization with no saved settings has Allowed with disclosure");
        (await OverrideAsync(noSettingsAllowed)).Should().BeNull();
        (await OverrideAsync(noPolicy)).Should().BeNull("a workflow with no policy follows the organization");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE comes back on the tables the data step lifted it from");

        await scratch.MigrateAsync(Before, ct);

        (await scratch.ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_name = 'jobs' AND column_name = 'ai_use_override'", ct))
            .Should().Be(0, "undoing the migration drops the column");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
    }
}
