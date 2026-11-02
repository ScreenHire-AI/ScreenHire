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

// The saved candidates' rules (docs/architecture/talent-pool.md): saving a candidate ends the application without a
// rejection, only with the candidate's own permission, which has no end date. The note on a saved candidate is the entry's
// own, and an edit of it is described for History.
public sealed class TalentPoolTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recruiter = Guid.CreateVersion7();

    [Fact]
    public void Saving_a_candidate_ends_the_application_without_moving_it_and_keeps_the_note()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday));
        var interview = job.Workflow.FindStage("interview")!;
        application.MoveTo(interview, Monday, Recruiter, note: null);

        var kept = application.KeepInMind(interview, Monday.AddDays(3), Recruiter, "  Strong runner-up  ");

        application.KeptInMindAt.Should().Be(Monday.AddDays(3));
        application.CurrentStageId.Should().Be("interview", "the candidate leaves the job where they stood, not through Rejected");
        application.HasEnded(interview).Should().BeTrue();
        kept.Type.Should().Be(ApplicationEventType.KeptInMind);
        kept.Data.Note.Should().Be("Strong runner-up");
        kept.ActorUserId.Should().Be(Recruiter);
    }

    [Fact]
    public void Only_an_open_application_with_a_permission_that_holds_can_be_saved()
    {
        var (withoutConsent, job) = Applied(consent: null);
        var applied = job.Workflow.FindStage("applied");
        FluentActions.Invoking(() => withoutConsent.KeepInMind(applied, Monday, Recruiter, null))
            .Should().Throw<InvalidOperationException>().WithMessage("*permission*");

        var (withdrawn, _) = Applied(new FutureRolesConsent(Monday));
        withdrawn.WithdrawFutureRoles(Monday.AddDays(1));
        FluentActions.Invoking(() => withdrawn.KeepInMind(applied, Monday.AddDays(2), Recruiter, null))
            .Should().Throw<InvalidOperationException>("a withdrawn permission blocks saving").WithMessage("*permission*");

        var (rejected, _) = Applied(new FutureRolesConsent(Monday));
        var rejectedStage = job.Workflow.Stages.First(s => s.Kind == StageKind.Rejected);
        rejected.MoveTo(rejectedStage, Monday, Recruiter, "Not a fit");
        FluentActions.Invoking(() => rejected.KeepInMind(rejectedStage, Monday, Recruiter, null))
            .Should().Throw<InvalidOperationException>().WithMessage("*ended*");

        var (twice, _) = Applied(new FutureRolesConsent(Monday));
        twice.KeepInMind(applied, Monday, Recruiter, null);
        FluentActions.Invoking(() => twice.KeepInMind(applied, Monday, Recruiter, null)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_permission_has_no_end_date_so_one_given_years_ago_still_allows_saving()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday.AddYears(-5)));

        application.AllowsFutureRoles.Should().BeTrue("the permission lasts until the candidate asks or the team removes them");
        FluentActions.Invoking(() => application.KeepInMind(job.Workflow.FindStage("applied"), Monday, Recruiter, null))
            .Should().NotThrow();
        Applied(consent: null).Application.AllowsFutureRoles.Should().BeFalse("no tick, no permission");
    }

    [Fact]
    public void A_withdrawn_permission_ends_at_once_and_is_not_carried_to_a_new_application()
    {
        var (application, _) = Applied(new FutureRolesConsent(Monday));
        application.AllowsFutureRoles.Should().BeTrue();
        application.CarriedFutureRoles().Should().Be(new FutureRolesConsent(Monday), "adding them to a job keeps the date it was given");

        application.WithdrawFutureRoles(Monday.AddDays(2));
        application.WithdrawFutureRoles(Monday.AddDays(5));

        application.FutureRolesWithdrawnAt.Should().Be(Monday.AddDays(2), "withdrawing twice keeps the first time");
        application.AllowsFutureRoles.Should().BeFalse();
        application.CarriedFutureRoles().Should().BeNull();
    }

    [Fact]
    public void Moving_a_saved_application_brings_it_back_even_to_the_stage_it_was_in()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday));
        var applied = job.Workflow.FindStage("applied")!;
        application.KeepInMind(applied, Monday, Recruiter, null);

        var moved = application.MoveTo(applied, Monday.AddDays(1), Recruiter, note: null);

        moved.Should().NotBeNull();
        moved!.Type.Should().Be(ApplicationEventType.StageChanged);
        application.KeptInMindAt.Should().BeNull();
        application.HasEnded(applied).Should().BeFalse();
        application.MoveTo(applied, Monday.AddDays(2), Recruiter, note: null).Should().BeNull("an open application moving to its own stage changes nothing");
    }

    [Fact]
    public void A_saved_entry_needs_the_applications_own_saved_event_and_starts_with_its_note()
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday));
        var (other, _) = Applied(new FutureRolesConsent(Monday));
        var kept = application.KeepInMind(job.Workflow.FindStage("applied"), Monday, Recruiter, "  why  ");

        var entry = TalentPoolEntry.For(application, kept);

        entry.ApplicationId.Should().Be(application.Id);
        entry.CandidateId.Should().Be(application.CandidateId);
        entry.KeptEventId.Should().Be(kept.Id);
        entry.KeptByUserId.Should().Be(Recruiter);
        entry.Note.Should().Be("why", "the entry's note starts as the saved event's note");
        FluentActions.Invoking(() => TalentPoolEntry.For(other, kept)).Should().Throw<InvalidOperationException>();

        entry.Invited(job.Id, Monday.AddDays(2));
        entry.Invited(Guid.CreateVersion7(), Monday.AddDays(1));
        entry.LastInvitedJobId.Should().Be(job.Id, "an older invitation than the last changes nothing");
    }

    [Fact]
    public void Saving_without_a_note_gives_an_entry_without_one()
    {
        var (application, kept) = SavedWithNote("   ");

        TalentPoolEntry.For(application, kept).Note.Should().BeNull();
    }

    [Fact]
    public void Editing_the_note_trims_it_and_blank_text_clears_it_while_the_saved_event_keeps_the_first_note()
    {
        var (application, kept) = SavedWithNote("First note");
        var entry = TalentPoolEntry.For(application, kept);

        entry.EditNote("  Second note \n");
        entry.Note.Should().Be("Second note");

        entry.EditNote("   ");
        entry.Note.Should().BeNull();

        entry.EditNote("Third note");
        entry.EditNote(null);
        entry.Note.Should().BeNull();
        kept.Data.Note.Should().Be("First note", "the Activity entry keeps the note as first written");
    }

    [Fact]
    public void Adding_or_removing_the_entry_or_leaving_its_note_alone_is_not_a_History_line()
    {
        var (application, kept) = SavedWithNote("First note");
        var entry = TalentPoolEntry.For(application, kept);

        SettingsChanges.SavedNote(null, entry).Should().BeEmpty("saving someone is on the candidate's Activity");
        SettingsChanges.SavedNote(entry, null).Should().BeEmpty("so is taking them out");
        SettingsChanges.SavedNote(entry, TalentPoolEntry.For(application, kept)).Should().BeEmpty("an unchanged note is not a change");
    }

    [Fact]
    public void Editing_a_note_says_so_in_the_History_with_the_note_before_and_after()
    {
        var (application, kept) = SavedWithNote("First note");
        var before = TalentPoolEntry.For(application, kept);
        var after = TalentPoolEntry.For(application, kept);
        after.EditNote("Second note");

        var line = SettingsChanges.SavedNote(before, after).Should().ContainSingle().Subject;

        line.Sentence().Should().Be("edited the note");
        line.Details.Should().Equal(new ChangeDetail("Note", "First note", "Second note", ChangeValueKind.Paragraphs));
    }

    [Fact]
    public void Writing_a_first_note_says_added_a_note_and_clearing_it_says_removed_the_note()
    {
        var (bare, bareEvent) = SavedWithNote(null);
        var empty = TalentPoolEntry.For(bare, bareEvent);
        var written = TalentPoolEntry.For(bare, bareEvent);
        written.EditNote("A note");

        var added = SettingsChanges.SavedNote(empty, written).Should().ContainSingle().Subject;

        added.Sentence().Should().Be("added a note");
        added.Details.Should().Equal(new ChangeDetail("Note", null, "A note", ChangeValueKind.Paragraphs));

        var (application, kept) = SavedWithNote("First note");
        var before = TalentPoolEntry.For(application, kept);
        var cleared = TalentPoolEntry.For(application, kept);
        cleared.EditNote("");

        var removed = SettingsChanges.SavedNote(before, cleared).Should().ContainSingle().Subject;

        removed.Sentence().Should().Be("removed the note");
        removed.Details.Should().Equal(new ChangeDetail("Note", "First note", null, ChangeValueKind.Paragraphs));
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

    private static (JobApplication Application, ApplicationEvent Kept) SavedWithNote(string? note)
    {
        var (application, job) = Applied(new FutureRolesConsent(Monday));
        return (application, application.KeepInMind(job.Workflow.FindStage("applied"), Monday, Recruiter, note));
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
