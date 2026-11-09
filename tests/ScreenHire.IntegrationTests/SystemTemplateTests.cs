using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.IntegrationTests;

/// <summary>The system templates the migration ships are valid under today's rules, and jobs can be made from them and published.</summary>
public sealed class SystemTemplateTests(AppHostFixture fixture)
{
    public static TheoryData<Guid, Guid, int, string> Templates => new()
    {
        { SystemTemplates.SoftwareEngineer, SystemTemplates.SoftwareEngineerVersion1, 1, "Software engineer" },
        { SystemTemplates.SoftwareEngineer, SystemTemplates.SoftwareEngineerVersion2, 2, "Software engineer" },
        { SystemTemplates.SoftwareEngineer, SystemTemplates.SoftwareEngineerVersion3, 3, "Software engineer" },
        { SystemTemplates.SoftwareEngineer, SystemTemplates.SoftwareEngineerVersion4, 4, "Software engineer" },
        { SystemTemplates.SoftwareEngineer, SystemTemplates.SoftwareEngineerVersion5, 5, "Software engineer" },
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion1, 1, "Hourly role" },
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion2, 2, "Hourly role" },
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion3, 3, "Hourly role" },
        { SystemTemplates.OfficeRole, SystemTemplates.OfficeRoleVersion1, 1, "Office role" },
        { SystemTemplates.TradeRole, SystemTemplates.TradeRoleVersion1, 1, "Trade role with a trial shift" },
        { SystemTemplates.HighVolume, SystemTemplates.HighVolumeVersion1, 1, "High volume" },
    };

    public static TheoryData<Guid> StartingPoints => new() { SystemTemplates.OfficeRole, SystemTemplates.TradeRole, SystemTemplates.HighVolume };

    // The three starting points of the new template screen: a copy of one is a draft a fresh organization can publish at
    // once (its emails are the built-in ones), and nothing in it turns anyone down.
    [Theory]
    [MemberData(nameof(StartingPoints))]
    public async Task A_starting_point_copies_into_a_draft_that_publishes_and_never_rejects(Guid templateId)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var system = (await recruiter.GetAsync($"/v1/templates/{templateId}/versions/1")).Expect(HttpStatusCode.OK);
        var definition = JsonSerializer.Deserialize<HiringTemplateDefinition>(system.At("definition").GetRawText(), JsonColumns.SerializerOptions)!;

        var copy = (await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Our copy", ["copyFromVersionId"] = system.Id() })).Expect(HttpStatusCode.Created);
        var draft = (await recruiter.GetAsync($"/v1/templates/{copy.Id()}/versions/1")).Expect(HttpStatusCode.OK);
        var published = (await recruiter.PostAsync($"/v1/templates/{copy.Id()}/versions/1/publish")).Expect(HttpStatusCode.OK);

        draft.At("problems").GetArrayLength().Should().Be(0);
        published.Text("status").Should().Be("Published");
        definition.Workflow.Automations.SelectMany(a => a.Actions).Should().NotContain(a => a.Type == ActionType.Reject);
        definition.Workflow.Automations.Should().NotContain(a => a.AllowAutoReject);
        definition.Workflow.Stages.Where(s => s.SlaDays is not null && !s.Kind.IsTerminal()).Should().OnlyContain(
            s => definition.Workflow.Automations.Any(a => a.Trigger.Type == TriggerType.SlaBreached && a.Trigger.StageId == s.Id),
            "every time limit has the reminder the stage sheet promises");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task A_system_template_version_deserializes_and_passes_the_definition_validator(Guid templateId, Guid versionId, int number, string name)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var version = (await recruiter.GetAsync($"/v1/templates/{templateId}/versions/{number}")).Expect(HttpStatusCode.OK);
        var definition = JsonSerializer.Deserialize<HiringTemplateDefinition>(version.At("definition").GetRawText(), JsonColumns.SerializerOptions);

        version.Id().Should().Be(versionId);
        version.Text("status").Should().Be("Published");
        (await recruiter.GetAsync($"/v1/templates/{templateId}")).Text("name").Should().Be(name);
        definition.Should().NotBeNull();
        DefinitionValidator.Validate(definition).Should().BeEmpty();
        definition!.Workflow.Stages[0].Kind.Should().Be(StageKind.Applied);
    }

    // Version 5 is version 4 with its verification automation saying verification (ADR-0019). Its id stays, because
    // automation ids feed idempotency keys, and every earlier version reads the renamed trigger and facts.
    [Fact]
    public async Task Version_5_names_its_verification_automation_and_tag_for_verification_and_keeps_its_id()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var four = (await recruiter.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/4")).Expect(HttpStatusCode.OK);
        var five = (await recruiter.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/versions/5")).Expect(HttpStatusCode.OK);

        five.Text("definition.workflow.automations.1.id").Should().Be("integrity_review");
        five.Text("definition.workflow.automations.1.name").Should().Be("Ask a reviewer to look at verification findings");
        five.Text("definition.workflow.automations.1.actions.1.tag").Should().Be("verification-review");
        five.Text("definition.workflow.automations.1.trigger.type").Should().Be("VerificationAssessed");
        four.Text("definition.workflow.automations.1.trigger.type").Should().Be("VerificationAssessed");
        four.Text("definition.workflow.automations.1.name").Should().Be("Ask a reviewer to look at integrity findings", "a published version keeps its words");
        five.At("definition.workflow.verificationPolicy").ValueKind.Should().Be(JsonValueKind.Object);
        five.At("definition.workflow.automations.1.conditions.rules").EnumerateArray().Select(r => r.GetProperty("fact").GetString())
            .Should().OnlyContain(fact => fact!.StartsWith("verification.", StringComparison.Ordinal));
    }

    // Every stored version names built-in scorecards (migration AddScorecardTemplates) and holds no copy of their questions.
    [Theory]
    [MemberData(nameof(Templates))]
    public async Task A_system_template_version_names_built_in_scorecards_and_holds_no_copy_of_them(Guid templateId, Guid versionId, int number, string name)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();

        var version = (await recruiter.GetAsync($"/v1/templates/{templateId}/versions/{number}")).Expect(HttpStatusCode.OK);
        var stages = version.At("definition.workflow.stages").EnumerateArray().ToList();
        var named = stages.Where(s => s.Has("scorecardTemplateId")).ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("scorecardTemplateId").GetString()!);

        version.Id().Should().Be(versionId);
        stages.Should().OnlyContain(s => !s.Has("scorecard"), $"{name} version {number} is a template, which names its scorecards");
        named.Values.Should().OnlyContain(id => BuiltInScorecards.Find(id) != null, "a system template can only name ScreenHire's own scorecards");
        string[] expected = (templateId, number) switch
        {
            _ when templateId == SystemTemplates.OfficeRole => ["phone-screen", "interview"],
            _ when templateId == SystemTemplates.TradeRole => ["phone-screen"],
            _ when templateId == SystemTemplates.HighVolume => ["screening"],
            _ when templateId == SystemTemplates.SoftwareEngineer && number >= 3 => ["screening", "technical-exercise", "engineering-interview"],
            _ when templateId == SystemTemplates.HourlyRole && number >= 2 => ["phone-screen", "hourly-interview"],
            _ => [],
        };
        named.Values.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task A_job_made_from_a_system_template_can_be_published_once_it_has_a_place(Guid templateId, Guid versionId, int number, string name)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = new JsonObject
        {
            ["title"] = $"{name} opening",
            ["templateVersionId"] = versionId,
            ["locations"] = new JsonArray(new JsonObject { ["country"] = "GB", ["city"] = "Leeds" }),
            ["applicantLocationRequirements"] = new JsonArray("GB"),
        };

        var job = await recruiter.CreateJobAsync(body);
        var published = await recruiter.PublishJobAsync(job.Id());

        job.Text("template.templateId").Should().Be(templateId.ToString());
        job.At("template.version").GetInt32().Should().Be(number);
        published.Text("status").Should().Be("Open");
        published.At("validThrough").ValueKind.Should().Be(JsonValueKind.String, "both templates set a posting duration");
        published.At("hasUnpublishedChanges").GetBoolean().Should().BeFalse("the job still equals the version just frozen");
        var application = await recruiter.CreateApplicationAsync(job.Id(), "casey.morgan@example.com");
        application.Text("stage.kind").Should().Be("Applied");
    }
}
