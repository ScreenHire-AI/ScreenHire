using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>
/// Library forms (workflow-engine.md section 7, Forms): a form is checked on its own as a follow-up questionnaire is, a
/// hiring template's copy names its library form and version together, versions are immutable once published, and an
/// equality monitoring form is only ever counted: choices only, nothing required, and no automation reads or starts on it.
/// </summary>
public sealed class FormLibraryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static readonly Question StartDate = new("start_date", QuestionType.ShortText, "When could you start?");

    private static readonly Question AgeBand = new(
        "age_band", QuestionType.SingleChoice, "Your age", Options: [new("under_25", "Under 25"), new("over_25", "25 or over"), new("not_say", "Prefer not to say")]);

    private static readonly FollowUpQuestionnaire Equality =
        new("equality", "Equality monitoring", [AgeBand], FormTemplateId: Guid.CreateVersion7(), FormVersion: 1, Kind: FormKind.EqualityMonitoring);

    [Fact]
    public void A_form_on_its_own_is_checked_as_a_follow_up_is()
    {
        DefinitionValidator.ValidateForm(new("Availability", [StartDate]), FormKind.Standard).Should().BeEmpty();
        DefinitionValidator.ValidateForm(new("Availability", []), FormKind.Standard).Select(e => e.Path).Should().Equal("questions");
        DefinitionValidator.ValidateForm(new("", [StartDate]), FormKind.Standard).Select(e => e.Path).Should().Equal("name");
        DefinitionValidator.ValidateForm(new("Files", [new("cv", QuestionType.File, "Your CV")]), FormKind.Standard)
            .Select(e => e.Path).Should().Equal(["questions[0].type"], "a follow-up page takes no uploads");
        DefinitionValidator.ValidateForm(new("Twice", [StartDate, StartDate with { Label = "Again" }]), FormKind.Standard)
            .Select(e => e.Path).Should().Equal("questions[1].id");
    }

    [Fact]
    public void An_equality_monitoring_form_asks_only_optional_choices_with_no_knockout()
    {
        DefinitionValidator.ValidateForm(new("Equality monitoring", [AgeBand]), FormKind.EqualityMonitoring).Should().BeEmpty();

        var errors = DefinitionValidator.ValidateForm(
            new("Equality monitoring", [
                StartDate,
                AgeBand with { Id = "required_band", Required = true },
                new("eligible", QuestionType.YesNo, "Are you eligible?", Knockout: new Knockout(false)),
            ]),
            FormKind.EqualityMonitoring);

        errors.Select(e => e.Path).Should().Equal("questions[0].type", "questions[1].required", "questions[2].knockout");
    }

    [Fact]
    public void A_copy_names_its_library_form_and_version_together()
    {
        var linked = new FollowUpQuestionnaire("availability", "Availability", [StartDate], FormTemplateId: Guid.CreateVersion7(), FormVersion: 2);
        DefinitionValidator.Validate(Workflow(followUps: [linked])).Should().BeEmpty();
        DefinitionValidator.Validate(Workflow(followUps: [linked with { FormVersion = null }])).Select(e => e.Path)
            .Should().Equal("followUpQuestionnaires[0].formVersion");
        DefinitionValidator.Validate(Workflow(followUps: [linked with { FormTemplateId = null }])).Select(e => e.Path)
            .Should().Equal("followUpQuestionnaires[0].formTemplateId");
        DefinitionValidator.Validate(Workflow(followUps: [linked with { FormVersion = 0 }])).Select(e => e.Path)
            .Should().Equal("followUpQuestionnaires[0].formVersion");
    }

    [Fact]
    public void An_equality_monitoring_form_may_be_sent_but_no_automation_reads_it_or_starts_on_it()
    {
        var send = new Automation("send-equality", "Send it", new(TriggerType.ApplicationSubmitted), null,
            [new(ActionType.SendQuestionnaire, QuestionnaireId: "equality")]);
        DefinitionValidator.Validate(Workflow(followUps: [Equality], automations: [send])).Should().BeEmpty();

        var reads = new Automation("reads", "Reads it", new(TriggerType.Manual),
            new(ConditionMatch.All, [new("answers.age_band", ConditionOperator.EqualTo, "under_25")]),
            [new(ActionType.AddTag, Tag: "young")]);
        var startsOn = new Automation("starts-on", "Starts on it", new(TriggerType.QuestionnaireCompleted, QuestionnaireId: "equality"), null,
            [new(ActionType.AddTag, Tag: "answered")]);

        DefinitionValidator.Validate(Workflow(followUps: [Equality], automations: [reads, startsOn])).Select(e => e.Path).Should().Equal(
            "automations[0].conditions.rules[0].fact", "automations[1].trigger.questionnaireId");
    }

    [Fact]
    public void A_published_version_never_changes_and_a_form_keeps_one_draft()
    {
        var form = FormTemplate.Create(Guid.CreateVersion7(), FormKind.Standard, new("Availability", [StartDate]), Start, null);
        var first = form.Draft!;
        form.Publish(first, Start, null);

        form.LatestPublishedVersion.Should().Be(1);
        form.Invoking(f => f.Revise(first, new("Changed", [StartDate]), Start)).Should().Throw<InvalidOperationException>();

        var second = form.CreateDraft(new("Availability", [StartDate with { Label = "Earliest start?" }]), Start, null);
        second.Version.Should().Be(2);
        form.Invoking(f => f.CreateDraft(new("Again", [StartDate]), Start, null)).Should().Throw<InvalidOperationException>();

        form.ToFollowUp(first, "availability").Should().BeEquivalentTo(
            new FollowUpQuestionnaire("availability", "Availability", [StartDate], null, form.Id, 1, null), "a template's copy names the version it holds");
    }

    [Fact]
    public void History_says_what_a_new_version_changed_and_when_a_template_takes_it()
    {
        var form = FormTemplate.Create(Guid.CreateVersion7(), FormKind.Standard, new("Availability", [StartDate]), Start, null);
        form.Publish(form.Draft!, Start, null);
        var revised = form.CreateDraft(new("Availability", [StartDate with { Label = "Earliest start?" }]), Start, null);

        FormTemplateChanges.Version(null, revised, form.Versions[0].Content).Select(l => l.Sentence())
            .Should().Equal("started version 2", "renamed the question from When could you start? to Earliest start?");

        var before = Workflow(followUps: [form.ToFollowUp(form.Versions[0], "availability")]);
        form.Publish(revised, Start, null);
        var after = Workflow(followUps: [form.ToFollowUp(revised, "availability")]);
        DefinitionChanges.Describe(before, after).Select(l => l.Sentence()).Should().Contain("updated Availability to version 2 of the library form");
    }

    [Fact]
    public void Equality_monitoring_answers_are_never_an_automations_fact()
    {
        var application = Application();
        var standard = new FollowUpQuestionnaire("availability", "Availability", [StartDate]);
        var answeredStandard = QuestionnaireRequest.Create(application, standard, Start);
        var standardEntry = answeredStandard.Complete(standard, [new("start_date", "Monday")], Start.AddHours(1));
        var answeredEquality = QuestionnaireRequest.Create(application, Equality, Start);
        var equalityEntry = answeredEquality.Complete(Equality, [new("age_band", "under_25")], Start.AddHours(2));

        answeredEquality.IsEqualityMonitoring.Should().BeTrue("the request keeps the kind it was sent with");
        answeredEquality.FormTemplateId.Should().Be(Equality.FormTemplateId);

        var later = new ApplicationEvent
        {
            OrganizationId = application.OrganizationId, ApplicationId = application.Id, Type = ApplicationEventType.TagAdded,
            Data = new(Tag: "x"), OccurredAt = Start.AddHours(3),
        };
        var facts = AutomationFacts.FromTimeline(
            [standardEntry, equalityEntry, later], later.Id, TriggerType.Manual, later.OccurredAt,
            FactSources.Of(application, [answeredStandard, answeredEquality]));

        facts.ValuesOf("answers.start_date").Should().Equal("Monday");
        facts.ValuesOf("answers.age_band").Should().BeEmpty();
    }

    private static HiringWorkflow Workflow(IReadOnlyList<FollowUpQuestionnaire>? followUps = null, IReadOnlyList<Automation>? automations = null) =>
        HiringWorkflow.Default with { FollowUpQuestionnaires = followUps, Automations = automations ?? [] };

    private static JobApplication Application()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Line cook", Slug = "line-cook" };
        var candidate = Candidate.Create(organization, "Sam", "Rivera", "sam@example.com", null, Start);
        return JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Start, null,
            new ApplicationIntake(ApplicationSource.CareersSite, [])).Application;
    }
}
