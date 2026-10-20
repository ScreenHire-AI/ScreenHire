using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Api.Demo;

/// <summary>
/// People who applied to Brightwater's other open jobs in the local demo, and the stage a recruiter has moved each to,
/// so the boards look lived in. All fictional (.claude/rules/no-personal-info.md): <c>example.*</c> addresses, Ofcom's
/// drama landline ranges and the RFC 5737 home broadband range Development's network settings describe. Each applies
/// from a device of their own and takes minutes, so the integrity checks find nothing to report.
/// </summary>
internal static class DemoCandidates
{
    public const string ProductDesigner = "senior-product-designer";
    public const string FrontendEngineer = "frontend-engineer-svelte";

    public static IReadOnlyList<DemoCandidate> All { get; } =
    [
        new(ProductDesigner, "interview", Person(
            "priya-natarajan", "Priya Natarajan", "priya.natarajan@example.com", "+44 20 7946 0311", "192.0.2.31", 17,
            "Priya Natarajan\nProduct designer, nine years. Research-led work on booking and payments products.\nI run design critiques and pair with engineers on prototypes.")),
        new(ProductDesigner, "screening", Person(
            "tom-whitfield", "Tom Whitfield", "tom.whitfield@example.org", "+44 113 496 0322", "192.0.2.32", 12,
            "Tom Whitfield\nSenior UX designer, seven years in SaaS dashboards and design systems.")),
        new(ProductDesigner, "applied", Person(
            "aisha-bello", "Aisha Bello", "aisha.bello@example.com", "+44 161 496 0333", "192.0.2.33", 21,
            "Aisha Bello\nInteraction designer moving from agency work to product. Accessibility champion.")),
        new(FrontendEngineer, "offer", Person(
            "marcus-lindqvist", "Marcus Lindqvist", "marcus.lindqvist@example.org", "+44 20 7946 0344", "192.0.2.34", 15,
            "Marcus Lindqvist\nFrontend engineer, six years. Svelte, TypeScript and design systems.\nI maintain a component library used by four product teams.")),
        new(FrontendEngineer, "interview", Person(
            "hannah-okoro", "Hannah Okoro", "hannah.okoro@example.com", "+44 117 496 0355", "192.0.2.35", 11,
            "Hannah Okoro\nFull-stack developer leaning frontend: SvelteKit, Node and Postgres.")),
        new(FrontendEngineer, "screening", Person(
            "eli-thompson", "Eli Thompson", "eli.thompson@example.org", "+44 131 496 0366", "192.0.2.36", 9,
            "Eli Thompson\nJunior frontend engineer, two years. Built the web shop for a small bakery chain.")),
    ];

    private static SyntheticApplicant Person(
        string key, string name, string email, string phone, string ip, int minutes, string resume) => new(
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
        Answers: [],
        ResumeProducer: "Microsoft Word for Microsoft 365");
}

/// <summary>An applicant to one of Brightwater's jobs, and the stage they are in now.</summary>
internal sealed record DemoCandidate(string JobSlug, string StageId, SyntheticApplicant Applicant);
