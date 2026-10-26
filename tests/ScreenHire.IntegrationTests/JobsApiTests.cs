using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.IntegrationTests;

/// <summary>The recruiter dashboard's job endpoints (<c>/v1/jobs</c>), against the shared AppHost. Each test has its own organization.</summary>
public sealed class JobsApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Creating_a_job_makes_a_draft_with_a_slug_from_its_title_and_the_default_workflow()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var created = await recruiter.CreateJobAsync(Samples.RemoteJob("Senior Platform Engineer"));

        created.Text("slug").Should().Be("senior-platform-engineer");
        created.Text("status").Should().Be("Draft");
        created.At("publishedVersion").GetInt32().Should().Be(0);
        created.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse("a draft has no published version to differ from");
        created.At("template").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        created.At("workflowVersion").GetInt32().Should().Be(1);
        created.Text("workflow.stages.0.id").Should().Be("applied");
        created.Count("workflow.stages").Should().Be(7);
        created.Text("salary.currency").Should().Be("GBP");
        var fetched = await recruiter.GetAsync($"/v1/jobs/{created.Id()}");
        fetched.Expect(HttpStatusCode.OK).Text("title").Should().Be("Senior Platform Engineer");
    }

    [Fact]
    public async Task Creating_a_second_job_with_the_same_title_gets_the_next_free_slug()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var first = await recruiter.CreateJobAsync(Samples.RemoteJob("Data Analyst"));
        var second = await recruiter.CreateJobAsync(Samples.RemoteJob("Data Analyst"));

        first.Text("slug").Should().Be("data-analyst");
        second.Text("slug").Should().Be("data-analyst-2");
    }

    [Fact]
    public async Task Creating_a_job_from_a_system_template_copies_its_workflow_and_applies_its_posting_defaults()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = new JsonObject
        {
            ["title"] = "Backend Engineer",
            ["templateVersionId"] = SystemTemplates.SoftwareEngineerVersion4,
            ["applicantLocationRequirements"] = new JsonArray("GB"),
        };

        var created = await recruiter.CreateJobAsync(body);

        created.Text("department").Should().Be("Engineering", "the template's posting default fills what the request left out");
        created.Text("workplaceType").Should().Be("Remote");
        created.Text("employmentType").Should().Be("FullTime");
        created.Text("description").Should().Contain("About the role");
        created.Text("template.versionId").Should().Be(SystemTemplates.SoftwareEngineerVersion4.ToString());
        created.Text("template.templateId").Should().Be(SystemTemplates.SoftwareEngineer.ToString());
        created.Text("template.templateName").Should().Be("Software engineer");
        created.At("template.version").GetInt32().Should().Be(4);
        created.At("template.updateAvailable").GetBoolean().Should().BeFalse();
        var templateVersion = await recruiter.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/4");
        // The template's stages name library scorecards; the job holds a copy of their questions, and nothing else differs.
        var templateStages = templateVersion.At("definition.workflow.stages").EnumerateArray().ToList();
        var jobStages = created.At("workflow.stages").EnumerateArray().ToList();
        jobStages.Select(s => s.GetProperty("id").GetString()).Should().Equal(templateStages.Select(s => s.GetProperty("id").GetString()));
        foreach (var (templateStage, jobStage) in templateStages.Zip(jobStages))
        {
            templateStage.Has("scorecard").Should().BeFalse("a template names its scorecard and holds no copy");
            if (templateStage.Has("scorecardTemplateId"))
            {
                var named = templateStage.GetProperty("scorecardTemplateId").GetString();
                jobStage.GetProperty("scorecardTemplateId").GetString().Should().Be(named);
                var library = (await recruiter.GetAsync($"/v1/scorecard-templates/{named}")).Expect(HttpStatusCode.OK);
                jobStage.GetProperty("scorecard").GetProperty("questions").GetRawText().Should().Be(library.At("questions").GetRawText());
                jobStage.GetProperty("scorecard").GetProperty("name").GetString().Should().Be(library.Text("name"));
            }
            else
            {
                jobStage.Has("scorecard").Should().BeFalse("a stage that names no scorecard has none");
            }
        }

        created.Text("workflow.stages.2.id").Should().Be("technical");
        created.Text("workflow.stages.3.scorecard.questions.0.id").Should().Be("role_skills", "the newest version ships default scorecards");
    }

    [Fact]
    public async Task Request_values_win_over_the_templates_posting_defaults()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = new JsonObject
        {
            ["title"] = "Staff Engineer",
            ["templateVersionId"] = SystemTemplates.SoftwareEngineerVersion1,
            ["department"] = "Platform",
            ["workplaceType"] = "Hybrid",
        };

        var created = await recruiter.CreateJobAsync(body);

        created.Text("department").Should().Be("Platform");
        created.Text("workplaceType").Should().Be("Hybrid");
        created.Text("employmentType").Should().Be("FullTime", "the template still supplies what the request omitted");
    }

    [Fact]
    public async Task Creating_a_job_from_an_unknown_or_unpublished_template_version_is_a_validation_error()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var draftTemplate = await recruiter.CreateTemplateAsync("Draft only");
        var draftVersionId = draftTemplate.Id("versions.0.id");

        var unknown = await recruiter.PostAsync("/v1/jobs", new JsonObject { ["title"] = "A", ["templateVersionId"] = Guid.NewGuid() });
        var unpublished = await recruiter.PostAsync("/v1/jobs", new JsonObject { ["title"] = "B", ["templateVersionId"] = draftVersionId });

        unknown.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("templateVersionId");
        unpublished.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("templateVersionId", "only published versions can start a job");
        (await recruiter.GetAsync("/v1/jobs")).Count().Should().Be(0, "a rejected create saves nothing");
    }

    [Fact]
    public async Task Updating_a_draft_replaces_its_posting_content_and_may_change_its_slug()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Support Engineer"));
        var update = Samples.RemoteJob("Support Engineering Lead");
        update["department"] = "Customer";
        update["slug"] = "support-lead";

        var updated = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}", update)).Expect(HttpStatusCode.OK);

        updated.Text("title").Should().Be("Support Engineering Lead");
        updated.Text("department").Should().Be("Customer");
        updated.Text("slug").Should().Be("support-lead");
        updated.Text("status").Should().Be("Draft");
        updated.At("updatedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task Updating_a_draft_to_another_jobs_slug_is_a_validation_error()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await recruiter.CreateJobAsync(Samples.RemoteJob("Designer"));
        var other = await recruiter.CreateJobAsync(Samples.RemoteJob("Illustrator"));
        var update = Samples.RemoteJob("Illustrator");
        update["slug"] = "designer";

        var result = await recruiter.PutAsync($"/v1/jobs/{other.Id()}", update);

        result.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("slug");
    }

    [Fact]
    public async Task Invalid_posting_content_is_a_validation_error_keyed_by_the_field()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var badCountry = Samples.RemoteJob("Country check");
        badCountry["locations"] = new JsonArray(new JsonObject { ["country"] = "Britain", ["city"] = "London" });
        badCountry["applicantLocationRequirements"] = new JsonArray("XYZ");
        var badSalary = Samples.RemoteJob("Salary check");
        badSalary["salary"] = new JsonObject { ["min"] = 60000, ["max"] = 50000, ["currency"] = "GBP", ["period"] = "Year" };
        var badCurrency = Samples.RemoteJob("Currency check");
        badCurrency["salary"] = new JsonObject { ["min"] = 1, ["max"] = 2, ["currency"] = "pounds", ["period"] = "Year" };
        var noTitle = Samples.RemoteJob("");

        var country = await recruiter.PostAsync("/v1/jobs", badCountry);
        var salary = await recruiter.PostAsync("/v1/jobs", badSalary);
        var currency = await recruiter.PostAsync("/v1/jobs", badCurrency);
        var title = await recruiter.PostAsync("/v1/jobs", noTitle);

        country.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain(["locations[0].country", "applicantLocationRequirements[0]"]);
        salary.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("salary.max");
        currency.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("salary.currency");
        title.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("title");
        (await recruiter.GetAsync("/v1/jobs")).Count().Should().Be(0);
    }

    [Fact]
    public async Task Publishing_a_job_opens_it_as_version_one_and_lists_the_version()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Site Reliability Engineer"));

        var published = await recruiter.PublishJobAsync(job.Id());

        published.Text("status").Should().Be("Open");
        published.At("publishedVersion").GetInt32().Should().Be(1);
        published.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse();
        published.At("publishedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        var versions = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions")).Expect(HttpStatusCode.OK);
        versions.Count().Should().Be(1);
        versions.At("0.version").GetInt32().Should().Be(1);
        var version = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions/1")).Expect(HttpStatusCode.OK);
        version.Text("snapshot.title").Should().Be("Site Reliability Engineer");
        version.Text("snapshot.slug").Should().Be("site-reliability-engineer");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions/2")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publishing_an_unchanged_job_again_adds_no_version()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Release Engineer"));
        await recruiter.PublishJobAsync(job.Id());

        var again = await recruiter.PublishJobAsync(job.Id());

        again.At("publishedVersion").GetInt32().Should().Be(1);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions")).Count().Should().Be(1);
    }

    [Fact]
    public async Task Changing_a_published_job_flags_unpublished_changes_until_it_is_published_again_as_version_two()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Security Engineer"));
        await recruiter.PublishJobAsync(job.Id());
        var update = Samples.RemoteJob("Security Engineer");
        update["description"] = "<p>Now with an on-call rota.</p>";

        var changed = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}", update)).Expect(HttpStatusCode.OK);

        changed.Text("status").Should().Be("Open", "editing does not close or republish a job");
        changed.At("hasUnpublishedChanges").GetBoolean().Should().BeTrue();
        changed.At("publishedVersion").GetInt32().Should().Be(1);
        var republished = await recruiter.PublishJobAsync(job.Id());
        republished.At("publishedVersion").GetInt32().Should().Be(2);
        republished.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse();
        var versions = await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions");
        versions.Count().Should().Be(2);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions/1")).Text("snapshot.description").Should().Contain("rely on", "version 1 keeps what was first published");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions/2")).Text("snapshot.description").Should().Contain("on-call");
    }

    [Fact]
    public async Task A_published_jobs_slug_cannot_change()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Account Executive"));
        await recruiter.PublishJobAsync(job.Id());
        var update = Samples.RemoteJob("Account Executive");
        update["slug"] = "account-exec";

        var result = await recruiter.PutAsync($"/v1/jobs/{job.Id()}", update);

        result.Expect(HttpStatusCode.Conflict);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Text("slug").Should().Be("account-executive");
    }

    [Fact]
    public async Task A_remote_job_without_applicant_countries_cannot_be_published()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = Samples.RemoteJob("Remote without countries");
        body.Remove("applicantLocationRequirements");
        var job = await recruiter.CreateJobAsync(body);

        var result = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/publish");

        result.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("applicantLocationRequirements");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Text("status").Should().Be("Draft");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions")).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_job_without_a_description_cannot_be_published()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = Samples.RemoteJob("No description");
        body.Remove("description");
        var job = await recruiter.CreateJobAsync(body);

        var result = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/publish");

        result.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("description");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Text("status").Should().Be("Draft");
    }

    [Fact]
    public async Task An_onsite_job_without_a_location_cannot_be_published()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = Samples.RemoteJob("Onsite without place");
        body["workplaceType"] = "Onsite";
        body.Remove("applicantLocationRequirements");
        var job = await recruiter.CreateJobAsync(body);

        var result = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/publish");

        result.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("locations");
    }

    [Fact]
    public async Task Closing_a_job_closes_it_and_closing_it_again_changes_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Office Manager"));
        await recruiter.PublishJobAsync(job.Id());

        var closed = await recruiter.CloseJobAsync(job.Id());
        var again = await recruiter.CloseJobAsync(job.Id());

        closed.Text("status").Should().Be("Closed");
        closed.At("closedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        again.Text("status").Should().Be("Closed");
        again.Text("closedAt").Should().Be(closed.Text("closedAt"), "the second close does not restamp the job");
        again.Text("updatedAt").Should().Be(closed.Text("updatedAt"));
    }

    [Fact]
    public async Task A_closed_job_cannot_be_edited_or_published()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Closed role"));
        await recruiter.CloseJobAsync(job.Id());

        var edit = await recruiter.PutAsync($"/v1/jobs/{job.Id()}", Samples.RemoteJob("Closed role, edited"));
        var publish = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/publish");

        edit.Expect(HttpStatusCode.Conflict);
        publish.Expect(HttpStatusCode.Conflict);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Text("title").Should().Be("Closed role");
    }

    [Fact]
    public async Task Template_diff_is_not_found_for_a_job_made_without_a_template()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Plain job"));

        var diff = await recruiter.GetAsync($"/v1/jobs/{job.Id()}/workflow/template-diff");

        diff.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Job_endpoints_need_a_signed_in_user()
    {
        using var visitor = new ApiSession(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;

        using var list = await visitor.Client.GetAsync("/v1/jobs", cancellationToken);
        using var detail = await visitor.Client.GetAsync($"/v1/jobs/{Guid.NewGuid()}", cancellationToken);
        using var create = await visitor.PostAsync("/v1/jobs", new { title = "Nope" }, cancellationToken);
        using var update = await visitor.PutAsync($"/v1/jobs/{Guid.NewGuid()}", new { title = "Nope" }, cancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        detail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        update.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Publishing_a_template_job_with_a_posting_duration_leaves_nothing_unpublished_and_adds_no_version_on_a_repeat()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = new JsonObject
        {
            ["title"] = "Frontend Engineer",
            ["templateVersionId"] = SystemTemplates.SoftwareEngineerVersion1,
            ["applicantLocationRequirements"] = new JsonArray("GB"),
        };
        var job = await recruiter.CreateJobAsync(body);

        var published = await recruiter.PublishJobAsync(job.Id());

        published.At("validThrough").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String, "the template posts for 30 days");
        published.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse("the job still equals the version just frozen");
        var again = await recruiter.PublishJobAsync(job.Id());
        again.At("publishedVersion").GetInt32().Should().Be(1);
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/versions")).Count().Should().Be(1);
    }


    [Fact]
    public async Task Template_diff_names_what_a_newer_template_version_changed_and_the_job_list_flags_the_update()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var copied = (await recruiter.PostAsync("/v1/templates", new JsonObject
        {
            ["name"] = "Our engineering pipeline",
            ["copyFromVersionId"] = SystemTemplates.SoftwareEngineerVersion1,
        })).Expect(HttpStatusCode.Created);
        var templateId = copied.Id();
        var version1 = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/1/publish")).Expect(HttpStatusCode.OK);
        var job = await recruiter.CreateJobAsync(new JsonObject
        {
            ["title"] = "Engineer",
            ["templateVersionId"] = version1.Id(),
            ["applicantLocationRequirements"] = new JsonArray("GB"),
        });

        var upToDate = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/workflow/template-diff")).Expect(HttpStatusCode.OK);
        upToDate.At("updateAvailable").GetBoolean().Should().BeFalse();
        upToDate.Count("changes").Should().Be(0);
        (await recruiter.GetAsync("/v1/jobs")).At("0.templateUpdateAvailable").GetBoolean().Should().BeFalse();

        var next = Samples.DefinitionWithRenamedStage(await Samples.DefinitionOfAsync(recruiter, templateId, 1), "screening", "Phone screen");
        (await recruiter.PostAsync($"/v1/templates/{templateId}/versions", new JsonObject { ["definition"] = next })).Expect(HttpStatusCode.Created);
        var version2 = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/2/publish")).Expect(HttpStatusCode.OK);

        var diff = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/workflow/template-diff")).Expect(HttpStatusCode.OK);
        diff.At("updateAvailable").GetBoolean().Should().BeTrue();
        diff.At("sourceVersion").GetInt32().Should().Be(1);
        diff.At("latestVersion").GetInt32().Should().Be(2);
        diff.Text("latestVersionId").Should().Be(version2.Text("id"));
        diff.Count("changes").Should().Be(1);
        diff.Text("changes.0.path").Should().Be("stages[screening].name");
        diff.Text("changes.0.kind").Should().Be("Changed");
        diff.Text("changes.0.before").Should().Be("Screening");
        diff.Text("changes.0.after").Should().Be("Phone screen");
        (await recruiter.GetAsync("/v1/jobs")).At("0.templateUpdateAvailable").GetBoolean().Should().BeTrue();
        var detail = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        detail.At("template.updateAvailable").GetBoolean().Should().BeTrue();
        detail.At("template.latestPublishedVersion").GetInt32().Should().Be(2);
        detail.Text("workflow.stages.1.name").Should().Be("Screening", "the job keeps its copy until a person adopts the new version");
    }
}
