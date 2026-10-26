using System.Reflection;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.UnitTests.History;

public sealed partial class TrackedKindsTests
{
    [Fact]
    public void Templates_jobs_roles_and_settings_are_tracked_and_nothing_that_holds_a_secret_is()
    {
        TrackedKinds.All.Select(k => k.EntityType).Should().BeEquivalentTo(
        [
            typeof(HiringTemplate), typeof(HiringTemplateVersion), typeof(EmailTemplate), typeof(Job), typeof(ScorecardTemplate), typeof(CustomRole),
            typeof(Organization), typeof(Organization), typeof(IntegritySettings),
        ]);
        TrackedKinds.All.Select(k => k.EntityType).Should().NotContain([typeof(ApiKey), typeof(WebhookEndpoint), typeof(OrganizationInvitation)]);
        TrackedKinds.Find(typeof(ApiKey)).Should().BeNull();
    }

    [Fact]
    public void No_tracked_entity_has_a_secret_like_property()
    {
        var secretLike = TrackedKinds.All
            .SelectMany(k => k.EntityType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(p => SecretLike().IsMatch(p.Name))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        secretLike.Should().BeEmpty("change history copies tracked values into its lines, and a secret must never be among them");
    }

    [Fact]
    public void A_version_row_belongs_to_its_templates_history()
    {
        var version = new HiringTemplateVersion { TemplateId = Guid.CreateVersion7(), Definition = HiringTemplateDefinition.Default };
        var kind = TrackedKinds.Find(typeof(HiringTemplateVersion))!;

        kind.Subject.Should().Be(ChangeSubjectKind.HiringTemplate);
        kind.SubjectId(version).Should().Be(version.TemplateId.ToString("D"));
    }

    [Fact]
    public void A_built_in_email_template_keeps_one_subject_id_whatever_its_row()
    {
        var reworded = EmailTemplate.Reword(Guid.CreateVersion7(), EmailTemplateIds.StageChange, "Subject", "Body", DateTimeOffset.UnixEpoch);

        TrackedKinds.Find(typeof(EmailTemplate))!.SubjectId(reworded).Should().Be(EmailTemplateIds.StageChange);
    }

    // A token budget counts a model's tokens, not a credential, so it is the one "Token" the pattern lets through.
    [GeneratedRegex("Secret|Hash|Token(?!Budget)|Password", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLike();

    [Fact]
    public void The_organization_row_gives_the_name_and_the_brand_colour_a_history_each()
    {
        var kinds = TrackedKinds.FindAll(typeof(Organization));

        kinds.Select(k => k.Subject).Should().Equal(ChangeSubjectKind.Organization, ChangeSubjectKind.Branding);
        TrackedKinds.FindAll(typeof(ApiKey)).Should().BeEmpty();
    }
}
