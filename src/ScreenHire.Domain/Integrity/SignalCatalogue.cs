using System.Collections.Frozen;
using System.Text;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// Every signal code the engine knows (integrity-engine.md §4): its category, the assessment it feeds, its weight, and
/// how it reads to a recruiter. Analyzers observe signals by code; the catalogue fills in the rest, so a signal means
/// the same thing wherever it comes from. Content and behavioural codes are listed here for the analyzers that later
/// tasks add.
/// </summary>
public static class SignalCatalogue
{
    // Network
    public const string DatacenterIp = "NET_DATACENTER_IP";
    public const string AnonymousProxy = "NET_ANON_PROXY";
    public const string GeoClaimMismatch = "NET_GEO_CLAIM_MISMATCH";

    // Identity and contact
    public const string DisposableEmail = "ID_EMAIL_DISPOSABLE";
    public const string EmailWithoutMx = "ID_EMAIL_NO_MX";
    public const string InvalidPhone = "ID_PHONE_INVALID";
    public const string VoipPhone = "ID_PHONE_VOIP";
    public const string DuplicateInOrganization = "ID_DUPLICATE_IN_ORG";

    // Automation and session
    public const string Webdriver = "AUTO_WEBDRIVER";
    public const string SubmitVelocity = "AUTO_SUBMIT_VELOCITY";
    public const string Honeypot = "AUTO_HONEYPOT";
    public const string TurnstileFailed = "AUTO_TURNSTILE_FAIL";

    // Behavioural telemetry (aggregates only), supplied by the telemetry module
    public const string PasteDominant = "BEH_PASTE_DOMINANT";
    public const string FocusSwitching = "BEH_FOCUS_SWITCHING";

    // Content forensics, supplied by the content analyzers
    public const string PromptInjection = "CNT_PROMPT_INJECTION";
    public const string NearDuplicateAnswer = "CNT_NEAR_DUPLICATE_ANSWER";
    public const string ResumeGenerator = "CNT_RESUME_GENERATOR";
    public const string Inconsistency = "CNT_INCONSISTENCY";
    public const string TemplatedAnswer = "CNT_TEMPLATED_ANSWER";

    private static readonly SignalDefinition[] Definitions =
    [
        new(DatacenterIp, SignalCategory.Network, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Sent from a hosting provider's network",
            "The application came from {network}, a hosting or cloud provider. People usually apply from a home, mobile or office connection."),
        new(AnonymousProxy, SignalCategory.Network, IntegrityAssessmentKind.Authenticity, SignalWeight.Low,
            "Sent through a VPN, proxy or Tor",
            "The application came through {service}. Many people use these for privacy, so this is context only and never a reason on its own.",
            ContextOnly: true),
        new(GeoClaimMismatch, SignalCategory.Network, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Network location differs from the location the candidate gave",
            "The candidate's details point to {claimed}, but the application came from a network in {observed}."),

        new(DisposableEmail, SignalCategory.Identity, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Throwaway email address",
            "The email address uses {domain}, a disposable email service whose inboxes expire."),
        new(EmailWithoutMx, SignalCategory.Identity, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Email domain cannot receive mail",
            "The domain {domain} publishes no mail server, so messages to this address cannot be delivered."),
        new(InvalidPhone, SignalCategory.Identity, IntegrityAssessmentKind.Authenticity, SignalWeight.Low,
            "Phone number is not a valid number",
            "The phone number does not match any number range in use for {region}."),
        new(VoipPhone, SignalCategory.Identity, IntegrityAssessmentKind.Authenticity, SignalWeight.Low,
            "Internet phone number",
            "The phone number is in a range for internet (VoIP) numbers in {region}. Many people use them legitimately; they are also easy to create in bulk."),
        new(DuplicateInOrganization, SignalCategory.Identity, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Shares details with other applicants",
            "This application and {others} to your organization came from different candidates but share {shared}."),

        new(Webdriver, SignalCategory.Automation, IntegrityAssessmentKind.Authenticity, SignalWeight.High,
            "Browser automation detected",
            "The browser reported that automation software was controlling it ({markers})."),
        new(SubmitVelocity, SignalCategory.Automation, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Completed faster than a person could",
            "The form was completed in {duration}, faster than reading and answering it plausibly takes ({minimum}).",
            AccessibilitySensitive: true),
        new(Honeypot, SignalCategory.Automation, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Hidden field filled in",
            "A form field hidden from people was filled in. Automated software usually does this; some autofill tools can too.",
            AccessibilitySensitive: true),
        new(TurnstileFailed, SignalCategory.Automation, IntegrityAssessmentKind.Authenticity, SignalWeight.Low,
            "Bot challenge not passed",
            "The browser did not pass the bot challenge. Browsers with JavaScript turned off or strict privacy tools fail it too, so this is context only.",
            ContextOnly: true),

        new(PasteDominant, SignalCategory.Behavioural, IntegrityAssessmentKind.AiAssistance, SignalWeight.Medium,
            "Answers mostly pasted in",
            "Most of the text in {field} arrived by pasting rather than typing.",
            AccessibilitySensitive: true),
        new(FocusSwitching, SignalCategory.Behavioural, IntegrityAssessmentKind.AiAssistance, SignalWeight.Low,
            "Frequent switching away while answering",
            "The candidate switched away from the form often while answering {field}.",
            AccessibilitySensitive: true),

        new(PromptInjection, SignalCategory.Document, IntegrityAssessmentKind.AiAssistance, SignalWeight.High,
            "Hidden instructions in the resume",
            "The resume contains text aimed at automated screening tools rather than people: {detail}."),
        new(NearDuplicateAnswer, SignalCategory.Content, IntegrityAssessmentKind.AiAssistance, SignalWeight.Medium,
            "Answer nearly identical to another applicant's",
            "The answer to {question} is nearly identical to an answer another applicant gave."),
        new(ResumeGenerator, SignalCategory.Document, IntegrityAssessmentKind.AiAssistance, SignalWeight.Low,
            "Resume made with a generator tool",
            "The resume file's details show it was made with {tool}."),
        new(Inconsistency, SignalCategory.Content, IntegrityAssessmentKind.Authenticity, SignalWeight.Medium,
            "Details contradict each other",
            "The {left} and the {right} give different {topic}. There is often an ordinary explanation, such as a role left off one of them, so ask the candidate before drawing a conclusion."),
        new(TemplatedAnswer, SignalCategory.Content, IntegrityAssessmentKind.AiAssistance, SignalWeight.Low,
            "Answer contains template text",
            "The answer to {question} contains {pattern}, text usually left over from a writing tool or template. This is for information only and never counts toward a level.",
            ContextOnly: true,
            Informational: true),
    ];

    /// <summary>Every definition by code.</summary>
    public static FrozenDictionary<string, SignalDefinition> All { get; } = Definitions.ToFrozenDictionary(d => d.Code, StringComparer.Ordinal);

    /// <summary>The definition of <paramref name="code"/>; an unknown code is a programming error.</summary>
    public static SignalDefinition Get(string code) =>
        All.TryGetValue(code, out var definition) ? definition : throw new ArgumentException($"Unknown signal code '{code}'.", nameof(code));
}

/// <summary>
/// What a signal code means. <see cref="ExplanationTemplate"/> has <c>{name}</c> placeholders that an observation fills
/// in (<see cref="Explain"/>).
/// </summary>
/// <remarks>
/// <c>informational</c>: never counted by the ruleset, not even as context next to other signals of its category.
/// </remarks>
public sealed record SignalDefinition(
    string Code,
    SignalCategory Category,
    IntegrityAssessmentKind Assessment,
    SignalWeight Weight,
    string Title,
    string ExplanationTemplate,
    bool AccessibilitySensitive = false,
    bool ContextOnly = false,
    bool Informational = false)
{
    /// <summary>The explanation with <paramref name="values"/> in its placeholders. A placeholder with no value stays visible.</summary>
    public string Explain(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return ExplanationTemplate;
        }

        var text = new StringBuilder(ExplanationTemplate);
        foreach (var (name, value) in values)
        {
            text.Replace("{" + name + "}", value);
        }

        return text.ToString();
    }
}
