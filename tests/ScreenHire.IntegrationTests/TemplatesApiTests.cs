using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.IntegrationTests;

/// <summary>The hiring template endpoints (<c>/v1/templates</c>), against the shared AppHost. Each test has its own organization.</summary>
public sealed class TemplatesApiTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_new_organization_lists_exactly_the_two_system_templates()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var list = (await recruiter.GetAsync("/v1/templates")).Expect(HttpStatusCode.OK);

        list.Count().Should().Be(2);
        var byName = Enumerable.Range(0, 2).ToDictionary(i => list.Text($"{i}.name"), i => list.At($"{i}"));
        byName.Keys.Should().BeEquivalentTo("Software engineer", "Hourly role");
        byName["Software engineer"].GetProperty("id").GetGuid().Should().Be(SystemTemplates.SoftwareEngineer);
        byName["Software engineer"].GetProperty("latestPublishedVersionId").GetGuid().Should().Be(SystemTemplates.SoftwareEngineerVersion2);
        byName["Hourly role"].GetProperty("id").GetGuid().Should().Be(SystemTemplates.HourlyRole);
        byName["Hourly role"].GetProperty("latestPublishedVersionId").GetGuid().Should().Be(SystemTemplates.HourlyRoleVersion1);
        byName.Values.Should().OnlyContain(t => t.GetProperty("isSystem").GetBoolean());
        byName["Software engineer"].GetProperty("latestPublishedVersion").GetInt32().Should().Be(2);
        byName["Hourly role"].GetProperty("latestPublishedVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Own_templates_are_listed_after_the_system_templates()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        await recruiter.CreateTemplateAsync("Aardvark pipeline");

        var list = (await recruiter.GetAsync("/v1/templates")).Expect(HttpStatusCode.OK);

        list.Count().Should().Be(3);
        list.Text("2.name").Should().Be("Aardvark pipeline", "system templates come first even when an own name sorts earlier");
        list.At("2.isSystem").GetBoolean().Should().BeFalse();
        list.At("2.draftVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Creating_a_template_without_a_definition_starts_a_draft_version_one_with_the_basic_pipeline()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var created = await recruiter.CreateTemplateAsync("Basic pipeline");

        created.Text("name").Should().Be("Basic pipeline");
        created.At("isSystem").GetBoolean().Should().BeFalse();
        created.Count("versions").Should().Be(1);
        created.At("versions.0.version").GetInt32().Should().Be(1);
        created.Text("versions.0.status").Should().Be("Draft");
        var version = (await recruiter.GetAsync($"/v1/templates/{created.Id()}/versions/1")).Expect(HttpStatusCode.OK);
        version.Text("status").Should().Be("Draft");
        version.Text("definition.workflow.stages.0.kind").Should().Be("Applied");
        version.Count("definition.workflow.stages").Should().Be(7);
    }

    [Fact]
    public async Task Copying_a_system_version_starts_a_draft_with_the_same_workflow()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var created = (await recruiter.PostAsync("/v1/templates", new JsonObject
        {
            ["name"] = "Our engineers",
            ["copyFromVersionId"] = SystemTemplates.SoftwareEngineerVersion1,
        })).Expect(HttpStatusCode.Created);

        var copy = await recruiter.GetAsync($"/v1/templates/{created.Id()}/versions/1");
        var system = await recruiter.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/1");
        copy.Text("status").Should().Be("Draft");
        copy.At("definition.workflow").GetRawText().Should().Be(system.At("definition.workflow").GetRawText());
        copy.Text("definition.postingDefaults.department").Should().Be("Engineering");
        created.At("isSystem").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_definition_that_breaks_a_structural_rule_is_rejected_with_definition_keys()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var firstStageNotApplied = Samples.Definition();
        firstStageNotApplied["workflow"]!["stages"]![0]!["kind"] = "Interview";
        var duplicateStageId = Samples.Definition();
        duplicateStageId["workflow"]!["stages"]![1]!["id"] = "applied";

        var kind = await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Bad kind", ["definition"] = firstStageNotApplied });
        var duplicate = await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Bad id", ["definition"] = duplicateStageId });

        kind.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("definition.workflow.stages[0].kind");
        duplicate.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("definition.workflow.stages[1].id");
        duplicate.ProblemKeys().Should().OnlyContain(k => k.StartsWith("definition.", StringComparison.Ordinal));
        (await recruiter.GetAsync("/v1/templates")).Count().Should().Be(2, "rejected definitions save nothing");
    }

    [Fact]
    public async Task A_definition_and_a_version_to_copy_together_are_rejected()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var both = await recruiter.PostAsync("/v1/templates", new JsonObject
        {
            ["name"] = "Both",
            ["definition"] = Samples.Definition(),
            ["copyFromVersionId"] = SystemTemplates.SoftwareEngineerVersion1,
        });
        var unknown = await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Unknown", ["copyFromVersionId"] = Guid.NewGuid() });

        both.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("copyFromVersionId");
        unknown.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("copyFromVersionId");
    }

    [Theory]
    [InlineData("Reject")]
    [InlineData("QueueForRejectionReview")]
    public async Task An_integrity_signal_cannot_drive_a_rejection(string actionType)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var action = new JsonObject { ["type"] = actionType, ["reason"] = "Not a fit" };
        var definition = Samples.DefinitionWith(Samples.IntegrityAutomation(action));

        var create = await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Auto reject", ["definition"] = definition.DeepClone() });
        var draft = await recruiter.CreateTemplateAsync("Draft to edit");
        var update = await recruiter.PutAsync($"/v1/templates/{draft.Id()}/versions/1", new JsonObject { ["definition"] = definition.DeepClone() });

        create.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("definition.workflow.automations[0].actions[0]");
        update.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("definition.workflow.automations[0].actions[0]");
        create.At("errors").EnumerateObject().Single().Value[0].GetString().Should().Contain("Integrity signals never drive rejection");
    }

    [Fact]
    public async Task An_integrity_signal_may_still_route_a_candidate_for_review()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var action = new JsonObject { ["type"] = "AddTag", ["tag"] = "needs-review" };

        var created = await recruiter.PostAsync(
            "/v1/templates", new JsonObject { ["name"] = "Routing", ["definition"] = Samples.DefinitionWith(Samples.IntegrityAutomation(action)) });

        created.Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_tag_added_by_an_integrity_driven_automation_cannot_launder_the_signal_into_another_automation()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        JsonObject AddTag() => Samples.IntegrityAutomation(new JsonObject { ["type"] = "AddTag", ["tag"] = "needs-review" });

        var laundered = await recruiter.PostAsync(
            "/v1/templates", new JsonObject { ["name"] = "Laundering", ["definition"] = Samples.DefinitionWith(AddTag(), Samples.EmailOnTag("needs-review")) });
        var unrelated = await recruiter.PostAsync(
            "/v1/templates", new JsonObject { ["name"] = "Unrelated tag", ["definition"] = Samples.DefinitionWith(AddTag(), Samples.EmailOnTag("referral")) });

        laundered.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("definition.workflow.automations[1].actions[0]");
        unrelated.Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Publishing_a_draft_makes_it_a_published_version_and_publishing_again_changes_nothing()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var template = await recruiter.CreateTemplateAsync("To publish");

        var published = (await recruiter.PostAsync($"/v1/templates/{template.Id()}/versions/1/publish")).Expect(HttpStatusCode.OK);
        var stored = (await recruiter.GetAsync($"/v1/templates/{template.Id()}/versions/1")).Expect(HttpStatusCode.OK);
        var again = (await recruiter.PostAsync($"/v1/templates/{template.Id()}/versions/1/publish")).Expect(HttpStatusCode.OK);

        published.Text("status").Should().Be("Published");
        published.Text("publishedBy").Should().Be(recruiter.UserId.ToString());
        published.At("publishedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        again.Text("publishedAt").Should().Be(stored.Text("publishedAt"), "a repeat publish does not restamp the version");
        var detail = await recruiter.GetAsync($"/v1/templates/{template.Id()}");
        detail.At("latestPublishedVersion").GetInt32().Should().Be(1);
        detail.Count("versions").Should().Be(1);
    }

    [Fact]
    public async Task A_published_version_cannot_be_edited()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, _) = await recruiter.CreatePublishedTemplateAsync("Frozen");
        var changed = Samples.DefinitionWithRenamedStage(Samples.Definition(), "screening", "Changed");

        var update = await recruiter.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = changed });

        update.Expect(HttpStatusCode.Conflict);
        var stored = await Samples.DefinitionOfAsync(recruiter, templateId, 1);
        stored["workflow"]!["stages"]![1]!["name"]!.GetValue<string>().Should().Be("Screening");
    }

    [Fact]
    public async Task Renaming_a_template_keeps_its_versions()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, _) = await recruiter.CreatePublishedTemplateAsync("Old name");

        var renamed = (await recruiter.PutAsync($"/v1/templates/{templateId}", new JsonObject { ["name"] = "New name", ["description"] = "For field roles." }))
            .Expect(HttpStatusCode.OK);

        renamed.Text("name").Should().Be("New name");
        renamed.Text("description").Should().Be("For field roles.");
        renamed.Count("versions").Should().Be(1);
        (await recruiter.PutAsync($"/v1/templates/{templateId}", new JsonObject { ["name"] = "" })).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task System_templates_are_read_only()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var definition = new JsonObject { ["definition"] = Samples.Definition() };

        var rename = await recruiter.PutAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}", new JsonObject { ["name"] = "Mine now" });
        var newVersion = await recruiter.PostAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions");
        var edit = await recruiter.PutAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/1", definition);
        var publish = await recruiter.PostAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/1/publish");

        rename.Expect(HttpStatusCode.Conflict);
        newVersion.Expect(HttpStatusCode.Conflict);
        edit.Expect(HttpStatusCode.Conflict);
        publish.Expect(HttpStatusCode.Conflict);
        var after = await recruiter.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}");
        after.Text("name").Should().Be("Software engineer");
        after.Count("versions").Should().Be(2, "version 1 and the version 2 a later migration shipped");
    }

    [Fact]
    public async Task Unknown_templates_and_versions_are_not_found()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, _) = await recruiter.CreatePublishedTemplateAsync("Known");
        var missing = Guid.NewGuid();

        (await recruiter.GetAsync($"/v1/templates/{missing}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync($"/v1/templates/{templateId}/versions/9")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/9/publish")).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.PostAsync($"/v1/templates/{missing}/versions")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Template_endpoints_need_a_signed_in_user()
    {
        using var visitor = new ApiSession(fixture);
        var cancellationToken = TestContext.Current.CancellationToken;

        using var list = await visitor.Client.GetAsync("/v1/templates", cancellationToken);
        using var create = await visitor.PostAsync("/v1/templates", new { name = "Nope" }, cancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_draft_version_can_be_edited_and_a_template_has_at_most_one_draft()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var (templateId, _) = await recruiter.CreatePublishedTemplateAsync("Versioned");
        var nextDefinition = Samples.DefinitionWithRenamedStage(Samples.Definition(), "interview", "Panel interview");

        var version2 = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions")).Expect(HttpStatusCode.Created);
        var second = await recruiter.PostAsync($"/v1/templates/{templateId}/versions");
        var edited = (await recruiter.PutAsync($"/v1/templates/{templateId}/versions/2", new JsonObject { ["definition"] = nextDefinition })).Expect(HttpStatusCode.OK);
        var invalid = Samples.Definition();
        invalid["workflow"]!["stages"] = new JsonArray();
        var rejected = await recruiter.PutAsync($"/v1/templates/{templateId}/versions/2", new JsonObject { ["definition"] = invalid });
        var published = (await recruiter.PostAsync($"/v1/templates/{templateId}/versions/2/publish")).Expect(HttpStatusCode.OK);

        version2.At("version").GetInt32().Should().Be(2);
        version2.Text("status").Should().Be("Draft");
        version2.Text("definition.workflow.stages.2.name").Should().Be("Interview", "a new draft starts as a copy of the newest version");
        second.Expect(HttpStatusCode.Conflict);
        edited.Text("definition.workflow.stages.2.name").Should().Be("Panel interview");
        rejected.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("definition.workflow.stages");
        published.Text("status").Should().Be("Published");
        (await recruiter.GetAsync($"/v1/templates/{templateId}")).At("latestPublishedVersion").GetInt32().Should().Be(2);
        (await recruiter.GetAsync($"/v1/templates/{templateId}/versions/1")).Text("definition.workflow.stages.2.name")
            .Should().Be("Interview", "version 1 is untouched by version 2");
    }
}
