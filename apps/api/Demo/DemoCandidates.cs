using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Api.Demo;

/// <summary>
/// People who applied to Brightwater's jobs in the local demo, and the stages a recruiter has moved each through, so the
/// boards look lived in and every stage of both workflows holds someone. All fictional (.claude/rules/no-personal-info.md):
/// <c>example.*</c> addresses, Ofcom's drama landline ranges and the RFC 5737 home broadband range Development's network
/// settings describe. Each applies from a device of their own and takes minutes, so the integrity checks find nothing to
/// report. Only these people are ever rejected, each with a recruiter's reason: never a synthetic applicant an integrity
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
        new(ProductDesigner, [new("interview")], Person(
            "priya-natarajan", "Priya Natarajan", "priya.natarajan@example.com", "+44 20 7946 0311", "192.0.2.31", 17,
            "Priya Natarajan\nProduct designer, nine years. Research-led work on booking and payments products.\nI run design critiques and pair with engineers on prototypes.")),
        new(ProductDesigner, [new("screening")], Person(
            "tom-whitfield", "Tom Whitfield", "tom.whitfield@example.org", "+44 113 496 0322", "192.0.2.32", 12,
            "Tom Whitfield\nSenior UX designer, seven years in SaaS dashboards and design systems.")),
        new(ProductDesigner, [], Person(
            "aisha-bello", "Aisha Bello", "aisha.bello@example.com", "+44 161 496 0333", "192.0.2.33", 21,
            "Aisha Bello\nInteraction designer moving from agency work to product. Accessibility champion.")),
        new(FrontendEngineer, [new("offer", "Offer sent after the panel; waiting on references.")], Person(
            "marcus-lindqvist", "Marcus Lindqvist", "marcus.lindqvist@example.org", "+44 20 7946 0344", "192.0.2.34", 15,
            "Marcus Lindqvist\nFrontend engineer, six years. Svelte, TypeScript and design systems.\nI maintain a component library used by four product teams.")),
        new(FrontendEngineer, [new("interview")], Person(
            "hannah-okoro", "Hannah Okoro", "hannah.okoro@example.com", "+44 117 496 0355", "192.0.2.35", 11,
            "Hannah Okoro\nFull-stack developer leaning frontend: SvelteKit, Node and Postgres.")),
        new(FrontendEngineer, [new("screening")], Person(
            "eli-thompson", "Eli Thompson", "eli.thompson@example.org", "+44 131 496 0366", "192.0.2.36", 9,
            "Eli Thompson\nJunior frontend engineer, two years. Built the web shop for a small bakery chain.")),
        new(SeniorDotnetEngineer, [new("screening"), new("technical", "Take-home exercise sent; due Friday.")], Person(
            "joel-mensah", "Joel Mensah", "joel.mensah@example.org", "+44 20 7946 0377", "192.0.2.37", 14,
            "Joel Mensah\nBackend engineer, five years of C# and SQL Server, moving to Postgres.\nI look after a billing service.",
            Answers("three_to_five", "Four weeks"))),
        new(SupportEngineer,
            [
                new("screening"), new("technical"), new("interview"),
                new("offer", "Offer sent after a strong panel."),
                new("hired", "Accepted the offer."),
            ],
            Person(
                "nadia-haddad", "Nadia Haddad", "nadia.haddad@example.com", "+44 121 496 0388", "192.0.2.38", 16,
                "Nadia Haddad\nSupport engineer, four years at a card payments provider.\nI write the runbooks our first line uses.",
                Answers("three_to_five", "Two weeks"))),
        new(SupportEngineer,
            [new("screening"), new("technical"), new("interview"), new("rejected", "We chose a candidate with more payments support experience.")],
            Person(
                "owen-price", "Owen Price", "owen.price@example.org", "+44 141 496 0399", "192.0.2.39", 13,
                "Owen Price\nIT support analyst, two years, moving into software support.",
                Answers("less_than_3", "One week"))),
        new(SupportEngineer,
            [new("screening"), new("technical"), new("withdrawn", "Withdrew: accepted another offer.")],
            Person(
                "lena-fischer", "Lena Fischer", "lena.fischer@example.com", "+44 20 7946 0400", "192.0.2.40", 10,
                "Lena Fischer\nTechnical support lead, eight years in SaaS for hospitality.",
                Answers("six_to_nine", "Three months"))),
    ];

    // The "Software engineer" questionnaire's answers; its resume question is answered by the upload.
    private static IReadOnlyList<ApplicationAnswer> Answers(string experience, string noticePeriod) =>
    [
        new("work_authorization", "yes"),
        new("experience", experience),
        new("notice_period", noticePeriod),
    ];

    private static SyntheticApplicant Person(
        string key, string name, string email, string phone, string ip, int minutes, string resume,
        IReadOnlyList<ApplicationAnswer>? answers = null) => new(
        key,
        name,
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
        $"{name} CV.pdf",
        resume,
        Answers: answers ?? [],
        ResumeProducer: "Microsoft Word for Microsoft 365");
}

/// <summary>An applicant to one of Brightwater's jobs and the moves a recruiter made, in order, each with an optional note.</summary>
internal sealed record DemoCandidate(string JobSlug, IReadOnlyList<DemoMove> Moves, SyntheticApplicant Applicant)
{
    /// <summary>The stage the candidate ends in: the last move's, or "applied" when nobody moved them.</summary>
    public string StageId => Moves.Count == 0 ? "applied" : Moves[^1].StageId;
}

/// <summary>One move to a stage of the job's workflow, as a recruiter makes it on the dashboard.</summary>
internal sealed record DemoMove(string StageId, string? Note = null);
