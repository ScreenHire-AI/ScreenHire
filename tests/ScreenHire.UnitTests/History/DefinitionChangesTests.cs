using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;

namespace ScreenHire.UnitTests.History;

public sealed class DefinitionChangesTests
{
    private static readonly Question Exercise = new("exercise", QuestionType.YesNo, "Did they finish the exercise in the time?", Required: true);
    private static readonly Question Notes = new("notes", QuestionType.LongText, "Notes on the exercise", Required: true);
    private static readonly Question Experience = new("experience", QuestionType.ShortText, "Years of experience");

    private static HiringWorkflow Workflow(params Question[] questions) =>
        HiringWorkflow.Default with { Questionnaire = new Questionnaire(questions) };

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(l => l.Sentence());

    [Fact]
    public void An_unchanged_definition_says_nothing()
    {
        DefinitionChanges.Describe(Workflow(Notes, Experience), Workflow(Notes, Experience)).Should().BeEmpty();
    }

    [Fact]
    public void An_added_question_is_named_with_its_answer_kind_in_the_details()
    {
        var lines = DefinitionChanges.Describe(Workflow(Notes), Workflow(Notes, Exercise));

        Sentences(lines).Should().Equal("added Did they finish the exercise in the time?");
        lines[0].Parts.Should().Equal(new ChangeText("added "), new ChangeText("Did they finish the exercise in the time?", Strong: true));
        lines[0].Details.Should().Contain(new ChangeDetail("Answer", null, "Yes or no")).And.Contain(new ChangeDetail("Required", null, "on"));
    }

    [Fact]
    public void A_removed_question_is_named()
    {
        Sentences(DefinitionChanges.Describe(Workflow(Notes, Exercise), Workflow(Notes))).Should().Equal("removed Did they finish the exercise in the time?");
    }

    [Fact]
    public void A_renamed_question_is_matched_by_its_id_so_it_is_one_rename()
    {
        var lines = DefinitionChanges.Describe(Workflow(Notes), Workflow(Notes with { Label = "Notes on the take-home exercise" }));

        Sentences(lines).Should().Equal("renamed the question from Notes on the exercise to Notes on the take-home exercise");
        lines[0].Parts[^1].Should().Be(new ChangeText("Notes on the take-home exercise", Strong: true));
    }

    [Fact]
    public void A_question_made_optional_or_required_says_so()
    {
        Sentences(DefinitionChanges.Describe(Workflow(Notes), Workflow(Notes with { Required = false })))
            .Should().Equal("made Notes on the exercise optional");
        Sentences(DefinitionChanges.Describe(Workflow(Experience), Workflow(Experience with { Required = true })))
            .Should().Equal("made Years of experience required");
    }

    [Fact]
    public void A_changed_answer_kind_says_from_and_to_with_required_unchanged_in_the_details()
    {
        var lines = DefinitionChanges.Describe(Workflow(Experience), Workflow(Experience with { Type = QuestionType.Number }));

        Sentences(lines).Should().Equal("changed Years of experience from Short text to Number");
        lines[0].Details.Should().Equal(new ChangeDetail("Answer", "Short text", "Number"), new ChangeDetail("Required", "off", "off"));
    }

    [Fact]
    public void Other_changes_to_a_question_are_one_line_with_each_value_before_and_after()
    {
        var lines = DefinitionChanges.Describe(
            Workflow(Experience), Workflow(Experience with { HelpText = "Count paid work only.", MaxLength = 20 }));

        Sentences(lines).Should().Equal("changed Years of experience");
        lines[0].Details.Should().BeEquivalentTo([
            new ChangeDetail("Help text", null, "Count paid work only."),
            new ChangeDetail("Longest answer", null, "20 characters"),
        ]);
    }

    [Fact]
    public void Reordered_questions_are_one_line()
    {
        Sentences(DefinitionChanges.Describe(Workflow(Notes, Experience), Workflow(Experience, Notes))).Should().Equal("reordered the questions");
    }

    [Fact]
    public void An_added_stage_is_named_with_its_kind()
    {
        var before = HiringWorkflow.Default;
        var after = before with { Stages = [.. before.Stages.Take(2), new("technical", "Technical interview", StageKind.Interview), .. before.Stages.Skip(2)] };

        var lines = DefinitionChanges.Describe(before, after);

        Sentences(lines).Should().Equal("added the stage Technical interview");
        lines[0].Details.Should().Contain(new ChangeDetail("Kind", null, "Interview"));
    }

    [Fact]
    public void Stage_renames_time_limits_and_scorecards_say_what_changed()
    {
        var before = HiringWorkflow.Default;
        var after = before with
        {
            Stages = [.. before.Stages.Select(s => s.Id switch
            {
                "screening" => s with { Name = "Phone screen", SlaDays = 5 },
                "interview" => s with { SlaDays = 1, Scorecard = s.Scorecard! with { Questions = [.. s.Scorecard!.Questions.Skip(1)] } },
                _ => s,
            })],
        };

        Sentences(DefinitionChanges.Describe(before, after)).Should().Equal(
            "renamed the stage from Screening to Phone screen",
            "changed the time limit of Phone screen from 3 days to 5 days",
            "set a time limit of 1 day on Interview",
            "changed the scorecard of Interview");
    }

    [Fact]
    public void Automations_added_turned_off_and_changed_say_so()
    {
        var email = new Automation("welcome", "Welcome email", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
            [new AutomationAction(ActionType.SendEmail, EmailTemplate: EmailTemplateIds.ApplicationReceived)]);
        var before = HiringWorkflow.Default with { Automations = [email] };

        Sentences(DefinitionChanges.Describe(HiringWorkflow.Default, before)).Should().Equal("added the automation Welcome email");
        Sentences(DefinitionChanges.Describe(before, before with { Automations = [email with { Enabled = false }] }))
            .Should().Equal("turned off Welcome email");

        var moved = email with { Trigger = new AutomationTrigger(TriggerType.StageEntered, "screening") };
        var lines = DefinitionChanges.Describe(before, before with { Automations = [moved] });
        Sentences(lines).Should().Equal("changed the automation Welcome email");
        lines[0].Details.Should().Equal(new ChangeDetail("When", "An application arrives", "A candidate enters a stage: Screening"));
    }

    [Fact]
    public void A_webhook_is_recorded_by_its_host_so_a_token_in_its_address_never_is()
    {
        const string token = "T0KEN-in-the-address";
        var hook = new Automation("hook", "Tell the hiring channel", new AutomationTrigger(TriggerType.ApplicationSubmitted), null,
            [new AutomationAction(ActionType.CallWebhook, Url: $"https://hooks.example.com/services/{token}?key={token}")]);
        var before = HiringWorkflow.Default with { Automations = [hook] };
        var rotated = hook with { Actions = [new AutomationAction(ActionType.CallWebhook, Url: "https://hooks.example.com/services/another")] };
        var moved = hook with { Actions = [new AutomationAction(ActionType.CallWebhook, Url: "https://hooks.example.org/x")] };

        var added = DefinitionChanges.Describe(HiringWorkflow.Default, before);
        var changed = DefinitionChanges.Describe(before, before with { Automations = [rotated] });
        var otherHost = DefinitionChanges.Describe(before, before with { Automations = [moved] });

        added[0].Details.Should().Contain(new ChangeDetail("Then", null, "Call a webhook: hooks.example.com"));
        Sentences(changed).Should().Equal("changed the webhook address of Tell the hiring channel");
        otherHost[0].Details.Should().Equal(new ChangeDetail("Then", "Call a webhook: hooks.example.com", "Call a webhook: hooks.example.org"));
        System.Text.Json.JsonSerializer.Serialize(added.Concat(changed).Concat(otherHost)).Should().NotContain(token);
    }

    [Fact]
    public void A_follow_up_questionnaire_added_whole_is_one_line_listing_its_questions()
    {
        var followUp = new FollowUpQuestionnaire("availability", "Availability", [new Question("start", QuestionType.ShortText, "When could you start?")]);

        var lines = DefinitionChanges.Describe(HiringWorkflow.Default, HiringWorkflow.Default with { FollowUpQuestionnaires = [followUp] });

        Sentences(lines).Should().Equal("added the follow-up questionnaire Availability");
        lines[0].Details.Should().Equal(new ChangeDetail("Questions", null, "When could you start?"));
    }

    [Fact]
    public void A_question_moved_into_a_follow_up_is_found_by_its_id()
    {
        var before = Workflow(Notes, Experience) with { FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Availability", [])] };
        var after = Workflow(Notes) with { FollowUpQuestionnaires = [new FollowUpQuestionnaire("availability", "Availability", [Experience])] };

        DefinitionChanges.Describe(before, after).Should().BeEmpty();
    }

    [Fact]
    public void Ai_use_and_posting_defaults_say_what_changed()
    {
        var before = HiringTemplateDefinition.Default;
        var after = before with
        {
            Workflow = before.Workflow with { IntegrityPolicy = new IntegrityPolicy(AiUsePolicy.NotAllowed) },
            PostingDefaults = new PostingDefaults(Department: "Engineering", Description: "<p>We build <b>booking</b> software.</p><ul><li>C#</li></ul>"),
        };

        var lines = DefinitionChanges.Describe(before, after);

        Sentences(lines).Should().Equal("changed AI use from Allowed with disclosure to Not allowed", "changed the job defaults");
        lines[1].Details.Should().Equal(
            new ChangeDetail("Department", null, "Engineering"),
            new ChangeDetail("Description", null, "We build booking software.\n\n- C#", ChangeValueKind.Paragraphs));
    }
}
