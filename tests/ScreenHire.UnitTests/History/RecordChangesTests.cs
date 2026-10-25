using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.History;

public sealed class RecordChangesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(l => l.Sentence());

    [Fact]
    public void A_template_is_created_from_its_source_renamed_and_its_versions_started_and_published()
    {
        var template = HiringTemplate.Create(Organization, "Tech interview", null, HiringTemplateDefinition.Default, Monday, null);
        var renamed = HiringTemplate.Create(Organization, "Technical interview", "How we interview", HiringTemplateDefinition.Default, Monday, null);

        Sentences(HiringTemplateChanges.Template(null, template, "Software engineer")).Should().Equal("created the template from Software engineer");
        Sentences(HiringTemplateChanges.Template(null, template)).Should().Equal("created the template");
        Sentences(HiringTemplateChanges.Template(template, renamed))
            .Should().Equal("renamed the template from Tech interview to Technical interview", "changed the description");

        var version1 = template.Versions[0];
        HiringTemplateChanges.Version(null, version1, addedWithTemplate: true).Should().BeEmpty("created the template says it");

        var draft = new HiringTemplateVersion { OrganizationId = Organization, TemplateId = template.Id, Version = 2, Definition = HiringTemplateDefinition.Default };
        Sentences(HiringTemplateChanges.Version(null, draft, HiringTemplateDefinition.Default)).Should().Equal("started version 2");

        // The draft as it was loaded, against the same row once published.
        var asLoaded = new HiringTemplateVersion { OrganizationId = Organization, TemplateId = template.Id, Version = 1, Definition = version1.Definition };
        template.Publish(version1, Monday, null);
        Sentences(HiringTemplateChanges.Version(asLoaded, version1)).Should().Equal("published version 1");
    }

    [Fact]
    public void A_template_row_that_only_follows_its_versions_says_nothing()
    {
        var template = HiringTemplate.Create(Organization, "Warehouse", null, HiringTemplateDefinition.Default, Monday, null);
        var later = HiringTemplate.Create(Organization, "Warehouse", null, HiringTemplateDefinition.Default, Monday, null);
        later.Publish(later.Versions[0], Monday.AddHours(1), null);

        HiringTemplateChanges.Template(template, later).Should().BeEmpty("UpdatedAt and LatestPublishedVersion follow the versions");
    }

    [Fact]
    public void A_built_in_email_template_compares_against_ScreenHire_wording_and_restoring_says_so()
    {
        var original = ("An update on your application for {{ jobTitle }}", "Hi {{ firstName }}");
        var reworded = EmailTemplate.Reword(Organization, EmailTemplateIds.StageChange, "Next steps for {{ jobTitle }}", "Hi {{ firstName }}", Monday);

        var lines = EmailTemplateChanges.Describe(null, reworded, original);
        Sentences(lines).Should().Equal("changed the subject");
        lines[0].Details.Should().Equal(new ChangeDetail("Subject", "An update on your application for {{ jobTitle }}", "Next steps for {{ jobTitle }}"));

        Sentences(EmailTemplateChanges.Describe(reworded, null, original)).Should().Equal("restored the original wording");
    }

    [Fact]
    public void An_own_email_template_is_created_renamed_changed_and_deleted()
    {
        var created = EmailTemplate.CreateCustom(Organization, "Interview invitation", "Interview at {{ organizationName }}", "Hi", Monday);
        var edited = new EmailTemplate { Id = created.Id, OrganizationId = Organization, Name = "Interview invite", Subject = created.Subject, Body = "Hello", CreatedAt = Monday };

        Sentences(EmailTemplateChanges.Describe(null, created)).Should().Equal("created the template");
        Sentences(EmailTemplateChanges.Describe(created, edited)).Should().Equal("renamed the template from Interview invitation to Interview invite", "changed the message");
        Sentences(EmailTemplateChanges.Describe(created, null)).Should().Equal("deleted the template");
    }

    [Fact]
    public void A_job_says_how_it_was_created_and_what_its_edits_and_status_changes_were()
    {
        var job = NewJob();
        Sentences(JobChanges.Describe(null, job, "Software engineer")).Should().Equal("created the job from Software engineer");

        var edited = NewJob();
        edited.Revise(edited.Snapshot() with
        {
            Title = "Senior Backend Engineer",
            Department = "Payments",
            Description = "<p>New <em>text</em></p>",
            Salary = new JobSalary(75_000, 95_000, "GBP", SalaryPeriod.Year),
            ValidThrough = Monday.AddDays(30),
        }, Monday);
        var lines = JobChanges.Describe(job, edited);

        Sentences(lines).Should().Equal(
            "renamed the job from Backend Engineer to Senior Backend Engineer",
            "changed the description",
            "changed the department from Engineering to Payments",
            "changed the salary",
            "changed when the posting expires");
        lines[1].Details.Should().Equal(new ChangeDetail("Description", "Old text", "New text", ChangeValueKind.Paragraphs));
        lines[3].Details.Should().Equal(new ChangeDetail("Salary", "70,000 to 90,000 GBP a year", "75,000 to 95,000 GBP a year"));
        lines[4].Details![0].Kind.Should().Be(ChangeValueKind.Instant);
        lines[4].Details![0].After.Should().Be("2026-11-18T09:00:00.0000000+00:00");

        var published = NewJob();
        published.Publish(Monday);
        Sentences(JobChanges.Describe(NewJob(), published)).Should().Equal("published the job");
        var closed = NewJob();
        closed.Publish(Monday);
        closed.Close(Monday);
        Sentences(JobChanges.Describe(published, closed)).Should().Equal("closed the job");
    }

    [Fact]
    public void A_job_workflow_edit_uses_the_definition_lines()
    {
        var job = NewJob();
        var edited = NewJob();
        edited.ReviseWorkflow(edited.Workflow with { Questionnaire = new Questionnaire([new Question("visa", QuestionType.YesNo, "Do you need a visa?")]) }, Monday);

        Sentences(JobChanges.Describe(job, edited)).Should().Equal("added Do you need a visa?");
    }

    [Theory]
    [InlineData("<p>One</p><p>Two &amp; three</p>", "One\nTwo & three")]
    [InlineData("<h2>Role</h2><ul><li>C#</li><li>SQL</li></ul>", "Role\n\n- C#\n- SQL")]
    [InlineData("Line<br>break", "Line\nbreak")]
    [InlineData("   ", null)]
    public void Rich_text_reads_as_text(string html, string? text)
    {
        RichTextToPlain.Convert(html).Should().Be(text);
    }

    private static Job NewJob()
    {
        var job = new Job
        {
            OrganizationId = Organization,
            CreatedAt = Monday,
            Title = "Backend Engineer",
            Slug = "backend-engineer",
            Department = "Engineering",
            Description = "<p>Old <em>text</em></p>",
            WorkplaceType = WorkplaceType.Remote,
            EmploymentType = EmploymentType.FullTime,
            ApplicantLocationRequirements = ["GB"],
        };
        job.SetSalary(new JobSalary(70_000, 90_000, "GBP", SalaryPeriod.Year));
        return job;
    }
}
