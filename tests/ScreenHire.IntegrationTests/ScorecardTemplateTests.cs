using System.Text.Json;
using System.Text.Json.Nodes;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The scorecard library (<c>/v1/scorecard-templates</c>, workflow-engine.md section 8): ScreenHire's built-ins first and
/// read only, an organization's own by name; every edit in the scorecard's history; another organization's scorecard is a
/// 404 and cannot be named; a Viewer reads but cannot write. A hiring template's stage names a scorecard and holds no
/// copy, a job copies the questions when it takes the workflow and keeps them when the library scorecard changes.
/// </summary>
public sealed class ScorecardTemplateTests(AppHostFixture fixture)
{
    private static readonly string[] BuiltInIds =
        ["screening", "interview", "technical-exercise", "engineering-interview", "phone-screen", "hourly-interview"];

    [Fact]
    public async Task The_library_lists_the_built_ins_in_their_order_then_the_organizations_own_by_name()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var zeta = await CreateScorecardAsync(owner, "Zeta review", Rating5("depth"));
        var alpha = await CreateScorecardAsync(owner, "Alpha review", Rating5("depth"), Question("notes", "LongText", "Notes"));
        await CreateScorecardAsync(other, "Not ours", Rating5("depth"));

        var list = (await owner.GetAsync("/v1/scorecard-templates")).Expect(HttpStatusCode.OK);

        list.Body.EnumerateArray().Select(s => s.GetProperty("id").GetString()).Should().Equal([.. BuiltInIds, alpha, zeta]);
        list.Body.EnumerateArray().Take(6).Should().OnlyContain(s => s.GetProperty("builtIn").GetBoolean());
        list.Body.EnumerateArray().Skip(6).Should().OnlyContain(s => !s.GetProperty("builtIn").GetBoolean());
        list.Text("1.name").Should().Be("Interview");
        list.At("1.questions").GetInt32().Should().Be(3);
        list.At("1.ratingQuestions").GetInt32().Should().Be(3);
        list.Text("6.name").Should().Be("Alpha review");
        list.At("6.questions").GetInt32().Should().Be(2);
        list.At("6.ratingQuestions").GetInt32().Should().Be(1, "only the 1 to 5 question counts toward the rating");
        (await other.GetAsync("/v1/scorecard-templates")).Count().Should().Be(7, "the six built-ins and its own");
    }

    [Fact]
    public async Task A_built_in_and_an_own_scorecard_are_read_by_id_and_a_built_in_cannot_be_changed()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth", required: true));

        var builtIn = (await owner.GetAsync("/v1/scorecard-templates/interview")).Expect(HttpStatusCode.OK);
        var own = (await owner.GetAsync($"/v1/scorecard-templates/{key}")).Expect(HttpStatusCode.OK);
        var edit = await owner.PutAsync("/v1/scorecard-templates/interview", new JsonObject { ["name"] = "Mine", ["questions"] = Questions(Rating5("depth")) });

        builtIn.At("builtIn").GetBoolean().Should().BeTrue();
        builtIn.Text("questions.0.id").Should().Be("role_skills");
        builtIn.Text("questions.0.type").Should().Be("Rating5");
        own.At("builtIn").GetBoolean().Should().BeFalse();
        own.Text("name").Should().Be("Platform interview");
        own.At("questions.0.required").GetBoolean().Should().BeTrue();
        edit.Expect(HttpStatusCode.Conflict).Text("detail").Should().Contain("read only");
        (await owner.GetAsync("/v1/scorecard-templates/interview")).Text("name").Should().Be("Interview");
        (await owner.GetAsync($"/v1/scorecard-templates/{Guid.NewGuid():D}")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/scorecard-templates/not-a-scorecard")).Expect(HttpStatusCode.NotFound);
        (await owner.PutAsync($"/v1/scorecard-templates/{Guid.NewGuid():D}", new JsonObject { ["name"] = "Ghost", ["questions"] = Questions(Rating5("depth")) }))
            .Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_renaming_and_changing_the_questions_of_a_scorecard_are_recorded_in_its_history_and_an_identical_save_is_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));
        var url = $"/v1/scorecard-templates/{key}";

        (await owner.PutAsync(url, new JsonObject { ["name"] = "Platform review", ["questions"] = Questions(Rating5("depth")) })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync(url, new JsonObject { ["name"] = "Platform review", ["questions"] = Questions(Rating5("depth")) })).Expect(HttpStatusCode.OK);
        var changed = (await owner.PutAsync(url, new JsonObject
        {
            ["name"] = "Platform review", ["questions"] = Questions(Rating5("depth"), Question("notes", "LongText", "Notes")),
        })).Expect(HttpStatusCode.OK);
        var history = (await owner.GetAsync($"{url}/history")).Expect(HttpStatusCode.OK);

        history.Count("entries").Should().Be(3, "the identical save changed nothing and recorded nothing");
        history.Text("entries.0.lines.0.text").Should().Be("changed the questions");
        history.Text("entries.1.lines.0.text").Should().Be("renamed the scorecard from Platform interview to Platform review");
        history.Text("entries.2.lines.0.text").Should().Be("created the scorecard");
        history.Text("entries.0.by").Should().Be("Member");
        history.Id("entries.0.userId").Should().Be(owner.UserId);
        history.At("entries.0.at").GetDateTimeOffset().Should().BeOnOrAfter(history.At("entries.2.at").GetDateTimeOffset());
        changed.Text("lastUpdated.by").Should().Be("Member");
        changed.Count("questions").Should().Be(2);
        (await owner.GetAsync("/v1/scorecard-templates/interview/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);
    }

    [Fact]
    public async Task A_scorecard_copied_from_another_records_where_it_came_from_and_an_unknown_source_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var interview = (await owner.GetAsync("/v1/scorecard-templates/interview")).Expect(HttpStatusCode.OK);

        var copy = (await owner.PostAsync("/v1/scorecard-templates", new JsonObject
        {
            ["name"] = "Our interview", ["questions"] = JsonNode.Parse(interview.At("questions").GetRawText()), ["copiedFrom"] = "interview",
        })).Expect(HttpStatusCode.Created);
        var unknown = await owner.PostAsync("/v1/scorecard-templates", new JsonObject
        {
            ["name"] = "Ghost copy", ["questions"] = Questions(Rating5("depth")), ["copiedFrom"] = Guid.NewGuid().ToString("D"),
        });
        var malformed = await owner.PostAsync("/v1/scorecard-templates", new JsonObject
        {
            ["name"] = "Ghost copy", ["questions"] = Questions(Rating5("depth")), ["copiedFrom"] = "nonsense",
        });

        copy.Text("copiedFrom").Should().Be("interview");
        copy.Count("questions").Should().Be(3);
        (await owner.GetAsync($"/v1/scorecard-templates/{copy.Text("id")}/history")).Text("entries.0.lines.0.text")
            .Should().Be("created the scorecard from Interview");
        unknown.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("copiedFrom");
        malformed.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("copiedFrom");
    }

    [Fact]
    public async Task A_scorecard_with_no_name_no_questions_a_repeated_id_or_options_on_a_rating_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));

        async Task<ApiResult> PostAsync(string name, JsonNode questions) =>
            await owner.PostAsync("/v1/scorecard-templates", new JsonObject { ["name"] = name, ["questions"] = questions });

        var noName = await PostAsync("  ", Questions(Rating5("depth")));
        var noQuestions = await PostAsync("Empty", new JsonArray());
        var repeated = await PostAsync("Repeated", Questions(Rating5("depth"), Rating5("depth")));
        var optionsOnRating = await PostAsync("Options", Questions(Question("depth", "Rating5", "Depth", false, "a", "b")));
        var choiceWithoutOptions = await PostAsync("Choice", Questions(Question("pick", "SingleChoice", "Pick one")));
        var update = await owner.PutAsync($"/v1/scorecard-templates/{key}", new JsonObject { ["name"] = "Platform interview", ["questions"] = new JsonArray() });

        noName.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("name");
        noQuestions.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("questions");
        repeated.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("questions[1].id");
        optionsOnRating.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("questions[0].options");
        choiceWithoutOptions.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("questions[0].options");
        update.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("questions");
        (await owner.GetAsync("/v1/scorecard-templates")).Count().Should().Be(7, "nothing was stored by a refused request");
    }

    [Fact]
    public async Task Another_organization_gets_404_for_a_scorecard_its_history_and_its_edits_and_cannot_name_or_copy_it()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(ours, "Platform interview", Rating5("depth"));
        var edit = new JsonObject { ["name"] = "Taken over", ["questions"] = Questions(Rating5("depth")) };

        (await theirs.GetAsync($"/v1/scorecard-templates/{key}")).Expect(HttpStatusCode.NotFound);
        (await theirs.GetAsync($"/v1/scorecard-templates/{key}/history")).Expect(HttpStatusCode.NotFound);
        (await theirs.PutAsync($"/v1/scorecard-templates/{key}", edit)).Expect(HttpStatusCode.NotFound);
        var copy = await theirs.PostAsync("/v1/scorecard-templates", new JsonObject { ["name"] = "Copy", ["questions"] = Questions(Rating5("depth")), ["copiedFrom"] = key });
        var named = await theirs.PostAsync("/v1/templates", new JsonObject { ["name"] = "Pipeline", ["definition"] = DefinitionNaming("interview", key) });

        copy.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("copiedFrom");
        named.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecardTemplateId");
        (await ours.GetAsync($"/v1/scorecard-templates/{key}")).Text("name").Should().Be("Platform interview");
        (await ours.GetAsync($"/v1/scorecard-templates/{key}/history")).Count("entries").Should().Be(1, "their edit recorded nothing");
    }

    [Fact]
    public async Task A_viewer_reads_the_library_but_cannot_create_or_change_a_scorecard()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));

        (await viewer.GetAsync("/v1/scorecard-templates")).Expect(HttpStatusCode.OK).Count().Should().Be(7);
        (await viewer.GetAsync($"/v1/scorecard-templates/{key}")).Expect(HttpStatusCode.OK);
        (await viewer.GetAsync($"/v1/scorecard-templates/{key}/history")).Expect(HttpStatusCode.OK);
        (await viewer.PostAsync("/v1/scorecard-templates", new JsonObject { ["name"] = "Mine", ["questions"] = Questions(Rating5("depth")) }))
            .Expect(HttpStatusCode.Forbidden);
        (await viewer.PutAsync($"/v1/scorecard-templates/{key}", new JsonObject { ["name"] = "Mine", ["questions"] = Questions(Rating5("depth")) }))
            .Expect(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"/v1/scorecard-templates/{key}")).Text("name").Should().Be("Platform interview");
    }

    [Fact]
    public async Task A_hiring_template_names_its_scorecards_and_an_inline_copy_or_a_missing_scorecard_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"));
        var inline = Samples.Definition();
        inline["workflow"]!["stages"]!.AsArray()[2]!["scorecard"] = new JsonObject { ["name"] = "Inline", ["questions"] = Questions(Rating5("depth")) };
        var missing = DefinitionNaming("interview", Guid.NewGuid().ToString("D"));
        var malformed = DefinitionNaming("interview", "nonsense");

        var created = (await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Pipeline", ["definition"] = DefinitionNaming("interview", key) }))
            .Expect(HttpStatusCode.Created);
        var templateId = created.Id();

        (await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Inline", ["definition"] = inline.DeepClone() })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecard");
        (await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Missing", ["definition"] = missing.DeepClone() })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecardTemplateId");
        (await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Malformed", ["definition"] = malformed })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecardTemplateId");
        (await owner.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = missing.DeepClone() })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecardTemplateId");
        (await owner.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject { ["definition"] = inline.DeepClone() })).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("definition.workflow.stages[2].scorecard");
        var stored = await owner.GetAsync($"/v1/templates/{templateId}/versions/1");
        stored.Text("definition.workflow.stages.2.scorecardTemplateId").Should().Be(key);
        stored.At("definition.workflow.stages.2.scorecard").ValueKind.Should().Be(JsonValueKind.Null);
        (await owner.PostAsync($"/v1/templates/{templateId}/versions/1/publish")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_job_copies_the_questions_its_template_names_and_keeps_them_when_the_library_scorecard_changes()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Platform interview", Rating5("depth"), Question("notes", "ShortText", "Notes"));
        var (_, versionId) = await owner.CreatePublishedTemplateAsync("Platform pipeline", DefinitionNaming("interview", key));

        var first = await JobFromVersionAsync(owner, "Platform Engineer", versionId);
        var firstApplication = (await owner.CreateApplicationAsync(first.Id(), ApiSession.NewEmail("candidate"))).Id();
        (await owner.PutAsync($"/v1/scorecard-templates/{key}", new JsonObject
        {
            ["name"] = "Platform interview v2", ["questions"] = Questions(Rating5("design")),
        })).Expect(HttpStatusCode.OK);
        var afterEdit = (await owner.GetAsync($"/v1/jobs/{first.Id()}")).Expect(HttpStatusCode.OK);
        var second = await JobFromVersionAsync(owner, "Platform Engineer II", versionId);
        var secondApplication = (await owner.CreateApplicationAsync(second.Id(), ApiSession.NewEmail("candidate"))).Id();

        first.Text("workflow.stages.2.scorecardTemplateId").Should().Be(key);
        first.Text("workflow.stages.2.scorecard.name").Should().Be("Platform interview");
        first.Text("workflow.stages.2.scorecard.questions.0.id").Should().Be("depth");
        first.Text("workflow.stages.2.scorecard.questions.1.id").Should().Be("notes");
        afterEdit.Text("workflow.stages.2.scorecard.name").Should().Be("Platform interview", "the job keeps the copy it took");
        afterEdit.Count("workflow.stages.2.scorecard.questions").Should().Be(2);
        afterEdit.Text("workflow.stages.2.scorecard.questions.0.id").Should().Be("depth");
        second.Text("workflow.stages.2.scorecard.name").Should().Be("Platform interview v2", "a job created afterwards takes the new questions");
        second.Count("workflow.stages.2.scorecard.questions").Should().Be(1);
        second.Text("workflow.stages.2.scorecard.questions.0.id").Should().Be("design");

        // The questions a candidate's reviewers answer are the job's copy.
        foreach (var application in new[] { firstApplication, secondApplication })
        {
            (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        }

        (await owner.PostAsync($"/v1/applications/{firstApplication}/scorecards", Submission("interview", "Yes", Answer("design", rating: 4))))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("answers[0]");
        (await owner.PostAsync($"/v1/applications/{firstApplication}/scorecards", Submission("interview", "Yes", Answer("depth", rating: 4))))
            .Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{secondApplication}/scorecards", Submission("interview", "Yes", Answer("depth", rating: 4))))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("answers[0]");
        (await owner.PostAsync($"/v1/applications/{secondApplication}/scorecards", Submission("interview", "Yes", Answer("design", rating: 4))))
            .Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_job_workflow_edit_that_names_a_scorecard_without_a_copy_gets_the_servers_copy_and_a_missing_scorecard_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var key = await CreateScorecardAsync(owner, "Offer review", Rating5("fairness"), Question("terms", "LongText", "Terms"));
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();

        async Task<ApiResult> NameAsync(string scorecardKey)
        {
            var current = (await owner.GetAsync($"/v1/jobs/{job}")).Expect(HttpStatusCode.OK);
            var workflow = JsonNode.Parse(current.At("workflow").GetRawText())!.AsObject();
            var offer = workflow["stages"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == "offer")!.AsObject();
            offer["scorecardTemplateId"] = scorecardKey;
            offer["scorecard"] = null;
            return await owner.PutAsync($"/v1/jobs/{job}/workflow", new JsonObject
            {
                ["workflow"] = workflow, ["expectedWorkflowVersion"] = current.At("workflowVersion").GetInt32(),
            });
        }

        var missing = await NameAsync(Guid.NewGuid().ToString("D"));
        var saved = (await NameAsync(key)).Expect(HttpStatusCode.OK);

        missing.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("workflow.stages[3].scorecardTemplateId");
        saved.Text("workflow.stages.3.id").Should().Be("offer");
        saved.Text("workflow.stages.3.scorecardTemplateId").Should().Be(key);
        saved.Text("workflow.stages.3.scorecard.name").Should().Be("Offer review");
        saved.Text("workflow.stages.3.scorecard.questions.0.id").Should().Be("fairness");
        saved.Text("workflow.stages.3.scorecard.questions.1.id").Should().Be("terms");
    }
}
