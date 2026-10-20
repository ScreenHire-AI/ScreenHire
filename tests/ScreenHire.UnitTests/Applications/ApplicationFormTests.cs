using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Applications;

public sealed class ApplicationFormTests
{
    private static readonly Question WorkAuthorization = new(
        "work_authorization", QuestionType.YesNo, "Right to work?", Required: true, Knockout: new Knockout(DisqualifyingAnswer: false));

    private static readonly Question Experience = new(
        "experience", QuestionType.SingleChoice, "Experience", Required: true,
        Options: [new("junior", "Junior"), new("senior", "Senior")]);

    private static readonly Question Shifts = new(
        "shifts", QuestionType.MultiChoice, "Shifts", Options: [new("mornings", "Mornings"), new("evenings", "Evenings")]);

    private static readonly Question Years = new("years", QuestionType.Number, "Years", Min: 0, Max: 50);

    private static readonly Question Portfolio = new("portfolio", QuestionType.Url, "Portfolio");

    private static readonly Question Notice = new("notice", QuestionType.ShortText, "Notice period", MaxLength: 10);

    private static readonly Question Cv = new("resume", QuestionType.File, "CV", Required: true, AcceptedFileTypes: [".pdf"]);

    private static readonly IReadOnlyList<Question> All = [Cv, WorkAuthorization, Experience, Shifts, Years, Portfolio, Notice];

    [Fact]
    public void The_resume_comes_first_and_a_job_without_one_gets_an_optional_one()
    {
        var withOwn = HiringWorkflow.Default with { Questionnaire = new Questionnaire([WorkAuthorization, Cv]) };
        var without = HiringWorkflow.Default with { Questionnaire = new Questionnaire([WorkAuthorization]) };

        ApplicationForm.QuestionsFor(withOwn).Should().Equal(Cv, WorkAuthorization);
        ApplicationForm.QuestionsFor(without).Should().Equal(ApplicationForm.DefaultResumeQuestion, WorkAuthorization);
        ApplicationForm.DefaultResumeQuestion.Required.Should().BeFalse();
    }

    [Fact]
    public void Valid_answers_pass_including_a_knockout_answer()
    {
        ApplicationAnswer[] answers =
        [
            new("resume", Guid.NewGuid().ToString()),
            new("work_authorization", "no"),
            new("experience", "senior"),
            new("shifts", Values: ["mornings", "evenings"]),
            new("years", "7.5"),
            new("portfolio", "https://example.com/work"),
            new("notice", "1 month"),
        ];

        ApplicationForm.Validate(All, answers).Should().BeEmpty();
    }

    [Fact]
    public void Missing_required_answers_are_reported_per_question()
    {
        var errors = ApplicationForm.Validate(All, [new("experience", "junior")]);

        errors.Should().BeEquivalentTo([
            new ApplicationFormError("resume", "Add a file."),
            new ApplicationFormError("work_authorization", "Answer this question."),
        ]);
    }

    [Theory]
    [InlineData("work_authorization", "maybe")]
    [InlineData("experience", "principal")]
    [InlineData("years", "lots")]
    [InlineData("years", "51")]
    [InlineData("years", "-1")]
    [InlineData("portfolio", "javascript:alert(1)")]
    [InlineData("portfolio", "example.com")]
    [InlineData("notice", "three months")]
    [InlineData("resume", "not-an-upload-id")]
    public void An_answer_of_the_wrong_shape_is_reported(string questionId, string value)
    {
        var errors = ApplicationForm.Validate(All, [new(questionId, value)]);

        errors.Should().Contain(e => e.QuestionId == questionId);
    }

    [Fact]
    public void Unknown_repeated_and_misplaced_answers_are_reported()
    {
        ApplicationAnswer[] answers =
        [
            new("favourite_colour", "teal"),
            new("experience", "junior"),
            new("experience", "senior"),
            new("shifts", Values: ["mornings", "mornings"]),
            new("years", Values: ["1"]),
        ];

        var errors = ApplicationForm.Validate(All, answers);

        errors.Select(e => e.QuestionId).Should().Contain(["favourite_colour", "experience", "shifts", "years"]);
    }

    [Fact]
    public void Normalizing_drops_empty_answers_and_trims_text()
    {
        var normalized = ApplicationForm.Normalize([new("notice", "  2 weeks "), new("portfolio", "   "), new("shifts", Values: [])]);

        normalized.Should().BeEquivalentTo([new ApplicationAnswer("notice", "2 weeks")]);
    }

    [Fact]
    public void Text_limits_are_the_questions_own_within_the_type_ceiling()
    {
        ApplicationForm.MaxLengthOf(Notice).Should().Be(10);
        ApplicationForm.MaxLengthOf(new Question("q", QuestionType.ShortText, "Q")).Should().Be(ApplicationForm.ShortTextMaxLength);
        ApplicationForm.MaxLengthOf(new Question("q", QuestionType.LongText, "Q", MaxLength: 100_000)).Should().Be(ApplicationForm.LongTextMaxLength);
    }
}
