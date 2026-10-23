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
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion1, 1, "Hourly role" },
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion2, 2, "Hourly role" },
        { SystemTemplates.HourlyRole, SystemTemplates.HourlyRoleVersion3, 3, "Hourly role" },
    };

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
