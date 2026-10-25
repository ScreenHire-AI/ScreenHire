namespace ScreenHire.Domain.Hiring;

/// <summary>
/// ScreenHire's own scorecards, in every organization's library next to its own (<see cref="ScorecardTemplate"/>).
/// They live here rather than in a table, as the built-in email templates do (<see cref="EmailTemplateIds"/>), and they
/// are read only: a team copies one to change it. A hiring template's stage names one by its id; a job copies its
/// questions, so changing a built-in here changes the jobs created afterwards and never a job that has its copy.
/// </summary>
/// <remarks>
/// Each is a v0.2 default scorecard as 1 to 5 rating questions. Migration <c>AddScorecardTemplates</c> matches stored v0.2
/// scorecards against these questions to point stages at them, so the questions must stay as they are until a later
/// migration says otherwise.
/// </remarks>
public static class BuiltInScorecards
{
    public static BuiltInScorecard Screening { get; } = new("screening", "Screening",
    [
        Rating("role_requirements", "Meets the role's requirements", "The experience, qualifications or licences the job description asks for."),
        Rating("relevant_experience", "Relevant experience", "Work they have done that is like this job's work."),
    ]);

    public static BuiltInScorecard Interview { get; } = new("interview", "Interview",
    [
        Rating("role_skills", "Skills the role needs", "The skills in the job description, shown in examples or answers."),
        Rating("problem_solving", "Problem solving", "How they work through a problem taken from this job's work."),
        Rating("work_knowledge", "Knowledge of the work", "What they know about the tools, methods or rules this job uses."),
    ]);

    public static BuiltInScorecard TechnicalExercise { get; } = new("technical-exercise", "Technical exercise",
    [
        Rating("exercise_solution", "Solves the exercise", "Whether the solution does what the exercise asked."),
        Rating("code_quality", "Code quality", "Readable, tested and well-structured code."),
        Rating("design_choices", "Design choices", "The trade-offs they made and why they suit the problem."),
    ]);

    public static BuiltInScorecard EngineeringInterview { get; } = new("engineering-interview", "Engineering interview",
    [
        Rating("role_skills", "Engineering skills the role needs", "The skills in the job description, shown in examples or answers."),
        Rating("system_design", "System design", "How they would design a system like the ones this team builds."),
        Rating("problem_solving", "Problem solving", "How they work through a problem taken from this job's work."),
    ]);

    public static BuiltInScorecard PhoneScreen { get; } = new("phone-screen", "Phone screen",
    [
        Rating("role_requirements", "Meets the role's requirements", "Licences, certificates or experience the job description asks for."),
        Rating("relevant_experience", "Relevant experience", "Work they have done that is like this job's tasks."),
    ]);

    public static BuiltInScorecard HourlyRoleInterview { get; } = new("hourly-interview", "Hourly role interview",
    [
        Rating("role_skills", "Skills the role needs", "The tasks of the job, shown in examples or a short trial."),
        Rating("safe_working", "Knows how to work safely", "The safety steps this job's tasks need."),
        Rating("relevant_experience", "Relevant experience", "Work they have done that is like this job's tasks."),
    ]);

    /// <summary>Every built-in, in the order a library lists them.</summary>
    public static IReadOnlyList<BuiltInScorecard> All { get; } =
        [Screening, Interview, TechnicalExercise, EngineeringInterview, PhoneScreen, HourlyRoleInterview];

    /// <summary>The built-in with <paramref name="id"/>, or null.</summary>
    public static BuiltInScorecard? Find(string? id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// Whether <paramref name="key"/> can name a library scorecard: a built-in's id, or an organization's own scorecard
    /// by its id in the "D" format. Whether that scorecard exists is the API's check, which can see the library.
    /// </summary>
    public static bool IsWellFormed(string? key) =>
        Find(key) is not null || (Guid.TryParseExact(key, "D", out var id) && id != Guid.Empty);

    private static ScorecardQuestion Rating(string id, string label, string guidance) =>
        new(id, ScorecardQuestionType.Rating5, label, Guidance: guidance);
}

/// <summary>One of ScreenHire's own scorecards.</summary>
public sealed record BuiltInScorecard(string Id, string Name, IReadOnlyList<ScorecardQuestion> Questions)
{
    /// <summary>A job's copy of this scorecard.</summary>
    public Scorecard ToScorecard() => new(Name, Questions);
}
