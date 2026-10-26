using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Demo;

/// <summary>
/// The demo's scorecards (workflow-engine.md section 8): Brightwater's own "Technical interview" scorecard, with every
/// answer kind, on the .NET job's Interview stage, and scorecards that Avery Quinn and Jordan Ellis gave on the default
/// workflow's built-in Screening and Interview scorecards, so the job tables show ratings and "Not rated yet". Avery also
/// has a review to give on Priya Natarajan, so signed in as Avery her rating reads "Rating after your scorecard".
/// </summary>
internal static class DemoScorecards
{
    /// <summary>The team's own library scorecard, on the .NET job's Interview stage.</summary>
    public static ScorecardTemplate TechnicalInterview(Guid organizationId, DateTimeOffset now) => ScorecardTemplate.Create(
        organizationId,
        "Technical interview",
        [
            new("problem_solving", ScorecardQuestionType.Rating5, "Problem solving", Required: true,
                Guidance: "Breaks the exercise into steps and explains the trade-offs."),
            new("dotnet", ScorecardQuestionType.Rating10, ".NET and C#", Required: true,
                Guidance: "Uses the language and the framework well, and knows why as well as how."),
            new("readable", ScorecardQuestionType.RatingYesNo, "Is the code readable, with tests where they matter?", Required: true,
                Guidance: "Readable names, tests where they matter, no dead code."),
            new("finished", ScorecardQuestionType.YesNo, "Did they finish the exercise in the time?",
                Guidance: "Within the 90 minutes, with no extra time."),
            new("level", ScorecardQuestionType.SingleChoice, "Level the work shows",
                Options: [new("mid", "Mid"), new("senior", "Senior"), new("staff", "Staff")]),
            new("next_interview", ScorecardQuestionType.MultiChoice, "What should the next interview cover?",
                Options: [new("system_design", "System design"), new("testing", "Testing"), new("databases", "Databases"), new("front_end", "Front end")]),
            new("summary", ScorecardQuestionType.ShortText, "The work in a sentence"),
            new("notes", ScorecardQuestionType.LongText, "Notes on the exercise"),
        ],
        now);

    /// <summary>The scorecards people gave, by candidate email, oldest first.</summary>
    public static IReadOnlyList<DemoScorecard> All { get; } =
    [
        new("hannah.okoro@example.com", "interview", DevelopmentDataSeeder.DemoRecruiterEmail, Recommendation.Yes,
            "Solid on SvelteKit routing; less sure about testing the load functions.",
            Ratings(("role_skills", 4), ("problem_solving", 4), ("work_knowledge", 3))),
        new("hannah.okoro@example.com", "interview", DevelopmentDataSeeder.DemoOwnerEmail, Recommendation.StrongYes,
            "Walked through the booking calendar's accessibility choices clearly.",
            Ratings(("role_skills", 5), ("problem_solving", 5), ("work_knowledge", 4))),
        new("eli.thompson@example.org", "screening", DevelopmentDataSeeder.DemoRecruiterEmail, Recommendation.Yes,
            "Early in their career, with a shipped web shop to show.",
            Ratings(("role_requirements", 4), ("relevant_experience", 3))),
        new("priya.natarajan@example.com", "interview", DevelopmentDataSeeder.DemoRecruiterEmail, Recommendation.Yes,
            "Strong research story; the portfolio review ran short.",
            Ratings(("role_skills", 3), ("problem_solving", 4), ("work_knowledge", 3))),
    ];

    /// <summary>The candidate Avery Quinn has a review to give on, in this stage, so her rating is held back from Avery.</summary>
    public const string ReviewForOwner = "priya.natarajan@example.com";

    public const string ReviewForOwnerStage = "interview";

    private static Dictionary<string, ScorecardAnswerValue> Ratings(params (string QuestionId, int Rating)[] ratings) =>
        ratings.ToDictionary(r => r.QuestionId, r => new ScorecardAnswerValue(Rating: r.Rating), StringComparer.Ordinal);
}

/// <summary>One person's scorecard for one demo candidate's stage.</summary>
internal sealed record DemoScorecard(
    string CandidateEmail, string StageId, string ReviewerEmail, Recommendation Recommendation, string Note,
    IReadOnlyDictionary<string, ScorecardAnswerValue> Answers);
