using System.Text.Json.Nodes;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A job's own copy of its workflow: <c>PUT /v1/jobs/{jobId}/workflow</c> (a recruiter's edit) and
/// <c>POST /v1/jobs/{jobId}/workflow/apply-template-update</c> (adopting a newer published template version). Each test has
/// its own organization.
/// </summary>
public sealed class JobWorkflowTests(AppHostFixture fixture)
{
    private static readonly Guid UnknownId = Guid.NewGuid();

    [Fact]
    public async Task Editing_a_jobs_workflow_replaces_it_and_starts_workflow_version_two_without_touching_the_template_source()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, versionId) = await recruiter.CreatePublishedTemplateAsync("Edit source");
        var job = await CreateTemplateJobAsync(recruiter, versionId);
        job.At("workflowVersion").GetInt32().Should().Be(1);
        var edited = WorkflowOf(job);
        SetStageName(edited, "screening", "Phone screen");

        var updated = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(edited, 1))).Expect(HttpStatusCode.OK);

        updated.At("workflowVersion").GetInt32().Should().Be(2);
        updated.Text("workflow.stages.1.name").Should().Be("Phone screen");
        updated.Text("template.templateId").Should().Be(templateId.ToString());
        updated.At("template.version").GetInt32().Should().Be(1, "the job still records the template version it came from");
        updated.Text("template.versionId").Should().Be(versionId.ToString());
        updated.At("template.updateAvailable").GetBoolean().Should().BeFalse("no newer version was published");
        var stored = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        stored.At("workflowVersion").GetInt32().Should().Be(2);
        stored.Text("workflow.stages.1.name").Should().Be("Phone screen");
        var template = await Samples.DefinitionOfAsync(recruiter, templateId, 1);
        template["workflow"]!["stages"]![1]!["name"]!.GetValue<string>().Should().Be("Screening", "a job's edit never changes its template");
    }

    [Fact]
    public async Task Saving_an_identical_workflow_changes_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Unchanged workflow"));

        var same = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(WorkflowOf(job), 1))).Expect(HttpStatusCode.OK);

        same.At("workflowVersion").GetInt32().Should().Be(1);
        same.At("workflow").GetRawText().Should().Be(job.At("workflow").GetRawText());
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).At("workflowVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_stale_expected_workflow_version_is_a_conflict_and_saves_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Contended workflow"));
        var first = WorkflowOf(job);
        SetStageName(first, "screening", "First edit");
        (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(first, 1))).Expect(HttpStatusCode.OK);
        var second = WorkflowOf(job);
        SetStageName(second, "screening", "Second edit");

        var behind = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(second, 1));
        var ahead = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(second, 3));

        behind.Expect(HttpStatusCode.Conflict);
        ahead.Expect(HttpStatusCode.Conflict);
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(2);
        stored.Text("workflow.stages.1.name").Should().Be("First edit");
    }

    [Fact]
    public async Task A_closed_jobs_workflow_cannot_be_edited()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Closed workflow"));
        await recruiter.CloseJobAsync(job.Id());
        var edited = WorkflowOf(job);
        SetStageName(edited, "screening", "Too late");

        var update = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(edited, 1));

        update.Expect(HttpStatusCode.Conflict);
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.Text("workflow.stages.1.name").Should().Be("Screening");
    }

    [Fact]
    public async Task Another_organizations_job_workflow_is_not_found_for_both_routes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (_, versionId) = await owner.CreatePublishedTemplateAsync("Private pipeline");
        var job = await CreateTemplateJobAsync(owner, versionId);
        var edited = WorkflowOf(job);
        SetStageName(edited, "screening", "Not yours");

        var update = await other.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(edited, 1));
        var apply = await other.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(versionId, 1));

        update.Status.Should().Be(HttpStatusCode.NotFound);
        apply.Status.Should().Be(HttpStatusCode.NotFound);
        var stored = (await owner.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.Text("workflow.stages.1.name").Should().Be("Screening");
    }

    [Fact]
    public async Task A_missing_job_is_not_found()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var update = await recruiter.PutAsync($"/v1/jobs/{UnknownId}/workflow", WorkflowRequest(Samples.Definition()["workflow"]!.AsObject(), 1));
        var apply = await recruiter.PostAsync($"/v1/jobs/{UnknownId}/workflow/apply-template-update", ApplyRequest(UnknownId, 1));

        update.Status.Should().Be(HttpStatusCode.NotFound);
        apply.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_workflow_that_breaks_a_structural_rule_is_rejected_with_workflow_keys_and_saves_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Invalid workflow"));
        var noStages = WorkflowOf(job);
        noStages["stages"] = new JsonArray();
        var duplicateId = WorkflowOf(job);
        duplicateId["stages"]![1]!["id"] = "applied";

        var empty = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(noStages, 1));
        var duplicate = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(duplicateId, 1));

        empty.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("workflow.stages");
        empty.ProblemKeys().Should().OnlyContain(k => k.StartsWith("workflow.", StringComparison.Ordinal));
        duplicate.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("workflow.stages[1].id");
        duplicate.ProblemKeys().Should().OnlyContain(k => k.StartsWith("workflow.", StringComparison.Ordinal));
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).At("workflowVersion").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("Reject")]
    [InlineData("QueueForRejectionReview")]
    public async Task An_integrity_signal_cannot_drive_a_rejection_in_a_jobs_workflow(string actionType)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Guardrail"));
        var automation = Samples.IntegrityAutomation(new JsonObject { ["type"] = actionType, ["reason"] = "Not a fit" });
        automation["conditions"]!["rules"]![0]!["value"] = "VerificationRecommended";
        var workflow = WorkflowOf(job);
        workflow["automations"] = new JsonArray(automation);

        var update = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(workflow, 1));

        update.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("workflow.automations[0].actions[0]");
        update.ProblemKeys().Should().OnlyContain(k => k.StartsWith("workflow.", StringComparison.Ordinal));
        update.At("errors").GetProperty("workflow.automations[0].actions[0]")[0].GetString()
            .Should().Contain("Integrity signals never drive rejection");
        var stored = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.Count("workflow.automations").Should().Be(0);
    }

    // Publishing refuses such a version, so it is written as the owner: it stands for one published before the
    // guardrail tightened. Applying it must be refused all the same.
    [Fact]
    public async Task A_template_update_whose_integrity_signal_drives_a_rejection_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Old guardrail");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        var version2 = await PublishVersion2Async(recruiter, templateId, "screening", "Phone screen");
        var definition = await Samples.DefinitionOfAsync(recruiter, templateId, 2);
        var automation = Samples.IntegrityAutomation(new JsonObject { ["type"] = "Reject", ["reason"] = "Not a fit" });
        automation["conditions"]!["rules"]![0]!["value"] = "VerificationRecommended";
        definition["workflow"]!["automations"] = new JsonArray(automation);
        await using (var connection = new Npgsql.NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(ct)))
        {
            await connection.OpenAsync(ct);
            await using var command = new Npgsql.NpgsqlCommand(
                "UPDATE hiring_template_versions SET definition = @definition::jsonb WHERE id = @id", connection);
            command.Parameters.AddWithValue("definition", definition.ToJsonString());
            command.Parameters.AddWithValue("id", version2);
            (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
        }

        var apply = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 1));

        apply.Expect(HttpStatusCode.BadRequest).At("errors").GetProperty("workflow.automations[0].actions[0]")[0].GetString()
            .Should().Contain("Integrity signals never drive rejection");
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.At("template.version").GetInt32().Should().Be(1);
        stored.Text("workflow.stages.1.name").Should().Be("Screening");
    }

    [Fact]
    public async Task An_integrity_signal_may_still_route_a_candidate_for_review_in_a_jobs_workflow()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Routing"));
        var workflow = WorkflowOf(job);
        workflow["automations"] = new JsonArray(Samples.IntegrityAutomation(new JsonObject { ["type"] = "AddTag", ["tag"] = "needs-review" }));

        var update = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(workflow, 1));

        update.Expect(HttpStatusCode.OK).Count("workflow.automations").Should().Be(1);
        update.At("workflowVersion").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_stage_that_still_holds_an_application_cannot_be_removed_but_an_empty_one_can()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Occupied stage"));
        await recruiter.PublishJobAsync(job.Id());
        var application = await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "screening" }))
            .Expect(HttpStatusCode.OK);
        var withoutScreening = WorkflowWithoutStage(job, "screening");
        var withoutInterview = WorkflowWithoutStage(job, "interview");

        var occupied = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(withoutScreening, 1));
        var empty = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(withoutInterview, 1));

        occupied.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("workflow.stages");
        var message = occupied.At("errors").GetProperty("workflow.stages").EnumerateArray().Single().GetString();
        message.Should().Contain("Screening").And.Contain("1 candidate").And.Contain("Move them");
        empty.Expect(HttpStatusCode.OK).At("workflowVersion").GetInt32().Should().Be(2);
        empty.Count("workflow.stages").Should().Be(6);
        (await recruiter.GetAsync($"/v1/applications/{application.Id()}")).Text("stage.id").Should().Be("screening");
    }

    [Fact]
    public async Task Applying_a_published_template_update_takes_its_workflow_and_clears_the_diff()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Updatable");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        var version2 = await PublishVersion2Async(recruiter, templateId, "screening", "Phone screen");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/workflow/template-diff")).Expect(HttpStatusCode.OK)
            .At("updateAvailable").GetBoolean().Should().BeTrue();

        var applied = (await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 1)))
            .Expect(HttpStatusCode.OK);

        applied.At("template.version").GetInt32().Should().Be(2);
        applied.Text("template.versionId").Should().Be(version2.ToString());
        applied.At("template.updateAvailable").GetBoolean().Should().BeFalse();
        applied.At("workflowVersion").GetInt32().Should().Be(2);
        applied.Text("workflow.stages.1.name").Should().Be("Phone screen");
        var diff = (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/workflow/template-diff")).Expect(HttpStatusCode.OK);
        diff.At("updateAvailable").GetBoolean().Should().BeFalse();
        diff.At("sourceVersion").GetInt32().Should().Be(2);
        diff.Count("changes").Should().Be(0);
        (await recruiter.GetAsync("/v1/jobs")).At("0.templateUpdateAvailable").GetBoolean().Should().BeFalse();
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(2);
        stored.At("template.version").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Applying_a_template_update_replaces_the_jobs_own_edits()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Replaces edits");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        var edited = WorkflowOf(job);
        SetStageName(edited, "interview", "Job-only interview");
        (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(edited, 1))).Expect(HttpStatusCode.OK);
        var version2 = await PublishVersion2Async(recruiter, templateId, "screening", "Phone screen");

        var applied = (await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 2)))
            .Expect(HttpStatusCode.OK);

        applied.At("workflowVersion").GetInt32().Should().Be(3);
        applied.Text("workflow.stages.1.name").Should().Be("Phone screen");
        applied.Text("workflow.stages.2.name").Should().Be("Interview", "the job's own rename is replaced by the template's workflow");
    }

    [Fact]
    public async Task A_template_update_that_drops_a_stage_holding_an_application_is_refused()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Drops a stage");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        await recruiter.PublishJobAsync(job.Id());
        var application = await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"));
        (await recruiter.PostAsync($"/v1/applications/{application.Id()}/move", new JsonObject { ["toStageId"] = "interview" }))
            .Expect(HttpStatusCode.OK);
        var definition = await Samples.DefinitionOfAsync(recruiter, templateId, 1);
        var stages = definition["workflow"]!["stages"]!.AsArray();
        stages.Remove(stages.Single(s => s!["id"]!.GetValue<string>() == "interview"));
        (await recruiter.PostAsync($"/v1/templates/{templateId}/versions", new JsonObject { ["definition"] = definition })).Expect(HttpStatusCode.Created);
        var version2 = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/2/publish")).Expect(HttpStatusCode.OK).Id();

        var apply = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 1));

        apply.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("workflow.stages");
        apply.At("errors").GetProperty("workflow.stages")[0].GetString().Should().Contain("Interview").And.Contain("Move them");
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.At("template.version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_template_update_must_be_a_published_version_of_the_jobs_own_template()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Source template");
        var (_, otherTemplateVersion) = await recruiter.CreatePublishedTemplateAsync("Some other template");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        var draft = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions")).Expect(HttpStatusCode.Created);
        draft.Text("status").Should().Be("Draft");

        var otherTemplate = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(otherTemplateVersion, 1));
        var draftVersion = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(draft.Id(), 1));
        var unknown = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(UnknownId, 1));

        otherTemplate.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
        draftVersion.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
        unknown.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.At("template.version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_job_made_without_a_template_cannot_apply_a_template_update()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync("Not its template");
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("No template"));

        var apply = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(versionId, 1));

        apply.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("templateVersionId");
    }

    [Fact]
    public async Task Applying_a_template_update_with_a_stale_expected_version_or_to_a_closed_job_is_a_conflict()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await recruiter.CreatePublishedTemplateAsync("Conflicts");
        var job = await CreateTemplateJobAsync(recruiter, version1);
        var closed = await CreateTemplateJobAsync(recruiter, version1);
        await recruiter.CloseJobAsync(closed.Id());
        var version2 = await PublishVersion2Async(recruiter, templateId, "screening", "Phone screen");

        var stale = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 2));
        var onClosed = await recruiter.PostAsync($"/v1/jobs/{closed.Id()}/workflow/apply-template-update", ApplyRequest(version2, 1));

        stale.Expect(HttpStatusCode.Conflict);
        onClosed.Expect(HttpStatusCode.Conflict);
        var stored = await recruiter.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.At("template.version").GetInt32().Should().Be(1);
        (await recruiter.GetAsync($"/v1/jobs/{closed.Id()}")).At("template.version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_viewer_cannot_edit_a_workflow_or_apply_a_template_update()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (templateId, version1) = await owner.CreatePublishedTemplateAsync("Viewer pipeline");
        var job = await CreateTemplateJobAsync(owner, version1);
        var version2 = await PublishVersion2Async(owner, templateId, "screening", "Phone screen");
        using var viewer = await ViewerOfAsync(owner.OrganizationId, cancellationToken);
        var edited = WorkflowOf(job);
        SetStageName(edited, "screening", "Sneaky");

        var update = await viewer.PutAsync($"/v1/jobs/{job.Id()}/workflow", WorkflowRequest(edited, 1));
        var apply = await viewer.PostAsync($"/v1/jobs/{job.Id()}/workflow/apply-template-update", ApplyRequest(version2, 1));

        update.Status.Should().Be(HttpStatusCode.Forbidden);
        apply.Status.Should().Be(HttpStatusCode.Forbidden);
        var stored = await owner.GetAsync($"/v1/jobs/{job.Id()}");
        stored.At("workflowVersion").GetInt32().Should().Be(1);
        stored.Text("workflow.stages.1.name").Should().Be("Screening");
    }

    // A complete job body, so the job publishes whatever posting defaults the template has (the basic one has none).
    private static async Task<ApiResult> CreateTemplateJobAsync(Tenant recruiter, Guid templateVersionId)
    {
        var body = Samples.RemoteJob("Engineer");
        body["templateVersionId"] = templateVersionId;
        return await recruiter.CreateJobAsync(body);
    }

    /// <summary>Starts version 2 of the template as a copy of version 1 with one stage renamed, publishes it, and returns its id.</summary>
    private static async Task<Guid> PublishVersion2Async(Tenant recruiter, Guid templateId, string stageId, string stageName)
    {
        var next = Samples.DefinitionWithRenamedStage(await Samples.DefinitionOfAsync(recruiter, templateId, 1), stageId, stageName);
        (await recruiter.PostAsync($"/v1/templates/{templateId}/versions", new JsonObject { ["definition"] = next })).Expect(HttpStatusCode.Created);
        return (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/2/publish")).Expect(HttpStatusCode.OK).Id();
    }

    /// <summary>The job's workflow as an editable copy.</summary>
    private static JsonObject WorkflowOf(ApiResult job) => JsonNode.Parse(job.At("workflow").GetRawText())!.AsObject();

    private static JsonObject WorkflowWithoutStage(ApiResult job, string stageId)
    {
        var workflow = WorkflowOf(job);
        var stages = workflow["stages"]!.AsArray();
        stages.Remove(stages.Single(s => s!["id"]!.GetValue<string>() == stageId));
        return workflow;
    }

    private static void SetStageName(JsonObject workflow, string stageId, string name) =>
        workflow["stages"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == stageId)!["name"] = name;

    private static JsonObject WorkflowRequest(JsonObject workflow, int expectedWorkflowVersion) =>
        new() { ["workflow"] = workflow.DeepClone(), ["expectedWorkflowVersion"] = expectedWorkflowVersion };

    private static JsonObject ApplyRequest(Guid templateVersionId, int expectedWorkflowVersion) =>
        new() { ["templateVersionId"] = templateVersionId, ["expectedWorkflowVersion"] = expectedWorkflowVersion };

    // Nobody can join with another role through the API yet, so the membership is written as the application role with
    // the organization as tenant, as HiringRolesTests does.
    private async Task<Tenant> ViewerOfAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        try
        {
            await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("viewer"), cancellationToken);
            var userId = (await session.CurrentUserAsync(cancellationToken))!.Value.GetProperty("id").GetGuid();
            var tenant = new TenantContext();
            tenant.SetOrganization(organizationId);
            await using (var db = await fixture.CreateDbContextAsync(tenant, cancellationToken))
            {
                db.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = organizationId,
                    UserId = userId,
                    Role = OrganizationRole.Viewer,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return new Tenant(session, organizationId, userId);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }
}
