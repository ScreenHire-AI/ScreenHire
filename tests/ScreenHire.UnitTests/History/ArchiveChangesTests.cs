using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// Archiving a template and restoring it from the archive each make a History line, for every kind of template, so the
/// save gets a change record and moves Last updated (change-history.md).
/// </summary>
public sealed class ArchiveChangesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();

    [Fact]
    public void A_hiring_template_is_archived_and_restored()
    {
        var before = HiringTemplate.Create(Organization, "Summer interns", null, HiringTemplateDefinition.Default, Monday, null);
        var archived = HiringTemplate.Create(Organization, "Summer interns", null, HiringTemplateDefinition.Default, Monday, null);
        archived.Archive(Monday.AddDays(1));

        Sentences(HiringTemplateChanges.Template(before, archived)).Should().Equal("archived the template");
        Sentences(HiringTemplateChanges.Template(archived, before)).Should().Equal("restored the template from the archive");
    }

    [Fact]
    public void A_scorecard_is_archived_and_restored()
    {
        var before = Scorecard();
        var archived = Scorecard();
        archived.Archive(Monday.AddDays(1));

        Sentences(ScorecardTemplateChanges.Describe(before, archived)).Should().Equal("archived the scorecard");
        Sentences(ScorecardTemplateChanges.Describe(archived, before)).Should().Equal("restored the scorecard from the archive");
    }

    [Fact]
    public void An_email_template_is_archived_and_restored()
    {
        var before = EmailTemplate.CreateCustom(Organization, "Spring open day", "Meet {{ organizationName }}", "Hi", Monday);
        var archived = EmailTemplate.CreateCustom(Organization, "Spring open day", "Meet {{ organizationName }}", "Hi", Monday);
        archived.ArchivedAt = Monday.AddDays(1);

        Sentences(EmailTemplateChanges.Describe(before, archived)).Should().Equal("archived the template");
        Sentences(EmailTemplateChanges.Describe(archived, before)).Should().Equal("restored the template from the archive");
    }

    [Fact]
    public void An_archived_template_reads_only_until_it_is_restored()
    {
        var template = HiringTemplate.Create(Organization, "Summer interns", null, HiringTemplateDefinition.Default, Monday, null);
        template.Archive(Monday);
        template.Archive(Monday.AddDays(1));
        template.ArchivedAt.Should().Be(Monday, "archiving twice changes nothing");

        var rename = () => template.Rename("Winter interns", null, Monday);
        rename.Should().Throw<InvalidOperationException>();

        template.Unarchive(Monday.AddDays(2));
        template.Rename("Winter interns", null, Monday.AddDays(2));
        template.Name.Should().Be("Winter interns");

        var scorecard = Scorecard();
        scorecard.Archive(Monday);
        var update = () => scorecard.Update("Renamed", scorecard.Questions, Monday);
        update.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_system_template_is_never_archived()
    {
        var system = new HiringTemplate { Name = "Hourly role", CreatedAt = Monday };

        var archive = () => system.Archive(Monday);
        archive.Should().Throw<InvalidOperationException>();
    }

    private static ScorecardTemplate Scorecard() =>
        ScorecardTemplate.Create(Organization, "Technical interview", [new("problem", ScorecardQuestionType.Rating5, "Problem solving")], Monday);

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(l => l.Sentence());
}
