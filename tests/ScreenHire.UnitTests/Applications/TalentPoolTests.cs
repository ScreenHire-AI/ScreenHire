using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.TalentPool;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.TalentPool;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Applications;

// The talent pool's rules (docs/architecture/talent-pool.md): keeping a candidate in mind ends the application without a
// rejection, only with the candidate's own permission, which runs out at the shorter of the period then and now.
public sealed class TalentPoolTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    [Fact]
    public void Keeping_a_candidate_in_mind_ends_the_application_without_moving_it_and_keeps_the_note()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday, 12));
        var interview = job.Workflow.FindStage("interview")!;
        application.MoveTo(interview, Monday, Recruiter, note: null);

        var kept = application.KeepInMind(interview, 12, Monday.AddDays(3), Recruiter, "  Strong runner-up  ");

        application.KeptInMindAt.Should().Be(Monday.AddDays(3));
        application.CurrentStageId.Should().Be("interview", "the candidate leaves the job where they stood, not through Rejected");
        application.HasEnded(interview).Should().BeTrue();
        kept.Type.Should().Be(ApplicationEventType.KeptInMind);
        kept.Data.Note.Should().Be("Strong runner-up");
        kept.ActorUserId.Should().Be(Recruiter);
    }

    [Fact]
    public void Only_an_open_application_with_a_permission_that_holds_can_be_kept()
    {
        var (withoutConsent, job) = Applied(consent: null);
        var applied = job.Workflow.FindStage("applied");
        FluentActions.Invoking(() => withoutConsent.KeepInMind(applied, 12, Monday, Recruiter, null))
            .Should().Throw<InvalidOperationException>().WithMessage("*permission*");

        var (expired, _) = Applied(new FutureRolesConsent(Monday, 6));
        FluentActions.Invoking(() => expired.KeepInMind(applied, 12, Monday.AddMonths(6), Recruiter, null))
            .Should().Throw<InvalidOperationException>("permission runs out at the end of the period");

        var (rejected, _) = Applied(new FutureRolesConsent(Monday, 12));
        var rejectedStage = job.Workflow.Stages.First(s => s.Kind == StageKind.Rejected);
        rejected.MoveTo(rejectedStage, Monday, Recruiter, "Not a fit");
        FluentActions.Invoking(() => rejected.KeepInMind(rejectedStage, 12, Monday, Recruiter, null))
            .Should().Throw<InvalidOperationException>().WithMessage("*ended*");

        var (twice, _) = Applied(new FutureRolesConsent(Monday, 12));
        twice.KeepInMind(applied, 12, Monday, Recruiter, null);
        FluentActions.Invoking(() => twice.KeepInMind(applied, 12, Monday, Recruiter, null)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Permission_runs_out_at_the_shorter_of_the_period_it_was_given_for_and_the_period_now()
    {
        var (application, _) = Applied(new FutureRolesConsent(Monday, 12));

        application.FutureRolesUntil(12).Should().Be(Monday.AddMonths(12));
        application.FutureRolesUntil(24).Should().Be(Monday.AddMonths(12), "a longer setting never extends a permission already given");
        application.FutureRolesUntil(6).Should().Be(Monday.AddMonths(6), "a shorter setting shortens it");
        application.AllowsFutureRoles(12, Monday.AddMonths(12).AddTicks(-1)).Should().BeTrue();
        application.AllowsFutureRoles(12, Monday.AddMonths(12)).Should().BeFalse();
    }

    [Fact]
    public void A_withdrawn_permission_ends_at_once_and_is_not_carried_to_a_new_application()
    {
        var (application, _) = Applied(new FutureRolesConsent(Monday, 12));
        application.CarriedFutureRoles().Should().Be(new FutureRolesConsent(Monday, 12), "adding them to a job keeps the clock");

        application.WithdrawFutureRoles(Monday.AddDays(2));
        application.WithdrawFutureRoles(Monday.AddDays(5));

        application.FutureRolesWithdrawnAt.Should().Be(Monday.AddDays(2), "withdrawing twice keeps the first time");
        application.FutureRolesUntil(12).Should().BeNull();
        application.CarriedFutureRoles().Should().BeNull();
    }

    [Fact]
    public void Moving_a_kept_application_brings_it_back_even_to_the_stage_it_was_in()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday, 12));
        var applied = job.Workflow.FindStage("applied")!;
        application.KeepInMind(applied, 12, Monday, Recruiter, null);

        var moved = application.MoveTo(applied, Monday.AddDays(1), Recruiter, note: null);

        moved.Should().NotBeNull();
        moved!.Type.Should().Be(ApplicationEventType.StageChanged);
        application.KeptInMindAt.Should().BeNull();
        application.HasEnded(applied).Should().BeFalse();
        application.MoveTo(applied, Monday.AddDays(2), Recruiter, note: null).Should().BeNull("an open application moving to its own stage changes nothing");
    }

    [Fact]
    public void A_pool_entry_needs_the_applications_own_kept_in_mind_entry()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday, 12));
        var (other, _) = Applied(new FutureRolesConsent(Monday, 12));
        var kept = application.KeepInMind(job.Workflow.FindStage("applied"), 12, Monday, Recruiter, "why");

        var entry = TalentPoolEntry.For(application, kept);

        entry.ApplicationId.Should().Be(application.Id);
        entry.CandidateId.Should().Be(application.CandidateId);
        entry.KeptEventId.Should().Be(kept.Id);
        entry.KeptByUserId.Should().Be(Recruiter);
        FluentActions.Invoking(() => TalentPoolEntry.For(other, kept)).Should().Throw<InvalidOperationException>();

        entry.Invited(job.Id, Monday.AddDays(2));
        entry.Invited(Guid.CreateVersion7(), Monday.AddDays(1));
        entry.LastInvitedJobId.Should().Be(job.Id, "an older invitation than the last changes nothing");
    }

    [Fact]
    public void The_period_is_six_to_twentyfour_months_and_twelve_by_default()
    {
        TalentPoolSettings.MonthsOf(null).Should().Be(12);
        TalentPoolSettings.IsAllowed(5).Should().BeFalse();
        TalentPoolSettings.IsAllowed(6).Should().BeTrue();
        TalentPoolSettings.IsAllowed(24).Should().BeTrue();
        TalentPoolSettings.IsAllowed(25).Should().BeFalse();
    }

    [Fact]
    public void Changing_the_period_says_so_in_the_history_and_a_first_save_of_the_default_says_nothing()
    {
        var organization = Guid.CreateVersion7();

        SettingsChanges.TalentPool(null, new TalentPoolSettings { OrganizationId = organization, PermissionMonths = 12 }).Should().BeEmpty();
        var line = SettingsChanges.TalentPool(
            new TalentPoolSettings { OrganizationId = organization, PermissionMonths = 12 },
            new TalentPoolSettings { OrganizationId = organization, PermissionMonths = 18 }).Should().ContainSingle().Subject;
        line.Should().BeEquivalentTo(ChangeLine.Of("changed how long permission for future roles lasts from 12 months to ", "18 months"));
    }

    [Fact]
    public void The_invitation_must_carry_the_job_link_and_the_way_to_stop_and_no_other_template_may()
    {
        var templates = new EmailTemplates();

        templates.Validate(EmailTemplateIds.TalentPoolInvitation, new("A new role", "Apply: {{ jobLink }}")).Should().ContainSingle()
            .Which.Message.Should().Contain("{{ removeLink }}");
        templates.Validate(EmailTemplateIds.TalentPoolInvitation, new("A new role", "Stop: {{ removeLink }}")).Should().ContainSingle()
            .Which.Message.Should().Contain("{{ jobLink }}");
        templates.Validate(EmailTemplateIds.TalentPoolInvitation, templates.Default(EmailTemplateIds.TalentPoolInvitation)).Should().BeEmpty();
        templates.Validate(EmailTemplateIds.StageChange, new("Update", "{{ jobLink }}")).Should().ContainSingle()
            .Which.Message.Should().Contain("There is no variable \"jobLink\"");
        EmailTemplateIds.IsActionOnly(EmailTemplateIds.TalentPoolInvitation).Should().BeTrue("a person never sends it as an ordinary email");
    }

    [Fact]
    public void The_invitation_shows_the_job_link_and_the_way_to_stop_as_links_the_candidate_can_follow()
    {
        var job = "https://acme.careers.example.com/jobs/support-engineer";
        var stop = "https://acme.careers.example.com/talent-pool/token-123";

        var email = new EmailTemplates().Render(EmailTemplateIds.TalentPoolInvitation, "sam@example.com",
            new EmailTemplateModel("Sam Rivera", "Support engineer", "Acme", FirstName: "Sam", JobLink: job, RemoveLink: stop));

        email.Subject.Should().Be("Acme would like you to apply for Support engineer");
        email.HtmlBody.Should().Contain($"<a href=\"{job}\"").And.Contain($"<a href=\"{stop}\"");
        email.TextBody.Should().Contain(job).And.Contain(stop);
    }

    [Fact]
    public void A_remove_link_reads_back_and_a_forged_or_questionnaire_token_does_not()
    {
        var provider = new EphemeralDataProtectionProvider();
        var careers = Options.Create(new CareersSiteOptions
        {
            Scheme = "https",
            CareersDomain = "careers.example.com",
            CareersHostPattern = "{tenant}.{CareersDomain}",
        });
        var links = new TalentPoolLinks(provider, careers);
        var (organization, candidate) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var token = links.Issue(organization, candidate);

        links.Read(token).Should().Be(new TalentPoolLink(organization, candidate));
        links.RemoveUrl("acme", token).Should().Be($"https://acme.careers.example.com/talent-pool/{token}");
        links.JobUrl("acme", "warehouse-lead").Should().Be("https://acme.careers.example.com/jobs/warehouse-lead");
        links.Read(token[..^2] + "xx").Should().BeNull();
        links.Read(null).Should().BeNull();
        var questionnaire = provider.CreateProtector("ScreenHire.Questionnaire.Link.v1").Protect($"{organization:N}.{candidate:N}");
        links.Read(questionnaire).Should().BeNull("each link has its own purpose");
    }

    private static (JobApplication Application, Job Job) Applied(FutureRolesConsent? consent)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Support engineer", Slug = "support-engineer" };
        var candidate = Candidate.Create(organization, "Grace", "Adeyemi", "grace@example.com", null, Monday);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [], FutureRoles: consent));
        return (application, job);
    }
}
