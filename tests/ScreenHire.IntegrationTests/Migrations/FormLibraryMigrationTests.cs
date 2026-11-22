using System.Text.Json.Nodes;
using Npgsql;

namespace ScreenHire.IntegrationTests.Migrations;

/// <summary>
/// Follow-up questionnaires become library forms with their names and questions intact (workflow-engine.md section 7,
/// Forms). Each own template's current versions link every follow-up to a library form of that template, one version per
/// different content in version order; older published versions and jobs keep their copies; a job's own follow-up that no
/// template holds becomes a form unless the library has its content already; and the tables keep row-level security
/// forced.
/// </summary>
public sealed class FormLibraryMigrationTests(AppHostFixture fixture)
{
    private const string Before = "AddManualPostings";
    private const string After = "AddFormLibrary";

    [Fact]
    public async Task Each_current_follow_up_becomes_a_versioned_library_form_and_jobs_keep_their_copies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scratch = await ScratchDatabase.CreateAsync(fixture, ct);
        await scratch.MigrateAsync(Before, ct);

        var studio = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var (kitchen, bar, borrowed) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (v1, v2, v3) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (barV1, borrowedV1) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (fromKitchen, ownForm, sameAsOwn, likeReferences) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        string Organization(Guid id, string name) => $"""
            INSERT INTO organizations (id, name, slug, created_at, embed_origins, home_region, indeed_eligible, is_trial, questions_per_page, signal_network_opt_in, setup_hidden)
            VALUES ('{id}', '{name}', 'org-{id:N}', now(), ARRAY[]::text[], 'EU', false, false, 0, false, false);
            """;
        string Template(Guid id, Guid organization, string name, int? latest) =>
            $"INSERT INTO hiring_templates (id, organization_id, name, created_at, latest_published_version) VALUES ('{id}', '{organization}', '{name}', now(), {latest?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"});";
        string Version(Guid id, Guid organization, Guid template, int version, string status, params string[] followUps) => $"""
            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            VALUES ('{id}', '{organization}', '{template}', {version}, '{status}', '{Definition(followUps)}', now() - interval '{10 - version} minutes',
                    {(status == "Published" ? $"now() - interval '{10 - version} minutes'" : "NULL")});
            """;
        string Job(Guid id, Guid organization, Guid? template, params string[] followUps) => $"""
            INSERT INTO jobs (id, organization_id, applicant_location_requirements, created_at, description, employment_type, locations, published_version, slug, status, title, workflow, workflow_version, workplace_type, hiring_team, source_template_id)
            VALUES ('{id}', '{organization}', ARRAY[]::text[], now(), 'Cooks.', 'FullTime', '[]', 0, 'job-{id:N}', 'Draft', 'Cook', '{Workflow(followUps)}', 1, 'OnSite', ARRAY[]::uuid[], {(template is { } t ? $"'{t}'" : "NULL")});
            """;

        var availability = FollowUp("availability", "Availability", ("start", "When can you start?"));
        var availabilityRevised = FollowUp("availability", "Availability", ("start", "What is the earliest day you can start?"));
        var references = FollowUp("references", "References", ("referee", "Who can we ask about your work?"));
        var own = FollowUp("own", "Before your first shift", ("shoes", "Do you have kitchen shoes?"));

        // The owner is a superuser here, which row-level security does not bind.
        await scratch.ExecuteAsync($"""
            {Organization(studio, "Harbour Lane Studio")}
            {Organization(other, "Northgate Bakery")}
            {Template(kitchen, studio, "Kitchen", 2)}
            {Version(v1, studio, kitchen, 1, "Published", availability)}
            {Version(v2, studio, kitchen, 2, "Published", availability, references)}
            {Version(v3, studio, kitchen, 3, "Draft", availabilityRevised, references)}
            {Template(bar, studio, "Bar", 1)}
            {Version(barV1, studio, bar, 1, "Published", availability)}
            {Template(borrowed, other, "Kitchen", 1)}
            {Version(borrowedV1, other, borrowed, 1, "Published", references)}
            {Job(fromKitchen, studio, kitchen, availability)}
            {Job(ownForm, studio, null, own)}
            {Job(sameAsOwn, studio, null, own)}
            {Job(likeReferences, studio, kitchen, FollowUp("refs-copy", "References", ("referee", "Who can we ask about your work?")))}
            """, ct);
        var jobsBefore = await scratch.ScalarAsync<string>("SELECT string_agg(workflow::text, '|' ORDER BY id) FROM jobs", ct);

        await scratch.MigrateAsync(After, ct);

        var forms = await FormsAsync(scratch.ConnectionString, ct);
        forms.Where(f => f.Organization == studio).Select(f => (f.Name, f.Versions)).Should().BeEquivalentTo(
            [("Availability", 2), ("References", 1), ("Availability", 1), ("Before your first shift", 1)],
            options => options.WithoutStrictOrdering(),
            "Kitchen's two follow-ups, the second version of Availability from its draft, Bar's own copy of Availability, and one form for the two jobs' identical own follow-up; the job whose follow-up matches References makes none");
        forms.Where(f => f.Organization == other).Select(f => f.Name).Should().Equal("References");
        forms.Should().OnlyContain(f => f.Kind == "Standard" && f.LatestPublished == f.Versions);

        var kitchenAvailability = forms.Single(f => f.Organization == studio && f.Name == "Availability" && f.Versions == 2);
        (await LinkAsync(scratch, v2, "availability", ct)).Should().Be((kitchenAvailability.Id, 1), "the newest published version holds the first content");
        (await LinkAsync(scratch, v3, "availability", ct)).Should().Be((kitchenAvailability.Id, 2), "the draft's changed form is the second version");
        (await LinkAsync(scratch, v2, "references", ct)).Should().Be(await LinkAsync(scratch, v3, "references", ct), "unchanged content is one version");
        (await LinkAsync(scratch, v1, "availability", ct)).Should().Be((null, null), "an older published version keeps its copy as it was");
        var barAvailability = forms.Single(f => f.Organization == studio && f.Name == "Availability" && f.Versions == 1);
        foreach (var (form, version, source) in new[]
        {
            (kitchenAvailability.Id, 1, availability),
            (kitchenAvailability.Id, 2, availabilityRevised),
            (forms.Single(f => f.Organization == studio && f.Name == "References").Id, 1, references),
            (barAvailability.Id, 1, availability),
            (forms.Single(f => f.Name == "Before your first shift").Id, 1, own),
            (forms.Single(f => f.Organization == other).Id, 1, references),
        })
        {
            var content = JsonNode.Parse(await scratch.ScalarAsync<string>(
                $"SELECT content::text FROM form_template_versions WHERE form_template_id = '{form}' AND version = {version}", ct));
            var expected = JsonNode.Parse(source)!.AsObject();
            expected.Remove("id");
            JsonNode.DeepEquals(content, expected).Should().BeTrue($"version {version} of {expected["name"]} keeps the follow-up's name and questions: {content}");
        }

        (await scratch.ScalarAsync<string>("SELECT string_agg(workflow::text, '|' ORDER BY id) FROM jobs", ct))
            .Should().Be(jobsBefore, "a live job's workflow is never rewritten");
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty("FORCE comes back on the tables the copy lifted it from");
        (await scratch.ScalarAsync<bool>("SELECT has_table_privilege('screenhire_app', 'form_templates', 'DELETE')", ct))
            .Should().BeFalse("deleting a form would cascade to its published versions");
        (await scratch.ScalarAsync<string>(
            "SELECT string_agg(policyname || ':' || cmd || ':' || coalesce(qual, ''), '|' ORDER BY policyname) FROM pg_policies WHERE tablename = 'form_template_versions' AND cmd IN ('UPDATE', 'DELETE')", ct))
            .Should().Contain("tenant_delete_drafts:DELETE").And.Contain("tenant_update_drafts:UPDATE").And.Contain("'Draft'", "a published version cannot be changed or deleted");

        await scratch.MigrateAsync(Before, ct);

        (await scratch.ScalarAsync<long>("SELECT count(*) FROM information_schema.tables WHERE table_name IN ('form_templates', 'form_template_versions')", ct))
            .Should().Be(0);
        (await scratch.UnforcedTablesAsync(ct)).Should().BeEmpty();
    }

    private static string FollowUp(string id, string name, params (string Id, string Label)[] questions) =>
        new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["questions"] = new JsonArray([.. questions.Select(q => (JsonNode)new JsonObject { ["id"] = q.Id, ["type"] = "ShortText", ["label"] = q.Label, ["required"] = false })]),
        }.ToJsonString();

    private static string Workflow(string[] followUps) =>
        """{"stages":[],"questionnaire":{"questions":[]},"automations":[],"followUpQuestionnaires":[""" + string.Join(",", followUps) + "]}";

    private static string Definition(string[] followUps) => """{"workflow":""" + Workflow(followUps) + ""","postingDefaults":{}}""";

    private static async Task<(Guid?, int?)> LinkAsync(ScratchDatabase scratch, Guid version, string followUpId, CancellationToken ct)
    {
        var json = await scratch.ScalarAsync<string>(
            $"SELECT f.value::text FROM hiring_template_versions tv, jsonb_array_elements(tv.definition -> 'workflow' -> 'followUpQuestionnaires') f WHERE tv.id = '{version}' AND f.value ->> 'id' = '{followUpId}'",
            ct);
        var node = JsonNode.Parse(json)!.AsObject();
        return (node["formTemplateId"]?.GetValue<Guid>(), node["formVersion"]?.GetValue<int>());
    }

    private sealed record LibraryForm(Guid Id, Guid Organization, string Name, string Kind, int? LatestPublished, int Versions);

    private static async Task<List<LibraryForm>> FormsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT f.id, f.organization_id, f.name, f.kind, f.latest_published_version,
                   (SELECT count(*) FROM form_template_versions v WHERE v.form_template_id = f.id AND v.status = 'Published')::int
            FROM form_templates f
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var forms = new List<LibraryForm>();
        while (await reader.ReadAsync(ct))
        {
            forms.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetInt32(5)));
        }

        return forms;
    }
}
