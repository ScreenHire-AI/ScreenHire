using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Questionnaire pages and saving to finish later (apply-flow.md §7 and §8): named sections in the definition and how the
/// validator holds them to the questions, the organization's questions per page, what a draft keeps and checks, the
/// draft's link and email, a follow-up request's saved answers, and the history lines for all of it.
/// </summary>
public sealed class QuestionnaireSectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Question Authorization = new("work_authorization", QuestionType.YesNo, "Can you work here?", Required: true, Knockout: new Knockout(false));
    private static readonly Question Experience = new("experience", QuestionType.SingleChoice, "Years?", Required: true, Options: [new("junior", "Junior"), new("senior", "Senior")]);
    private static readonly Question Notice = new("notice", QuestionType.ShortText, "Notice period?", MaxLength: 100);
    private static readonly Question Resume = new("resume", QuestionType.File, "CV", Required: true, AcceptedFileTypes: [".pdf"]);

    private static HiringWorkflow Workflow(IReadOnlyList<QuestionSection>? sections, IReadOnlyList<Question>? questions = null) =>
        HiringWorkflow.Default with { Questionnaire = new Questionnaire(questions ?? [Authorization, Experience, Notice], sections) };

    [Fact]
    public void Sections_that_cover_every_question_once_and_in_order_validate()
    {
        DefinitionValidator.Validate(Workflow([new("eligibility", "Eligibility", ["work_authorization"]), new("you", "About you", ["experience", "notice"])]))
            .Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(null)).Should().BeEmpty("no sections means automatic pages");
        DefinitionValidator.Validate(Workflow([])).Should().BeEmpty();
    }

    [Fact]
    public void Sections_are_named_unique_and_never_empty()
    {
        var errors = DefinitionValidator.Validate(Workflow(
        [
            new("one", " ", ["work_authorization"]),
            new("one", "Again", ["experience", "notice"]),
            new("empty", "Nothing here", []),
        ]));

        errors.Select(e => e.Path).Should().BeEquivalentTo(["questionnaire.sections[0].name", "questionnaire.sections[1].id", "questionnaire.sections[2]"]);
        errors.Single(e => e.Path == "questionnaire.sections[2]").Message.Should().Be("Add a question to this section or delete it.");
    }

    [Fact]
    public void Every_question_is_in_exactly_one_section_in_the_questionnaires_order()
    {
        DefinitionValidator.Validate(Workflow([new("a", "A", ["work_authorization", "experience"])]))
            .Should().ContainSingle(e => e.Path == "questionnaire.sections" && e.Message.StartsWith("Every question belongs to a section", StringComparison.Ordinal));
        DefinitionValidator.Validate(Workflow([new("a", "A", ["work_authorization", "experience"]), new("b", "B", ["experience", "notice"])]))
            .Should().ContainSingle(e => e.Message == "Question 'experience' is in two sections.");
        DefinitionValidator.Validate(Workflow([new("a", "A", ["experience", "work_authorization", "notice"])]))
            .Should().ContainSingle(e => e.Message.Contains("different order", StringComparison.Ordinal));
        DefinitionValidator.Validate(Workflow([new("a", "A", ["work_authorization", "experience", "notice", "ghost"])]))
            .Should().ContainSingle(e => e.Message == "This questionnaire has no question 'ghost'.");
    }

    [Fact]
    public void A_follow_up_questionnaire_takes_sections_by_the_same_rules()
    {
        var followUp = new FollowUpQuestionnaire("availability", "Availability",
            [new Question("start", QuestionType.ShortText, "Start?"), new Question("shifts", QuestionType.ShortText, "Shifts?")],
            [new QuestionSection("when", "When", ["start"])]);

        DefinitionValidator.Validate(HiringWorkflow.Default with { FollowUpQuestionnaires = [followUp] })
            .Should().ContainSingle(e => e.Path == "followUpQuestionnaires[0].sections");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Questions_per_page_stays_from_1_to_50(int value)
    {
        var organization = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };

        organization.QuestionsPerPage.Should().Be(8);
        organization.Invoking(o => o.SetQuestionsPerPage(value)).Should().Throw<ArgumentOutOfRangeException>();
        organization.SetQuestionsPerPage(12);
        organization.QuestionsPerPage.Should().Be(12);
    }

    [Fact]
    public void A_draft_checks_the_shape_of_its_answers_but_requires_nothing_and_takes_no_file()
    {
        var questions = ApplicationForm.QuestionsFor(Workflow(null, [Resume, Authorization, Experience]));

        ApplicationForm.ValidateDraft(questions, []).Should().BeEmpty("nothing is required before the application is sent");
        ApplicationForm.ValidateDraft(questions, [new ApplicationAnswer("work_authorization", "no")]).Should().BeEmpty("a knockout answer is a valid answer");
        ApplicationForm.ValidateDraft(questions, [new ApplicationAnswer("experience", "forever")]).Should().ContainSingle(e => e.QuestionId == "experience");
        ApplicationForm.ValidateDraft(questions, [new ApplicationAnswer("resume", Guid.NewGuid().ToString())]).Should().ContainSingle(e => e.QuestionId == "resume");
    }

    [Fact]
    public void A_draft_keeps_its_email_and_first_expiry_and_says_which_answers_came_from_it()
    {
        var content = new ApplicationDraftContent(" Casey ", " ", null, [new("notice", " Two weeks "), new("experience", "senior")], true, AiUseDeclaration.Used, " Grammar ");
        var draft = ApplicationDraft.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), " Casey@Example.com", content, Now);

        draft.Update(content with { Answers = [new("notice", "Two weeks"), new("experience", "junior")] }, Now.AddDays(10));

        draft.Email.Should().Be("casey@example.com");
        draft.FirstName.Should().Be("Casey");
        draft.LastName.Should().BeNull();
        draft.AccommodationRequested.Should().BeTrue();
        draft.AiUseDetails.Should().Be("Grammar");
        draft.ExpiresAt.Should().Be(Now.AddDays(30), "later saves never move the deletion");
        draft.UpdatedAt.Should().Be(Now.AddDays(10));
        draft.IsExpired(Now.AddDays(30)).Should().BeTrue();
        draft.CarriedOver([new("notice", "Two weeks"), new("experience", "senior"), new("other", "x")]).Should().Equal("notice");
    }

    [Fact]
    public void A_follow_up_keeps_saved_answers_until_it_is_answered()
    {
        var questionnaire = new FollowUpQuestionnaire("availability", "Availability", [new Question("start", QuestionType.ShortText, "Start?")]);
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Lead", Slug = "lead" };
        var candidate = Candidate.Create(organization, "Sam", null, "sam@example.com", null, Now);
        var application = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null).Application;
        var request = QuestionnaireRequest.Create(application, questionnaire, Now);

        request.SaveDraft([new ApplicationAnswer("start", " Monday "), new ApplicationAnswer("start2", "")], Now.AddDays(1));
        request.SaveDraft([new ApplicationAnswer("start", "Tuesday")], Now.AddDays(5));

        request.DraftAnswers.Should().Equal(new ApplicationAnswer("start", "Tuesday"));
        request.DraftExpiresAt.Should().Be(Now.AddDays(31));
        request.Complete(questionnaire, [new ApplicationAnswer("start", "Tuesday")], Now.AddDays(6));
        request.DraftAnswers.Should().BeEmpty();
        request.DraftSavedAt.Should().BeNull();
        request.Invoking(r => r.SaveDraft([], Now.AddDays(7))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_draft_link_reads_back_and_a_forged_or_foreign_token_does_not()
    {
        var provider = new EphemeralDataProtectionProvider();
        var drafts = Drafts(provider, withSender: true);
        var draft = ApplicationDraft.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "casey@example.com",
            new ApplicationDraftContent("Casey", null, null, [], false, AiUseDeclaration.NotProvided, null), Now);

        var token = drafts.Issue(draft);

        drafts.Read(token).Should().Be(new ApplicationDraftLink(draft.OrganizationId, draft.Id));
        drafts.Url("acme", "engineer", token).ToString().Should().Be($"https://acme.careers.example.com/jobs/engineer/apply?draft={token}");
        drafts.Read("not-a-token").Should().BeNull();
        var questionnaireLink = provider.CreateProtector("ScreenHire.Questionnaire.Link.v1").ToTimeLimitedDataProtector()
            .Protect($"{draft.OrganizationId:N}.{draft.Id:N}", DateTimeOffset.UtcNow.AddDays(1));
        drafts.Read(questionnaireLink).Should().BeNull("a questionnaire link is not a draft link");
        drafts.IsEnabled.Should().BeTrue();
        Drafts(provider, withSender: false).IsEnabled.Should().BeFalse("without email there is no link to send, so saving is hidden");
    }

    [Fact]
    public void The_draft_email_carries_the_link_the_date_and_that_files_are_not_kept()
    {
        var link = new Uri("https://acme.careers.example.com/jobs/engineer/apply?draft=token-123");

        var email = ApplicationDrafts.Compose("casey@example.com", "Acme <Labs>", "Engineer", link, new DateTimeOffset(2026, 11, 29, 9, 0, 0, TimeSpan.Zero));

        email.Subject.Should().Be("Finish your application for Engineer at Acme <Labs>");
        email.TextBody.Should().Contain(link.ToString()).And.Contain("29 November 2026").And.Contain("Files are not kept");
        email.HtmlBody.Should().Contain("Acme &lt;Labs&gt;").And.Contain($"href=\"{link}\"");
    }

    [Fact]
    public void History_says_what_changed_in_the_sections_and_the_questions_per_page()
    {
        var before = Workflow([new("eligibility", "Eligibility", ["work_authorization"]), new("you", "About you", ["experience", "notice"])]);
        var after = Workflow([new("eligibility", "Right to work", ["work_authorization", "experience"]), new("you", "About you", ["notice"]), new("more", "More", [])]);

        var lines = DefinitionChanges.Describe(before, after).Select(Text).ToList();

        lines.Should().Contain("renamed the section from Eligibility to Right to work");
        lines.Should().Contain("changed the questions in the section About you");
        lines.Should().Contain("added the section More");

        var organization = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };
        var changed = new Organization { Name = "Acme", Slug = "acme", HomeRegion = "eu" };
        changed.SetQuestionsPerPage(10);
        SettingsChanges.Name(organization, changed).Select(Text).Should().Equal("changed questions per page from 8 to 10");
    }

    private static string Text(ChangeLine line) => line.Sentence();

    private static ApplicationDrafts Drafts(IDataProtectionProvider provider, bool withSender)
    {
        var services = new ServiceCollection();
        if (withSender)
        {
            services.AddSingleton<Infrastructure.Email.IEmailSender>(new NullSender());
        }

        return new ApplicationDrafts(
            services.BuildServiceProvider(),
            provider,
            Options.Create(new DomainOptions { Scheme = "https", CareersDomain = "careers.example.com", CareersHostPattern = "{tenant}.{CareersDomain}" }),
            TimeProvider.System);
    }

    private sealed class NullSender : Infrastructure.Email.IEmailSender
    {
        public Task SendAsync(Infrastructure.Email.EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
