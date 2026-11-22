using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;
using static ScreenHire.IntegrationTests.Forms.FormSupport;

namespace ScreenHire.IntegrationTests.Forms;

/// <summary>
/// The form library (workflow-engine.md section 7, Forms): forms versioned like hiring templates, used by hiring templates
/// through a copy the API keeps equal to the library version it names, and a publish that offers to update the templates
/// that use an older version. Tenant-scoped: another organization's form is a 404.
/// </summary>
public sealed class FormLibraryTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_form_is_drafted_published_and_versioned_and_a_published_version_never_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var created = (await owner.PostAsync("/v1/form-templates", new JsonObject
        {
            ["content"] = Content("  Screening questionnaire  ", Question("start_date", "When could you start?", required: true)),
        })).Expect(HttpStatusCode.Created);
        var formId = created.Id();
        created.Text("name").Should().Be("Screening questionnaire");
        created.At("latestPublishedVersion").ValueKind.Should().Be(JsonValueKind.Null);
        created.At("versions.0.status").GetString().Should().Be("Draft");

        (await owner.PutAsync($"/v1/form-templates/{formId}/versions/1", new JsonObject { ["content"] = Content("Screening questionnaire") }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("content.questions");
        (await owner.PutAsync($"/v1/form-templates/{formId}/versions/1", new JsonObject
        {
            ["content"] = Content("Screening questionnaire", Question("start_date", "When could you start?"), Question("notice", "How much notice do you give?")),
        })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/form-templates/{formId}/versions/1/publish")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Published");
        (await owner.PutAsync($"/v1/form-templates/{formId}/versions/1", new JsonObject { ["content"] = Content("Changed", Question("x", "X?")) }))
            .Expect(HttpStatusCode.Conflict);

        var second = (await owner.PostAsync($"/v1/form-templates/{formId}/versions", new JsonObject())).Expect(HttpStatusCode.Created);
        second.At("version").GetInt32().Should().Be(2);
        second.Count("content.questions").Should().Be(2, "a new draft starts as a copy of the newest version");
        (await owner.PostAsync($"/v1/form-templates/{formId}/versions", new JsonObject())).Expect(HttpStatusCode.Conflict);

        var listed = (await owner.GetAsync("/v1/form-templates")).Expect(HttpStatusCode.OK);
        listed.At("equalityMonitoringEnabled").GetBoolean().Should().BeTrue("the test stacks turn it on");
        var summary = listed.At("forms").EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == formId);
        summary.GetProperty("latestPublishedVersion").GetInt32().Should().Be(1);
        summary.GetProperty("draftVersion").GetInt32().Should().Be(2);
        summary.GetProperty("questions").GetInt32().Should().Be(2);

        var history = (await owner.GetAsync($"/v1/form-templates/{formId}/history")).Expect(HttpStatusCode.OK);
        history.At("entries").EnumerateArray().SelectMany(e => e.GetProperty("lines").EnumerateArray())
            .Select(line => line.GetProperty("text").GetString())
            .Should().Contain(["created the form", "published version 1", "started version 2", "added How much notice do you give?"]);

        (await owner.PostAsync($"/v1/form-templates/{formId}/archive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeTrue();
        (await owner.PutAsync($"/v1/form-templates/{formId}/versions/2", new JsonObject { ["content"] = second.Body.GetProperty("content").Deserialize<JsonObject>() }))
            .Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/v1/form-templates/{formId}/unarchive")).Expect(HttpStatusCode.OK).At("archived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Another_organizations_form_is_not_found_a_viewer_reads_only_and_an_equality_form_asks_only_optional_choices()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var formId = await CreatePublishedFormAsync(owner, "Availability", "Standard", Question("start_date", "When could you start?"));

        (await other.GetAsync($"/v1/form-templates/{formId}")).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/form-templates/{formId}/versions", new JsonObject())).Expect(HttpStatusCode.NotFound);
        (await other.GetAsync("/v1/form-templates")).Expect(HttpStatusCode.OK).Count("forms").Should().Be(0);
        (await viewer.GetAsync($"/v1/form-templates/{formId}")).Expect(HttpStatusCode.OK);
        (await viewer.PostAsync("/v1/form-templates", new JsonObject { ["content"] = Content("Mine", Question("a", "A?")) }))
            .Expect(HttpStatusCode.Forbidden);

        var refused = (await owner.PostAsync("/v1/form-templates", new JsonObject
        {
            ["kind"] = "EqualityMonitoring",
            ["content"] = Content("Equality monitoring", Question("age", "How old are you?"), Question("disability", "Do you have a disability?", "YesNo", required: true)),
        })).Expect(HttpStatusCode.BadRequest);
        refused.ProblemKeys().Should().Equal("content.questions[0].type", "content.questions[1].required");
        (await owner.PostAsync("/v1/form-templates", new JsonObject
        {
            ["kind"] = "EqualityMonitoring",
            ["content"] = Content("Equality monitoring", Question("disability", "Do you have a disability?", "YesNo")),
        })).Expect(HttpStatusCode.Created).Text("kind").Should().Be("EqualityMonitoring");
    }

    [Fact]
    public async Task A_template_holds_the_library_versions_questions_and_cannot_name_a_form_that_is_not_published_in_the_library()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var formId = await CreatePublishedFormAsync(owner, "Availability", "Standard", Question("start_date", "When could you start?"));
        var foreign = await CreatePublishedFormAsync(other, "Not yours", "Standard", Question("secret", "Theirs?"));

        var template = await owner.CreateTemplateAsync("Uses the library", DefinitionUsing("availability", formId, 1, SendOnEntering("screening", "availability")));
        var templateId = template.Id();
        var copy = (await FollowUpsOfAsync(owner, templateId, 1)).Single()!;
        copy["name"]!.GetValue<string>().Should().Be("Availability", "the library's name, whatever the request said");
        copy["questions"]!.AsArray().Select(q => q!["id"]!.GetValue<string>()).Should().Equal("start_date");
        copy["formTemplateId"]!.GetValue<Guid>().Should().Be(formId);
        copy["formVersion"]!.GetValue<int>().Should().Be(1);

        var draft = (await owner.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject
        {
            ["definition"] = DefinitionUsing("availability", foreign, 1),
        })).Expect(HttpStatusCode.OK);
        draft.At("problems").EnumerateArray().Select(p => p.GetProperty("path").GetString())
            .Should().Contain("workflow.followUpQuestionnaires[0].formTemplateId", "another organization's form is not in this library");
        (await owner.PostAsync($"/v1/templates/{templateId}/versions/1/publish")).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("definition.workflow.followUpQuestionnaires[0].formTemplateId");

        (await owner.PostAsync($"/v1/form-templates/{formId}/versions", new JsonObject())).Expect(HttpStatusCode.Created);
        (await owner.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = DefinitionUsing("availability", formId, 2) }))
            .Expect(HttpStatusCode.OK).At("problems").EnumerateArray().Select(p => p.GetProperty("path").GetString())
            .Should().Contain("workflow.followUpQuestionnaires[0].formTemplateId", "a draft of a form is not for templates yet");
    }

    [Fact]
    public async Task Publishing_a_changed_form_updates_the_templates_that_ask_for_it_and_says_what_it_did_to_each()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var formId = await CreatePublishedFormAsync(owner, "Availability", "Standard", Question("start_date", "When could you start?"));
        var (published, _) = await owner.CreatePublishedTemplateAsync("Published only", DefinitionUsing("availability", formId, 1, SendOnEntering("screening", "availability")));
        var withDraft = (await owner.CreateTemplateAsync("Has a draft", DefinitionUsing("availability", formId, 1))).Id();
        var clashing = Samples.DefinitionWith();
        clashing["workflow"]!["questionnaire"]!["questions"] = new JsonArray(Question("notice", "Notice period on the application form?"));
        clashing["workflow"]!["followUpQuestionnaires"] = new JsonArray(LinkedFollowUp("availability", formId, 1));
        var (clash, _) = await owner.CreatePublishedTemplateAsync("Asks about notice already", clashing);
        var (unrelated, _) = await owner.CreatePublishedTemplateAsync("Uses no form");

        (await owner.PostAsync($"/v1/form-templates/{formId}/versions", new JsonObject
        {
            ["content"] = Content("Availability", Question("start_date", "When could you start?"), Question("notice", "How much notice do you give?")),
        })).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/form-templates/{formId}/versions/2/publish")).Expect(HttpStatusCode.OK);
        var usage = (await owner.GetAsync("/v1/template-usage")).Expect(HttpStatusCode.OK).At("forms").EnumerateArray()
            .Single(u => u.GetProperty("templateId").GetString() == formId.ToString("D"));
        usage.GetProperty("usedBy").EnumerateArray().Select(u => (u.GetProperty("name").GetString(), u.GetProperty("templateVersion").GetInt32()))
            .Should().BeEquivalentTo([("Published only", 1), ("Has a draft", 1), ("Asks about notice already", 1)]);

        var result = (await owner.PostAsync($"/v1/form-templates/{formId}/versions/2/apply-to-templates", new JsonObject
        {
            ["templateIds"] = new JsonArray(published, withDraft, clash, unrelated),
        })).Expect(HttpStatusCode.OK);
        var outcomes = result.At("templates").EnumerateArray().ToDictionary(t => t.GetProperty("templateId").GetGuid(), t => t);
        outcomes[published].GetProperty("outcome").GetString().Should().Be("Published");
        outcomes[published].GetProperty("version").GetInt32().Should().Be(2);
        outcomes[withDraft].GetProperty("outcome").GetString().Should().Be("UpdatedDraft");
        outcomes[clash].GetProperty("outcome").GetString().Should().Be("NotUpdated");
        outcomes[clash].GetProperty("problem").GetString().Should().Contain("notice", "its application form asks a question with the same id");
        outcomes[unrelated].GetProperty("outcome").GetString().Should().Be("NotUpdated");

        var live = (await owner.GetAsync($"/v1/templates/{published}")).Expect(HttpStatusCode.OK);
        live.At("latestPublishedVersion").GetInt32().Should().Be(2, "new jobs get the form's new questions");
        var copy = (await FollowUpsOfAsync(owner, published, 2)).Single()!;
        copy["formVersion"]!.GetValue<int>().Should().Be(2);
        copy["questions"]!.AsArray().Should().HaveCount(2);
        (await Samples.DefinitionOfAsync(owner, published, 2))["workflow"]!["automations"]!.AsArray().Select(a => a!["id"]!.GetValue<string>())
            .Should().Equal(["send-form-availability"], "the automation that sends it is kept");
        (await FollowUpsOfAsync(owner, withDraft, 1)).Single()!["formVersion"]!.GetValue<int>().Should().Be(2);
        (await owner.GetAsync($"/v1/templates/{withDraft}")).Expect(HttpStatusCode.OK).At("latestPublishedVersion").ValueKind
            .Should().Be(JsonValueKind.Null, "a draft waits for its own publish");
    }

    [Fact]
    public async Task Copying_a_ScreenHire_template_makes_its_forms_library_forms_of_the_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var copied = (await owner.PostAsync("/v1/templates", new JsonObject
        {
            ["name"] = "Our cafe",
            ["copyFromVersionId"] = SystemTemplates.HospitalityVersion1,
        })).Expect(HttpStatusCode.Created);

        var followUps = await FollowUpsOfAsync(owner, copied.Id(), 1);
        followUps.Should().NotBeEmpty();
        var forms = (await owner.GetAsync("/v1/form-templates")).Expect(HttpStatusCode.OK).At("forms").EnumerateArray().ToList();
        foreach (var followUp in followUps)
        {
            var formId = followUp!["formTemplateId"]!.GetValue<Guid>();
            forms.Should().ContainSingle(f => f.GetProperty("id").GetGuid() == formId && f.GetProperty("name").GetString() == followUp["name"]!.GetValue<string>()
                && f.GetProperty("latestPublishedVersion").GetInt32() == 1);
        }
    }
}
