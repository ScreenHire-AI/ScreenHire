using System.Reflection;
using System.Text.RegularExpressions;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.TalentPool;
using ScreenHire.Domain.Webhooks;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.UnitTests.History;

public sealed partial class TrackedKindsTests
{
    [Fact]
    public void Templates_jobs_roles_settings_the_team_and_api_keys_are_tracked_and_webhook_endpoints_are_not()
    {
        TrackedKinds.All.Select(k => k.EntityType).Should().BeEquivalentTo(
        [
            typeof(HiringTemplate), typeof(HiringTemplateVersion), typeof(EmailTemplate), typeof(Job), typeof(ScorecardTemplate), typeof(CustomRole),
            typeof(Organization), typeof(Organization), typeof(IntegritySettings), typeof(TalentPoolSettings),
            typeof(OrganizationMember), typeof(OrganizationInvitation), typeof(ApiKey),
        ]);
        TrackedKinds.FindAll(typeof(WebhookEndpoint)).Should().BeEmpty("its row holds the signing secret (ADR-0016)");
    }

    [Fact]
    public void No_describer_sees_a_secret_like_property()
    {
        var secretLike = TrackedKinds.All
            .SelectMany(k => k.ViewType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(p => SecretLike().IsMatch(p.Name))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        secretLike.Should().BeEmpty("change history copies what a describer sees into its lines, and a secret must never be among them");
    }

    [Fact]
    public void A_row_that_holds_a_secret_is_seen_only_through_a_view_without_it()
    {
        TrackedKinds.FindAll(typeof(ApiKey)).Single().ViewType.Should().Be<ApiKeyFacts>();
        TrackedKinds.FindAll(typeof(OrganizationInvitation)).Single().ViewType.Should().Be<InvitationFacts>();
        typeof(ApiKeyFacts).GetProperties().Select(p => p.Name).Should().NotContain([nameof(ApiKey.SecretHash), nameof(ApiKey.Prefix)]);
        typeof(InvitationFacts).GetProperties().Select(p => p.Name).Should().NotContain(nameof(OrganizationInvitation.TokenHash));
    }

    [Fact]
    public void Adding_a_template_job_or_role_creates_it_and_a_built_in_emails_first_edit_does_not()
    {
        var context = new ChangeContext(null!, null!);
        var custom = EmailTemplate.CreateCustom(Guid.CreateVersion7(), "Offer", "Subject", "Body", DateTimeOffset.UnixEpoch);
        var reworded = EmailTemplate.Reword(Guid.CreateVersion7(), EmailTemplateIds.StageChange, "Subject", "Body", DateTimeOffset.UnixEpoch);
        var email = TrackedKinds.FindAll(typeof(EmailTemplate)).Single();

        email.Creates(custom, context).Should().BeTrue();
        email.Creates(reworded, context).Should().BeFalse("it is an edit of ScreenHire's wording");
        TrackedKinds.FindAll(typeof(HiringTemplateVersion)).Single()
            .Creates(new HiringTemplateVersion { Definition = HiringTemplateDefinition.Default }, context).Should().BeFalse();
        TrackedKinds.FindAll(typeof(Job)).Single().Creates(new Job { Title = "Data engineer", Slug = "data-engineer" }, context).Should().BeTrue();
    }

    [Fact]
    public void A_version_row_belongs_to_its_templates_history()
    {
        var version = new HiringTemplateVersion { TemplateId = Guid.CreateVersion7(), Definition = HiringTemplateDefinition.Default };
        var kind = TrackedKinds.FindAll(typeof(HiringTemplateVersion)).Single();

        kind.Subject.Should().Be(ChangeSubjectKind.HiringTemplate);
        kind.SubjectId(version).Should().Be(version.TemplateId.ToString("D"));
    }

    [Fact]
    public void A_built_in_email_template_keeps_one_subject_id_whatever_its_row()
    {
        var reworded = EmailTemplate.Reword(Guid.CreateVersion7(), EmailTemplateIds.StageChange, "Subject", "Body", DateTimeOffset.UnixEpoch);

        TrackedKinds.FindAll(typeof(EmailTemplate)).Single().SubjectId(reworded).Should().Be(EmailTemplateIds.StageChange);
    }

    // A token budget counts a model's tokens, not a credential, so it is the one "Token" the pattern lets through. An API
    // key's prefix is not secret, yet it is part of the key, so it never enters a change either.
    [GeneratedRegex("Secret|Hash|Token(?!Budget)|Password|Prefix", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLike();

    [Fact]
    public void The_organization_row_gives_the_name_and_the_brand_colour_a_history_each()
    {
        var kinds = TrackedKinds.FindAll(typeof(Organization));

        kinds.Select(k => k.Subject).Should().Equal(ChangeSubjectKind.Organization, ChangeSubjectKind.Branding);
    }
}
