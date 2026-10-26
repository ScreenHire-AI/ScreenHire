using System.Text.Json.Serialization;
using FluentValidation;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Api.Contracts;

/// <summary>
/// What the careers page's telemetry module may do on this form (integrity-engine.md §3): <c>sessionToken</c> binds its
/// snapshots to one way through the form, and <c>deviceCheck</c> is the footing for the device fingerprint where the job
/// is (<c>Off</c>, <c>Notice</c>, or <c>Consent</c>, which needs the candidate's tick).
/// </summary>
public sealed record PublicTelemetrySettings(string SessionToken, DeviceCheckBasis DeviceCheck);

/// <summary>
/// One cumulative snapshot of one page view's form telemetry: session aggregates only (CLAUDE.md non-negotiable 4), never
/// keystrokes, key values, timings per key or field content. Unknown members are refused, so nothing else can travel in
/// it.
/// </summary>
/// <remarks>
/// <c>sessionToken</c> is the token from the application form. <c>pageViewId</c> names one render of one step, chosen by
/// the page; <c>sequence</c> counts its snapshots, and only a higher one replaces the stored snapshot.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TelemetrySnapshotInput(
    string SessionToken, Guid PageViewId, int Sequence, IReadOnlyList<FieldTelemetryInput> Fields, PageTelemetryInput Page);

/// <summary>Counts for one form field. <c>fieldId</c> is a question id or one of the form's own fields (<c>firstName</c>, ...).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldTelemetryInput(
    string FieldId,
    int PasteCount,
    int PastedCharacters,
    int TypedCharacters,
    int FocusCount,
    int BlurCount,
    int ActiveMilliseconds,
    bool Autofilled);

/// <summary>
/// Page-level counts: times the page was hidden, and automation markers from a fixed list (<c>webdriver</c>,
/// <c>headless</c>, ...).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PageTelemetryInput(int VisibilityChanges, IReadOnlyList<string> AutomationMarkers);

internal static class TelemetryMappings
{
    public static FieldTelemetry ToDomain(this FieldTelemetryInput field) => new(
        field.FieldId, field.PasteCount, field.PastedCharacters, field.TypedCharacters, field.FocusCount, field.BlurCount,
        field.ActiveMilliseconds, field.Autofilled);

    public static PageTelemetry ToDomain(this PageTelemetryInput page) =>
        new(page.VisibilityChanges, [.. page.AutomationMarkers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
}

internal sealed class TelemetrySnapshotInputValidator : AbstractValidator<TelemetrySnapshotInput>
{
    /// <summary>The automation markers the module reports. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> KnownMarkers =
        new HashSet<string>(["webdriver", "headless", "phantom", "selenium", "nightmare", "chromedriver"], StringComparer.Ordinal);

    // Generous ceilings: a candidate who spends a day on one field is still under them.
    private const int MaxCount = 10_000;
    private const int MaxCharacters = 1_000_000;
    private const int MaxMilliseconds = 7 * 24 * 60 * 60 * 1000;

    public TelemetrySnapshotInputValidator()
    {
        RuleFor(s => s.SessionToken).NotEmpty().MaximumLength(1024);
        RuleFor(s => s.PageViewId).NotEmpty();
        RuleFor(s => s.Sequence).InclusiveBetween(1, MaxCount);
        RuleFor(s => s.Fields).NotNull()
            .Must(f => f is null || f.Count <= TelemetrySnapshot.MaxFields).WithMessage($"At most {TelemetrySnapshot.MaxFields} fields.")
            .Must(f => f is null || f.Select(x => x?.FieldId).Distinct(StringComparer.Ordinal).Count() == f.Count).WithMessage("Each field once.");
        RuleForEach(s => s.Fields).NotNull().ChildRules(field =>
        {
            field.RuleFor(f => f.FieldId).NotEmpty().MaximumLength(TelemetrySnapshot.MaxFieldIdLength)
                .Matches("^[A-Za-z0-9_-]+$").WithMessage("A field id is letters, digits, '_' and '-'.");
            field.RuleFor(f => f.PasteCount).InclusiveBetween(0, MaxCount);
            field.RuleFor(f => f.PastedCharacters).InclusiveBetween(0, MaxCharacters);
            field.RuleFor(f => f.TypedCharacters).InclusiveBetween(0, MaxCharacters);
            field.RuleFor(f => f.FocusCount).InclusiveBetween(0, MaxCount);
            field.RuleFor(f => f.BlurCount).InclusiveBetween(0, MaxCount);
            field.RuleFor(f => f.ActiveMilliseconds).InclusiveBetween(0, MaxMilliseconds);
        });
        RuleFor(s => s.Page).NotNull();
        RuleFor(s => s.Page.VisibilityChanges).InclusiveBetween(0, MaxCount).When(s => s.Page is not null);
        RuleFor(s => s.Page.AutomationMarkers).NotNull().Must(m => m is null || m.Count <= KnownMarkers.Count)
            .When(s => s.Page is not null);
        RuleForEach(s => s.Page.AutomationMarkers).Must(KnownMarkers.Contains).WithMessage("Not a known automation marker.")
            .When(s => s.Page is not null);
    }
}
