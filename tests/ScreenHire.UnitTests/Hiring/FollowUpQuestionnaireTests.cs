using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Email;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Follow-up questionnaires (workflow-engine.md §8): how the validator reads them beside the apply questionnaire, how the
/// QuestionnaireCompleted trigger matches, which answers a facts snapshot sees, the request's lifecycle, the email that
/// carries the link and the link itself.
/// </summary>
public sealed class FollowUpQuestionnaireTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly Question WorkAuthorization = new(
        "work_authorization", QuestionType.YesNo, "Do you have the right to work here?", Required: true, Knockout: new Knockout(false));

    private static readonly Question Licence = new(
        "forklift_licence", QuestionType.YesNo, "Do you hold a forklift licence?", Required: true, Knockout: new Knockout(false, "This role needs a forklift licence."));

    private static readonly Question StartDate = new("start_date", QuestionType.ShortText, "When could you start?");

    private static readonly FollowUpQuestionnaire Availability = new("availability", "Availability", [StartDate, Licence]);

    [Fact]
    public void A_workflow_with_follow_up_questionnaires_validates()
    {
        DefinitionValidator.Validate(Workflow(followUps: [Availability])).Should().BeEmpty();
    }

    [Fact]
    public void A_stored_workflow_without_follow_ups_still_validates()
    {
        (HiringWorkflow.Default.FollowUpQuestionnaires is null).Should().BeTrue();
        DefinitionValidator.Validate(HiringWorkflow.Default).Should().BeEmpty();
    }

    [Fact]
    public void Question_ids_are_unique_across_every_questionnaire_of_a_workflow()
    {
        var clash = new FollowUpQuestionnaire("second", "Second", [WorkAuthorization with { Label = "Again" }]);
        var other = new FollowUpQuestionnaire("third", "Third", [StartDate with { Label = "Again" }]);

        DefinitionValidator.Validate(Workflow([WorkAuthorization], [Availability, clash, other])).Select(e => e.Path).Should().Equal(
            "followUpQuestionnaires[1].questions[0].id", "followUpQuestionnaires[2].questions[0].id");
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("linkedin_export")]
    public void A_follow_up_question_cannot_take_an_id_the_apply_form_uses(string id)
    {
        var followUp = new FollowUpQuestionnaire("more", "More", [StartDate with { Id = id }]);

        DefinitionValidator.Validate(Workflow(followUps: [followUp])).Should().ContainSingle(e => e.Path == "followUpQuestionnaires[0].questions[0].id");
    }

    [Fact]
    public void A_follow_up_questionnaire_asks_no_file_question_and_at_least_one_question()
    {
        var file = new FollowUpQuestionnaire("files", "Files", [new Question("portfolio", QuestionType.File, "Portfolio", AcceptedFileTypes: [".pdf"])]);
        var empty = new FollowUpQuestionnaire("empty", "Empty", []);

        DefinitionValidator.Validate(Workflow(followUps: [file, empty])).Select(e => e.Path).Should().BeEquivalentTo(
            "followUpQuestionnaires[0].questions[0].type", "followUpQuestionnaires[1].questions");
    }

    [Fact]
    public void Follow_up_questionnaire_ids_are_unique_and_named()
    {
        var twin = Availability with { Name = " " };

        DefinitionValidator.Validate(Workflow(followUps: [Availability, twin with { Questions = [StartDate with { Id = "other" }] }]))
            .Select(e => e.Path).Should().BeEquivalentTo("followUpQuestionnaires[1].id", "followUpQuestionnaires[1].name");
    }

    [Fact]
    public void Send_questionnaire_names_a_follow_up_of_the_workflow()
    {
        var ok = Automation("send", new AutomationTrigger(TriggerType.StageEntered, "screening"), [new AutomationAction(ActionType.SendQuestionnaire, QuestionnaireId: "availability")]);
        var missing = ok with { Id = "missing", Actions = [new AutomationAction(ActionType.SendQuestionnaire)] };
        var unknown = ok with { Id = "unknown", Actions = [new AutomationAction(ActionType.SendQuestionnaire, QuestionnaireId: "nope")] };
        var stray = ok with { Id = "stray", Actions = [new AutomationAction(ActionType.AddTag, Tag: "x", QuestionnaireId: "availability")] };

        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [ok, missing, unknown, stray])).Select(e => e.Path).Should().Equal(
            "automations[1].actions[0].questionnaireId", "automations[2].actions[0].questionnaireId", "automations[3].actions[0].questionnaireId");
    }

    [Fact]
    public void A_questionnaire_completed_trigger_may_name_a_follow_up_and_no_other_trigger_may()
    {
        var named = Automation("named", new AutomationTrigger(TriggerType.QuestionnaireCompleted, QuestionnaireId: "availability"), [Tag()]);
        var any = Automation("any", new AutomationTrigger(TriggerType.QuestionnaireCompleted), [Tag()]);
        var unknown = Automation("unknown", new AutomationTrigger(TriggerType.QuestionnaireCompleted, QuestionnaireId: "nope"), [Tag()]);
        var stray = Automation("stray", new AutomationTrigger(TriggerType.ApplicationSubmitted, QuestionnaireId: "availability"), [Tag()]);

        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [named, any, unknown, stray])).Select(e => e.Path).Should().Equal(
            "automations[2].trigger.questionnaireId", "automations[3].trigger.questionnaireId");
    }

    [Fact]
    public void Send_email_cannot_send_the_follow_up_questions_email()
    {
        var automation = Automation("a", new AutomationTrigger(TriggerType.ApplicationSubmitted),
            [new AutomationAction(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.QuestionnaireRequest)]);

        DefinitionValidator.Validate(Workflow(automations: [automation])).Should().ContainSingle()
            .Which.Path.Should().Be("automations[0].actions[0].emailTemplate");
    }

    // Only a person's Invite to apply sends the talent pool invitation (docs/architecture/talent-pool.md, guardrail 5):
    // no automation names it, so nothing automatic invites anyone or acts on the pool.
    [Fact]
    public void Send_email_cannot_send_the_talent_pool_invitation()
    {
        var automation = Automation("a", new AutomationTrigger(TriggerType.ApplicationSubmitted),
            [new AutomationAction(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.TalentPoolInvitation)]);

        DefinitionValidator.Validate(Workflow(automations: [automation])).Should().ContainSingle()
            .Which.Path.Should().Be("automations[0].actions[0].emailTemplate");
    }

    // The knockout rules hold for follow-up questions as for apply questions (guardrail 2): queue by default, reject only
    // with the opt-in, and never on a question that is not a knockout.
    [Fact]
    public void A_knockout_on_a_follow_up_question_rejects_only_with_the_opt_in()
    {
        Automation Knockout(bool optIn, string questionId = "forklift_licence") => new(
            "knockout", "Licence", new AutomationTrigger(TriggerType.QuestionnaireCompleted, QuestionnaireId: "availability"),
            new AutomationConditions(ConditionMatch.All, [new AutomationCondition($"answers.{questionId}", ConditionOperator.EqualTo, questionId == "start_date" ? "later" : "no")]),
            [new AutomationAction(ActionType.Reject, Reason: "This role needs a forklift licence.")],
            AllowAutoReject: optIn);

        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [Knockout(optIn: true)])).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [Knockout(optIn: false)])).Should().ContainSingle()
            .Which.Path.Should().Be("automations[0].allowAutoReject");
        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [Knockout(optIn: true, "start_date")])).Should().ContainSingle()
            .Which.Message.Should().Contain("Only a knockout automation may reject");
    }

    // Guardrail 1 (ADR-0004): a signal may route, tag, assign or request verification. Asking the candidate for more is
    // none of those, directly or through a tag a signal set.
    [Fact]
    public void A_signal_driven_automation_may_not_send_a_questionnaire()
    {
        var direct = Automation("direct", new AutomationTrigger(TriggerType.VerificationAssessed),
            [new AutomationAction(ActionType.SendQuestionnaire, QuestionnaireId: "availability")]);
        var tagger = Automation("tagger", new AutomationTrigger(TriggerType.VerificationAssessed), [new AutomationAction(ActionType.AddTag, Tag: "flagged")]);
        var laundered = Automation("laundered", new AutomationTrigger(TriggerType.Manual), [new AutomationAction(ActionType.SendQuestionnaire, QuestionnaireId: "availability")]) with
        {
            Conditions = new AutomationConditions(ConditionMatch.All, [new AutomationCondition(FactSchema.Tags, ConditionOperator.Contains, "flagged")]),
        };

        DefinitionValidator.Validate(Workflow(followUps: [Availability], automations: [direct, tagger, laundered])).Select(e => e.Path).Should().Equal(
            "automations[0].actions[0]", "automations[2].actions[0]");
    }

    [Fact]
    public void A_completed_questionnaire_fires_the_automations_that_name_it_or_name_none()
    {
        var workflow = Workflow(automations:
        [
            Automation("availability", new AutomationTrigger(TriggerType.QuestionnaireCompleted, QuestionnaireId: "availability"), [Tag()]),
            Automation("other", new AutomationTrigger(TriggerType.QuestionnaireCompleted, QuestionnaireId: "other"), [Tag()]),
            Automation("any", new AutomationTrigger(TriggerType.QuestionnaireCompleted), [Tag()]),
        ]);

        AutomationEvaluator.Triggered(workflow, TriggerType.QuestionnaireCompleted, null, "availability").Select(a => a.Id).Should().Equal("availability", "any");
    }

    [Fact]
    public void Facts_hold_the_apply_answers_and_the_source_for_every_trigger()
    {
        var application = Application([new ApplicationAnswer("work_authorization", "no"), new ApplicationAnswer("shifts", Values: ["nights", "mornings"])]);
        var submitted = Event(application, ApplicationEventType.Submitted, Start);

        var facts = AutomationFacts.FromTimeline([submitted], submitted.Id, TriggerType.ApplicationSubmitted, Start, FactSources.Of(application, []));

        facts.ValuesOf("answers.work_authorization").Should().Equal("no");
        facts.ValuesOf("answers.shifts").Should().Equal("nights", "mornings");
        facts.ValuesOf(FactSchema.Source).Should().Equal("CareersSite");
    }

    // Follow-up answers arrive with their QuestionnaireCompleted entry, so a trigger before it never sees them and a
    // replay of an earlier trigger sees what it saw the first time.
    [Fact]
    public void Follow_up_answers_count_only_for_triggers_after_they_arrived()
    {
        var application = Application([]);
        var submitted = Event(application, ApplicationEventType.Submitted, Start);
        var moved = Event(application, ApplicationEventType.StageChanged, Start.AddHours(1));
        var request = QuestionnaireRequest.Create(application, Availability, Start.AddHours(1));
        var completed = request.Complete(Availability, [new ApplicationAnswer("forklift_licence", "no")], Start.AddHours(2));
        var tagged = Event(application, ApplicationEventType.TagAdded, Start.AddHours(3), new ApplicationEventData(Tag: "x"));
        var timeline = new[] { submitted, moved, completed, tagged };
        var sources = FactSources.Of(application, [request]);

        AutomationFacts.FromTimeline(timeline, moved.Id, TriggerType.StageEntered, moved.OccurredAt, sources)
            .ValuesOf("answers.forklift_licence").Should().BeEmpty();
        AutomationFacts.FromTimeline(timeline, completed.Id, TriggerType.QuestionnaireCompleted, completed.OccurredAt, sources)
            .ValuesOf("answers.forklift_licence").Should().Equal("no");
        AutomationFacts.FromTimeline(timeline, tagged.Id, TriggerType.Manual, tagged.OccurredAt, sources)
            .ValuesOf("answers.forklift_licence").Should().Equal("no");
    }

    [Fact]
    public void A_request_is_sent_again_with_a_fresh_expiry_until_it_is_answered_once()
    {
        var application = Application([]);
        var request = QuestionnaireRequest.Create(application, Availability, Start);
        request.Resend(Availability, Start.AddDays(10));

        request.TimesSent.Should().Be(2);
        request.ExpiresAt.Should().Be(Start.AddDays(10) + QuestionnaireRequest.LinkLifetime);
        request.IsExpired(Start.AddDays(23)).Should().BeFalse("the second send moved the expiry on");
        request.IsExpired(Start.AddDays(24)).Should().BeTrue();

        var entry = request.Complete(Availability, [new ApplicationAnswer("start_date", "  Next month ")], Start.AddDays(11));

        entry.Type.Should().Be(ApplicationEventType.QuestionnaireCompleted);
        entry.Data.QuestionnaireRequestId.Should().Be(request.Id);
        request.CompletedEventId.Should().Be(entry.Id);
        request.Answers.Should().Equal(new ApplicationAnswer("start_date", "Next month"));
        request.Questions.Select(q => q.Label).Should().Equal(StartDate.Label, Licence.Label);
        FluentActions.Invoking(() => request.Complete(Availability, [], Start.AddDays(12))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => request.Resend(Availability, Start.AddDays(12))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_follow_up_questions_email_must_carry_its_link_and_no_other_template_may()
    {
        var templates = new EmailTemplates();

        templates.Validate(EmailTemplateIds.QuestionnaireRequest, new("More questions", "Hi {{ candidateName }}.")).Should().ContainSingle()
            .Which.Message.Should().Contain("{{ questionnaireLink }}");
        templates.Validate(EmailTemplateIds.QuestionnaireRequest, new("More questions", "Answer here: {{ questionnaireLink }}")).Should().BeEmpty();
        templates.Validate(EmailTemplateIds.StageChange, new("Update", "Answer here: {{ questionnaireLink }}")).Should().ContainSingle()
            .Which.Message.Should().Contain("There is no variable \"questionnaireLink\"");
        templates.Validate(null, new("Update", "{{ questionnaireLink }}")).Should().NotBeEmpty();
    }

    [Fact]
    public void The_follow_up_questions_email_renders_its_link_as_a_link()
    {
        var link = "https://acme.careers.example.com/questionnaires/token-123";
        var email = new EmailTemplates().Render(EmailTemplateIds.QuestionnaireRequest, "sam@example.com",
            new EmailTemplateModel("Sam Rivera", "Warehouse lead", "Acme", QuestionnaireLink: link));

        email.Subject.Should().Be("A few more questions about your application for Warehouse lead");
        email.TextBody.Should().Contain(link);
        email.HtmlBody.Should().Contain($"<a href=\"{link}\" rel=\"nofollow noopener noreferrer\">{link}</a>");
    }

    [Fact]
    public void A_link_reads_back_and_a_forged_or_foreign_token_does_not()
    {
        var provider = new EphemeralDataProtectionProvider();
        var links = new QuestionnaireLinks(provider, Options.Create(new CareersSiteOptions
        {
            Scheme = "https",
            CareersDomain = "careers.example.com",
            CareersHostPattern = "{tenant}.{CareersDomain}",
        }));
        var request = QuestionnaireRequest.Create(Application([]), Availability, DateTimeOffset.UtcNow);
        var token = links.Issue(request);

        links.Read(token).Should().Be(new QuestionnaireLink(request.OrganizationId, request.Id));
        links.Url("acme", token).Should().Be($"https://acme.careers.example.com/questionnaires/{token}");
        links.Read(token[..^2] + (token[^1] == 'A' ? "BB" : "AA")).Should().BeNull();
        links.Read("not-a-token").Should().BeNull();
        links.Read(null).Should().BeNull();

        // Another purpose (the telemetry session token) under the same keys is not a questionnaire link.
        var telemetry = provider.CreateProtector("ScreenHire.Telemetry.Session.v1").ToTimeLimitedDataProtector()
            .Protect($"{request.OrganizationId:N}.{request.Id:N}", DateTimeOffset.UtcNow.AddHours(1));
        links.Read(telemetry).Should().BeNull();
        links.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Links_without_a_careers_host_pattern_are_not_configured()
    {
        // A worker without ScreenHire:Domains would email https:///questionnaires/..., so SendQuestionnaire fails first.
        new QuestionnaireLinks(new EphemeralDataProtectionProvider(), Options.Create(new CareersSiteOptions())).IsConfigured.Should().BeFalse();
    }

    private static AutomationAction Tag() => new(ActionType.AddTag, Tag: "x");

    private static Automation Automation(string id, AutomationTrigger trigger, IReadOnlyList<AutomationAction> actions) =>
        new(id, $"Automation {id}", trigger, null, actions);

    private static HiringWorkflow Workflow(
        IReadOnlyList<Question>? questions = null, IReadOnlyList<FollowUpQuestionnaire>? followUps = null, IReadOnlyList<Automation>? automations = null) =>
        HiringWorkflow.Default with
        {
            Questionnaire = new Questionnaire(questions ?? []),
            FollowUpQuestionnaires = followUps,
            Automations = automations ?? [],
        };

    private static JobApplication Application(IReadOnlyList<ApplicationAnswer> answers)
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Warehouse lead", Slug = "warehouse-lead" };
        var candidate = Candidate.Create(organization, "Sam", "Rivera", "sam@example.com", null, Start);
        return JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Start, null,
            new ApplicationIntake(ApplicationSource.CareersSite, answers)).Application;
    }

    private static ApplicationEvent Event(JobApplication application, ApplicationEventType type, DateTimeOffset at, ApplicationEventData? data = null) =>
        new() { OrganizationId = application.OrganizationId, ApplicationId = application.Id, Type = type, Data = data ?? new(), OccurredAt = at };
}
