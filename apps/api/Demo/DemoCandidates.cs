using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Api.Demo;

/// <summary>
/// People who applied to Brightwater's jobs in the local demo, and the stages a recruiter has moved each through, so the
/// boards look lived in and every stage of both workflows holds someone. Each answers every question of their job, in
/// words that fit their resume. All fictional (.claude/rules/no-personal-info.md): <c>example.*</c> addresses and links,
/// Ofcom's drama landline ranges and the RFC 5737 home broadband range Development's network settings describe. Each
/// applies from a device of their own and takes minutes, so the integrity checks find nothing to report. Only these people are ever rejected, each with a recruiter's reason: never a synthetic applicant an integrity
/// check flagged (CLAUDE.md non-negotiable 2).
/// </summary>
internal static class DemoCandidates
{
    public const string ProductDesigner = "senior-product-designer";
    public const string FrontendEngineer = "frontend-engineer-svelte";
    public const string SeniorDotnetEngineer = "senior-dotnet-engineer";
    public const string SupportEngineer = "support-engineer";

    public static IReadOnlyList<DemoCandidate> All { get; } =
    [
        new(ProductDesigner, [new("interview")], KeepForFutureRoles: true, Applicant: Person(
            "priya-natarajan", "Priya", "Natarajan", "priya.natarajan@example.com", "+44 20 7946 0311", "192.0.2.31", 17,
            "Priya Natarajan\nProduct designer, nine years. Research-led work on booking and payments products.\nI run design critiques and pair with engineers on prototypes.",
            DesignerAnswers(
                "six_to_nine", "https://priya-natarajan.example.org/portfolio",
                "Venue owners kept abandoning our payout settings, which I had designed as one long form. In five moderated sessions, " +
                "four people stopped at the bank details because a screen asking for everything at once felt unsafe. We split it into " +
                "three short steps with a summary at the end, and completed setups rose by a third the next month.",
                "Three months"))),
        new(ProductDesigner, [new("screening")], Person(
            "tom-whitfield", "Tom", "Whitfield", "tom.whitfield@example.org", "+44 113 496 0322", "192.0.2.32", 12,
            "Tom Whitfield\nSenior UX designer, seven years in SaaS dashboards and design systems.",
            DesignerAnswers(
                "six_to_nine", "https://tomwhitfield.example.org/case-studies",
                "I put twelve charts on the first screen of an analytics dashboard. Watching support staff use it, I saw them look at " +
                "two, and only to answer one question: is anything broken today? We rebuilt that screen around a short status list " +
                "and moved the charts a click away. Support tickets about the dashboard dropped soon after.",
                "One month"))),
        new(ProductDesigner, [], Person(
            "aisha-bello", "Aisha", "Bello", "aisha.bello@example.com", "+44 161 496 0333", "192.0.2.33", 21,
            "Aisha Bello\nInteraction designer moving from agency work to product. Accessibility champion.",
            DesignerAnswers(
                "three_to_five", "https://aishabello.example.org/work",
                "A charity client wanted a donation form with animated steps and a progress bar. With screen reader users, the " +
                "animation announced every change and two testers gave up. I replaced it with a plain heading per step and a line " +
                "of text saying what was left. It also did better with sighted testers on slow phones.",
                "Two weeks"))),
        new(FrontendEngineer, [new("offer", "Offer sent after the panel; waiting on references.")], Person(
            "marcus-lindqvist", "Marcus", "Lindqvist", "marcus.lindqvist@example.org", "+44 20 7946 0344", "192.0.2.34", 15,
            "Marcus Lindqvist\nFrontend engineer, six years. Svelte, TypeScript and design systems.\nI maintain a component library used by four product teams.",
            FrontendAnswers("six_to_nine", "production", "yes", "https://code.example.org/marcus-lindqvist/component-library", "Three months"))),
        new(FrontendEngineer, [new("interview")], KeepForFutureRoles: true, Applicant: Person(
            "hannah-okoro", "Hannah", "Okoro", "hannah.okoro@example.com", "+44 117 496 0355", "192.0.2.35", 11,
            "Hannah Okoro\nFull-stack developer leaning frontend: SvelteKit, Node and Postgres.",
            FrontendAnswers("three_to_five", "production", "yes", "https://hannah-okoro.example.org/projects/booking-calendar", "One month"))),
        new(FrontendEngineer, [new("screening")], Person(
            "eli-thompson", "Eli", "Thompson", "eli.thompson@example.org", "+44 131 496 0366", "192.0.2.36", 9,
            "Eli Thompson\nJunior frontend engineer, two years. Built the web shop for a small bakery chain.",
            FrontendAnswers("less_than_3", "side_projects", "yes", "https://eli-thompson.example.org/bakery-shop", "Two weeks"))),
        new(SeniorDotnetEngineer, [new("screening"), new("technical", "Take-home exercise sent; due Friday.")], Person(
            "joel-mensah", "Joel", "Mensah", "joel.mensah@example.org", "+44 20 7946 0377", "192.0.2.37", 14,
            "Joel Mensah\nBackend engineer, five years of C# and SQL Server, moving to Postgres.\nI look after a billing service.",
            EngineerAnswers("three_to_five", "https://joel-mensah.example.org/billing-ledger", "Four weeks"))),
        // Support Engineer's hiring happened over the weeks before the demo, so Reports has a history to count: when each
        // applied and each move, in days before the demo starts. The job is closed, so no time limit runs on them.
        new(SupportEngineer,
            [
                new("screening", DaysAgo: 39), new("technical", DaysAgo: 35), new("interview", DaysAgo: 30),
                new("offer", "Offer sent after a strong panel.", 26),
                new("hired", "Accepted the offer.", 22),
            ],
            Person(
                "nadia-haddad", "Nadia", "Haddad", "nadia.haddad@example.com", "+44 121 496 0388", "192.0.2.38", 16,
                "Nadia Haddad\nSupport engineer, four years at a card payments provider.\nI write the runbooks our first line uses.",
                EngineerAnswers("three_to_five", "https://nadia-haddad.example.org/runbooks", "Two weeks")),
            AppliedDaysAgo: 41),
        new(SupportEngineer,
            [
                new("screening", DaysAgo: 36), new("technical", DaysAgo: 33), new("interview", DaysAgo: 29),
                new("rejected", "We chose a candidate with more payments support experience.", 27),
            ],
            Person(
                "owen-price", "Owen", "Price", "owen.price@example.org", "+44 141 496 0399", "192.0.2.39", 13,
                "Owen Price\nIT support analyst, two years, moving into software support.",
                EngineerAnswers("less_than_3", "https://owen-price.example.org/helpdesk-scripts", "One week")),
            AppliedDaysAgo: 38),
        new(SupportEngineer,
            [new("screening", DaysAgo: 34), new("technical", DaysAgo: 31), new("withdrawn", "Withdrew: accepted another offer.", 28)],
            Person(
                "lena-fischer", "Lena", "Fischer", "lena.fischer@example.com", "+44 20 7946 0400", "192.0.2.40", 10,
                "Lena Fischer\nTechnical support lead, eight years in SaaS for hospitality.",
                EngineerAnswers("six_to_nine", "https://lena-fischer.example.org/support-playbook", "Three months")),
            AppliedDaysAgo: 36),
        new(SupportEngineer,
            [
                new("screening", DaysAgo: 72), new("technical", DaysAgo: 68), new("interview", DaysAgo: 60),
                new("offer", DaysAgo: 55), new("hired", "Started in the first-line team.", 50),
            ],
            Person(
                "sam-brennan", "Sam", "Brennan", "sam.brennan@example.org", "+44 20 7946 0433", "192.0.2.43", 15,
                "Sam Brennan\nSupport engineer, five years on a point-of-sale help desk.\nI wrote our guide to diagnosing card reader faults.",
                EngineerAnswers("three_to_five", "https://sam-brennan.example.org/card-reader-guide", "One month")),
            AppliedDaysAgo: 75),
        // Added by a recruiter, who met them at a meetup, so Reports shows a second source.
        new(SupportEngineer,
            [new("screening", DaysAgo: 29), new("interview", DaysAgo: 24), new("offer", DaysAgo: 20), new("hired", DaysAgo: 16)],
            Person(
                "ruth-mwangi", "Ruth", "Mwangi", "ruth.mwangi@example.com", "+44 113 496 0444", "192.0.2.44", 12,
                "Ruth Mwangi\nTechnical support specialist, six years with hotel booking software.",
                EngineerAnswers("six_to_nine", "https://ruth-mwangi.example.org/escalation-notes", "Two weeks")),
            AppliedDaysAgo: 30,
            Source: ApplicationSource.Recruiter),
        new(SupportEngineer,
            [new("screening"), new("technical"), new("interview")],
            KeepForFutureRoles: true,
            KeptInMindNote: "Close second to Nadia. Clear runbooks and calm on the incident exercise; think of her for the next support opening.",
            Applicant: Person(
                "grace-adeyemi", "Grace", "Adeyemi", "grace.adeyemi@example.org", "+44 117 496 0411", "192.0.2.41", 15,
                "Grace Adeyemi\nSupport engineer, three years in a payroll software team.\nI built our first on-call handbook.",
                EngineerAnswers("three_to_five", "https://grace-adeyemi.example.org/on-call-handbook", "One month"))),
    ];

    // Answers to every question of the candidate's job: the "Software engineer" template's for the .NET and support jobs,
    // DemoQuestionnaires' for the others. The resume question is answered by the upload.
    private static IReadOnlyList<ApplicationAnswer> EngineerAnswers(string experience, string workSample, string noticePeriod) =>
    [
        new("work_authorization", "yes"),
        new("experience", experience),
        new("work_sample", workSample),
        new("notice_period", noticePeriod),
    ];

    private static IReadOnlyList<ApplicationAnswer> DesignerAnswers(string experience, string portfolio, string researchStory, string noticePeriod) =>
    [
        new("work_authorization", "yes"),
        new("experience", experience),
        new("portfolio", portfolio),
        new("research_story", researchStory),
        new("notice_period", noticePeriod),
    ];

    private static IReadOnlyList<ApplicationAnswer> FrontendAnswers(
        string experience, string svelte, string londonOffice, string workSample, string noticePeriod) =>
    [
        new("work_authorization", "yes"),
        new("experience", experience),
        new("svelte", svelte),
        new("london_office", londonOffice),
        new("work_sample", workSample),
        new("notice_period", noticePeriod),
    ];

    private static SyntheticApplicant Person(
        string key, string firstName, string lastName, string email, string phone, string ip, int minutes, string resume,
        IReadOnlyList<ApplicationAnswer> answers) => new(
        key,
        firstName,
        lastName,
        email,
        phone,
        ip,
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        TimeSpan.FromMinutes(minutes),
        AutomationMarkers: [],
        HoneypotFilled: false,
        DeviceFingerprint: $"device-{key}",
        AiUseDeclaration.DidNotUse,
        AiUseDetails: null,
        $"{firstName} {lastName} CV.pdf",
        resume,
        Answers: answers,
        ResumeProducer: "Microsoft Word for Microsoft 365");
}

/// <summary>
/// An applicant to one of Brightwater's jobs and the moves a recruiter made, in order, each with an optional note. Most
/// apply as the demo starts; <see cref="AppliedDaysAgo"/> dates one in the past, with its moves, for Reports.
/// </summary>
/// <remarks>
/// <c>KeepForFutureRoles</c>: they ticked "Keep my details for future roles" when they applied. <c>KeptInMindNote</c>:
/// after the moves, the team kept them in mind with this note, so the talent pool has someone in it.
/// </remarks>
internal sealed record DemoCandidate(
    string JobSlug,
    IReadOnlyList<DemoMove> Moves,
    SyntheticApplicant Applicant,
    int AppliedDaysAgo = 0,
    ApplicationSource Source = ApplicationSource.CareersSite,
    bool KeepForFutureRoles = false,
    string? KeptInMindNote = null)
{
    /// <summary>The stage the candidate ends in: the last move's, or "applied" when nobody moved them.</summary>
    public string StageId => Moves.Count == 0 ? "applied" : Moves[^1].StageId;
}

/// <summary>
/// One move to a stage of the job's workflow, as a recruiter makes it on the dashboard: as the demo starts, or
/// <see cref="DaysAgo"/> days before it.
/// </summary>
internal sealed record DemoMove(string StageId, string? Note = null, int DaysAgo = 0);
