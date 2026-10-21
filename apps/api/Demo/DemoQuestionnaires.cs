using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Demo;

/// <summary>
/// Questionnaires for the seeded jobs that <see cref="DevelopmentDataSeeder"/> creates without a template, as a recruiter
/// would write them on the dashboard. The .NET job and the jobs from Brightwater's template already ask the "Software
/// engineer" questions. Question ids are stable: the candidates' answers in <see cref="DemoCandidates"/> name them.
/// </summary>
internal static class DemoQuestionnaires
{
    public const string CustomerSuccessLead = "customer-success-lead";

    /// <summary>The questionnaire each job gets, by slug.</summary>
    public static IReadOnlyList<(string Slug, Questionnaire Questionnaire)> All { get; } =
    [
        (DemoCandidates.ProductDesigner, new(
        [
            RightToWork,
            Experience("How many years have you worked in product or UX design?"),
            new("portfolio", QuestionType.Url, "A link to your portfolio", Required: true,
                HelpText: "A site, a PDF or a case study we can open without signing in."),
            new("research_story", QuestionType.LongText, "Tell us about a design you changed because of what research or testing showed.",
                Required: true, MaxLength: 1500),
            NoticePeriod,
        ])),
        (DemoCandidates.FrontendEngineer, new(
        [
            RightToWork,
            Experience("How many years have you worked as a frontend engineer?"),
            new("svelte", QuestionType.SingleChoice, "How have you used Svelte?", Required: true,
                Options: [new("not_yet", "Not yet"), new("side_projects", "In side projects"), new("production", "In production")]),
            new("london_office", QuestionType.YesNo, "Can you work from our London office two days a week?", Required: true),
            new("work_sample", QuestionType.Url, "A link to code or a project you are proud of",
                HelpText: "A repository, a portfolio or a write-up. Optional."),
            NoticePeriod,
        ])),
        (CustomerSuccessLead, new(
        [
            RightToWork,
            new("manchester_office", QuestionType.YesNo, "Can you work from our Manchester office every weekday?", Required: true),
            new("accounts", QuestionType.Number, "How many customer accounts have you looked after at once?", Min: 0, Max: 1000),
            new("difficult_renewal", QuestionType.LongText, "Tell us about a customer you kept who was about to leave.",
                Required: true, MaxLength: 1500),
            NoticePeriod,
        ])),
    ];

    // The "Software engineer" template's wording, so every Brightwater job asks it the same way. Expression-bodied, not
    // initialized: All above is built first.
    private static Question RightToWork => new(
        "work_authorization", QuestionType.YesNo, "Do you have the right to work in the country this job is in?", Required: true,
        Knockout: new(DisqualifyingAnswer: false, "This role needs the right to work in the country it is based in."));

    private static Question NoticePeriod => new("notice_period", QuestionType.ShortText, "What is your notice period?", MaxLength: 100);

    // The template's experience bands, so answers read the same across jobs.
    private static Question Experience(string label) => new(
        "experience", QuestionType.SingleChoice, label, Required: true,
        Options: [new("less_than_3", "Less than 3"), new("three_to_five", "3 to 5"), new("six_to_nine", "6 to 9"), new("ten_plus", "10 or more")]);
}
