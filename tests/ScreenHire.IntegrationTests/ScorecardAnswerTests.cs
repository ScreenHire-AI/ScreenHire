using System.Text.Json;
using System.Text.Json.Nodes;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// What a scorecard submission takes, per answer kind (workflow-engine.md section 8): "yes" or "no" for the yes-or-no
/// kinds, an option id for a single choice, option ids for a multi-choice, text for the text kinds and a number in range
/// for the rating scales. A required question must be answered, an answer of the wrong shape is a 400 at that answer, and
/// the same submission again returns the stored scorecard.
/// </summary>
public sealed class ScorecardAnswerTests(AppHostFixture fixture)
{
    // One question of every answer kind, in this order.
    private static readonly JsonObject[] AllKinds =
    [
        Question("yn", "YesNo", "Would they start soon"),
        Question("sc", "SingleChoice", "Strongest area", false, "a", "b", "c"),
        Question("mc", "MultiChoice", "Seen in the exercise", false, "x", "y", "z"),
        Question("st", "ShortText", "In a few words"),
        Question("lt", "LongText", "In detail"),
        Question("r5", "Rating5", "Depth of experience"),
        Question("r10", "Rating10", "Problem solving"),
        Question("ryn", "RatingYesNo", "Meets the requirements"),
    ];

    [Fact]
    public async Task Every_answer_kind_is_submitted_and_comes_back_as_given_with_the_scorecards_own_rating()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (application, url) = await InInterviewAsync(owner, AllKinds);

        var created = (await owner.PostAsync(url, Submission("interview", "Yes",
            Answer("yn", "yes"),
            Answer("sc", "b"),
            Answer("mc", values: ["x", "z"]),
            Answer("st", "  Clear on trade-offs.  "),
            Answer("lt", "Walked through the migration plan step by step."),
            Answer("r5", rating: 4),
            Answer("r10", rating: 4),
            Answer("ryn", "yes")))).Expect(HttpStatusCode.Created);
        var listed = (await owner.GetAsync(url)).Expect(HttpStatusCode.OK);

        created.Count("answers").Should().Be(8, "one answer per question, in the scorecard's order");
        created.Body.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("questionId").GetString())
            .Should().Equal("yn", "sc", "mc", "st", "lt", "r5", "r10", "ryn");
        created.Text("answers.0.type").Should().Be("YesNo");
        created.Text("answers.0.value").Should().Be("yes");
        created.Text("answers.0.label").Should().Be("Would they start soon");
        created.Text("answers.1.value").Should().Be("b");
        created.Text("answers.1.options.1.id").Should().Be("b", "a choice keeps the options as they were asked");
        created.Body.GetProperty("answers")[2].GetProperty("values").EnumerateArray().Select(v => v.GetString()).Should().Equal("x", "z");
        created.Text("answers.3.value").Should().Be("Clear on trade-offs.", "text is kept trimmed");
        created.Text("answers.4.value").Should().Be("Walked through the migration plan step by step.");
        created.At("answers.5.rating").GetInt32().Should().Be(4);
        created.At("answers.6.rating").GetInt32().Should().Be(4);
        created.Text("answers.7.value").Should().Be("yes");
        created.Count("ratings").Should().Be(0);
        // Rating5 4, Rating10 4 (1 + 3 * 4 / 9 = 2.33) and a rating Yes (5): their mean, 3.78, to one decimal.
        created.At("rating").GetDecimal().Should().Be(3.8m);
        listed.Count().Should().Be(1);
        listed.Id("0.id").Should().Be(created.Id());
        listed.At("0.answers").GetRawText().Should().Be(created.At("answers").GetRawText());
        (await owner.GetAsync($"/v1/applications/{application}")).At("rating.value").GetDecimal().Should().Be(3.8m);
    }

    [Fact]
    public async Task An_answer_of_the_wrong_shape_for_its_kind_is_refused_at_that_answer_and_stores_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, url) = await InInterviewAsync(owner, AllKinds);
        var cases = new Dictionary<string, JsonObject>
        {
            ["yes-no says maybe"] = Answer("yn", "maybe"),
            ["yes-no is capitalised"] = Answer("yn", "Yes"),
            ["yes-no is a number"] = Answer("yn", rating: 1),
            ["choice names no option"] = Answer("sc", "zzz"),
            ["choice takes values"] = Answer("sc", values: ["a"]),
            ["multi-choice names no option"] = Answer("mc", values: ["x", "nope"]),
            ["multi-choice repeats an option"] = Answer("mc", values: ["x", "x"]),
            ["multi-choice takes a value"] = Answer("mc", "x"),
            ["short text is too long"] = Answer("st", new string('a', 501)),
            ["short text is a number"] = Answer("st", rating: 3),
            ["rating 1 to 5 is 0"] = Answer("r5", rating: 0),
            ["rating 1 to 5 is 6"] = Answer("r5", rating: 6),
            ["rating 1 to 5 is text"] = Answer("r5", "4"),
            ["rating 1 to 10 is 0"] = Answer("r10", rating: 0),
            ["rating 1 to 10 is 11"] = Answer("r10", rating: 11),
            ["rating yes-no is a number"] = Answer("ryn", rating: 5),
            ["rating yes-no is maybe"] = Answer("ryn", "maybe"),
            ["no such question"] = Answer("nope", "x"),
        };

        var outcomes = new Dictionary<string, string>();
        foreach (var (name, answer) in cases)
        {
            var result = await owner.PostAsync(url, Submission("interview", "Yes", answer));
            outcomes[name] = $"{(int)result.Status} {string.Join(",", result.ProblemKeys())}";
        }

        outcomes.Should().BeEquivalentTo(cases.ToDictionary(c => c.Key, _ => "400 answers[0]"));
        (await owner.GetAsync(url)).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_required_question_must_be_answered_and_a_blank_answer_does_not_count()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, url) = await InInterviewAsync(owner,
            Question("must", "Rating5", "Required rating", required: true),
            Question("why", "ShortText", "Required reason", required: true),
            Question("extra", "LongText", "Anything else"));

        var none = await owner.PostAsync(url, Submission("interview", "Yes"));
        var oneOfTwo = await owner.PostAsync(url, Submission("interview", "Yes", Answer("must", rating: 4)));
        var blank = await owner.PostAsync(url, Submission("interview", "Yes", Answer("must", rating: 4), Answer("why", "   ")));
        var emptyAnswer = await owner.PostAsync(url, Submission("interview", "Yes", Answer("must"), Answer("why", "Solid")));
        var complete = (await owner.PostAsync(url, Submission("interview", "Yes", Answer("must", rating: 4), Answer("why", "Solid")))).Expect(HttpStatusCode.Created);

        none.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().BeEquivalentTo("answers.must", "answers.why");
        oneOfTwo.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("answers.why");
        blank.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("answers[1]");
        emptyAnswer.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("answers[0]");
        complete.At("answers.2.value").ValueKind.Should().Be(JsonValueKind.Null, "an optional question left out stays unanswered");
        (await owner.GetAsync(url)).Count().Should().Be(1);
    }

    [Fact]
    public async Task The_same_answers_again_return_the_stored_scorecard_and_a_changed_answer_or_note_conflicts()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, url) = await InInterviewAsync(owner, Rating5("depth"), Question("st", "ShortText", "In a few words"), Question("mc", "MultiChoice", "Seen", false, "x", "y"));
        var first = Submission("interview", "Yes", Answer("depth", rating: 4), Answer("st", "Good"), Answer("mc", values: ["x", "y"]));
        var created = (await owner.PostAsync(url, first)).Expect(HttpStatusCode.Created);

        var again = (await owner.PostAsync(url, first)).Expect(HttpStatusCode.OK);
        var padded = (await owner.PostAsync(url, Submission("interview", "Yes", Answer("depth", rating: 4), Answer("st", "  Good  "), Answer("mc", values: ["x", "y"]))))
            .Expect(HttpStatusCode.OK);
        var otherRating = await owner.PostAsync(url, Submission("interview", "Yes", Answer("depth", rating: 5), Answer("st", "Good"), Answer("mc", values: ["x", "y"])));
        var otherText = await owner.PostAsync(url, Submission("interview", "Yes", Answer("depth", rating: 4), Answer("st", "Better"), Answer("mc", values: ["x", "y"])));
        var otherOptions = await owner.PostAsync(url, Submission("interview", "Yes", Answer("depth", rating: 4), Answer("st", "Good"), Answer("mc", values: ["x"])));
        var withNote = first.DeepClone().AsObject();
        withNote["note"] = "Added later";
        var otherNote = await owner.PostAsync(url, withNote);

        again.Id().Should().Be(created.Id());
        padded.Id().Should().Be(created.Id(), "the same meaning is the same submission");
        otherRating.Expect(HttpStatusCode.Conflict);
        otherText.Expect(HttpStatusCode.Conflict);
        otherOptions.Expect(HttpStatusCode.Conflict);
        otherNote.Expect(HttpStatusCode.Conflict);
        (await owner.GetAsync(url)).Count().Should().Be(1);
        (await owner.GetAsync(url)).At("0.answers.0.rating").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Answering_a_question_twice_or_giving_an_over_long_note_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, url) = await InInterviewAsync(owner, Rating5("depth"));

        var twice = await owner.PostAsync(url, Submission("interview", "Yes", Answer("depth", rating: 3), Answer("depth", rating: 4)));
        var longNote = Submission("interview", "Yes", Answer("depth", rating: 3));
        longNote["note"] = new string('n', 2001);

        twice.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("answers");
        (await owner.PostAsync(url, longNote)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        (await owner.GetAsync(url)).Count().Should().Be(0);
    }

    // A job whose interview stage asks these questions (an own library scorecard) and an application in Interview.
    private static async Task<(Guid Application, string Url)> InInterviewAsync(Tenant owner, params JsonObject[] questions)
    {
        var key = await CreateScorecardAsync(owner, "Panel review", questions);
        var job = await JobNamingAsync(owner, "Platform Engineer", "interview", key);
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        return (application, $"/v1/applications/{application}/scorecards");
    }
}
