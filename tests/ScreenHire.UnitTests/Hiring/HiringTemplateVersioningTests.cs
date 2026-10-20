using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

public sealed class HiringTemplateVersioningTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    [Fact]
    public void A_new_template_starts_with_a_draft_version_1()
    {
        var template = HiringTemplate.Create(Organization, "Warehouse", "Shift roles", HiringTemplateDefinition.Default, Monday, Recruiter);

        template.Versions.Should().ContainSingle();
        template.Draft.Should().BeSameAs(template.Versions[0]);
        template.Draft!.Version.Should().Be(1);
        template.Draft.Status.Should().Be(TemplateVersionStatus.Draft);
        template.Draft.OrganizationId.Should().Be(Organization);
        template.Draft.CreatedBy.Should().Be(Recruiter);
        template.LatestPublishedVersion.Should().BeNull();
        template.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void Publishing_the_draft_makes_it_the_latest_published_version()
    {
        var template = NewTemplate();
        var draft = template.Draft!;

        template.Publish(draft, Monday.AddHours(1), Recruiter);

        draft.Status.Should().Be(TemplateVersionStatus.Published);
        draft.PublishedAt.Should().Be(Monday.AddHours(1));
        draft.PublishedBy.Should().Be(Recruiter);
        template.LatestPublishedVersion.Should().Be(1);
        template.Draft.Should().BeNull();
    }

    [Fact]
    public void A_draft_can_be_revised()
    {
        var template = NewTemplate();
        var changed = Changed();

        template.Draft!.Revise(changed);

        template.Draft.Definition.Should().BeSameAs(changed);
    }

    [Fact]
    public void A_published_version_is_immutable()
    {
        var template = NewTemplate();
        var version = template.Draft!;
        template.Publish(version, Monday, Recruiter);

        var revise = () => version.Revise(Changed());
        var publishAgain = () => template.Publish(version, Monday.AddDays(1), Recruiter);

        revise.Should().Throw<InvalidOperationException>().WithMessage("*published and cannot change*");
        publishAgain.Should().Throw<InvalidOperationException>().WithMessage("*already published*");
        version.Definition.Should().Be(HiringTemplateDefinition.Default);
        version.PublishedAt.Should().Be(Monday);
    }

    [Fact]
    public void Edits_after_publishing_go_into_the_next_version()
    {
        var template = NewTemplate();
        template.Publish(template.Draft!, Monday, Recruiter);

        var next = template.CreateDraft(Changed(), Monday.AddDays(1), Recruiter);
        template.Publish(next, Monday.AddDays(2), Recruiter);

        next.Version.Should().Be(2);
        template.Versions.Select(v => (v.Version, v.Status)).Should().Equal(
            (1, TemplateVersionStatus.Published), (2, TemplateVersionStatus.Published));
        template.LatestPublishedVersion.Should().Be(2);
    }

    [Fact]
    public void A_template_has_at_most_one_draft()
    {
        var template = NewTemplate();

        var second = () => template.CreateDraft(Changed(), Monday, Recruiter);

        second.Should().Throw<InvalidOperationException>().WithMessage("*still a draft*");
        template.Versions.Should().ContainSingle();
    }

    [Fact]
    public void A_version_of_another_template_cannot_be_published_here()
    {
        var template = NewTemplate();
        var other = NewTemplate();

        var publish = () => template.Publish(other.Draft!, Monday, Recruiter);

        publish.Should().Throw<ArgumentException>();
        other.Draft!.Status.Should().Be(TemplateVersionStatus.Draft);
    }

    [Fact]
    public void A_system_template_is_read_only()
    {
        var system = new HiringTemplate { Name = "Software engineer", CreatedAt = Monday };

        var draft = () => system.CreateDraft(Changed(), Monday, Recruiter);
        var rename = () => system.Rename("Mine now", null, Monday);

        system.IsSystem.Should().BeTrue();
        draft.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");
        rename.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");
    }

    private static HiringTemplate NewTemplate() =>
        HiringTemplate.Create(Organization, "Warehouse", null, HiringTemplateDefinition.Default, Monday, Recruiter);

    private static HiringTemplateDefinition Changed() =>
        HiringTemplateDefinition.Default with { PostingDefaults = new PostingDefaults(Department: "Operations") };
}
