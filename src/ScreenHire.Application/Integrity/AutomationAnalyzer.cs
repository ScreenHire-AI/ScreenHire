using System.Globalization;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Integrity;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Automation signals from the careers form (integrity-engine.md §4): the honeypot, automation markers the page
/// reported (<c>navigator.webdriver</c> and the like), a form completed faster than a person could, and the bot
/// challenge's result (context only). A passed bot challenge is counter-evidence for the others.
/// </summary>
/// <remarks>
/// <para>
/// Submit velocity is timing, which an accommodation switches off (assistive technology and autofill change how long a
/// form takes), so it is not measured for an application with one.
/// </para>
/// <para>
/// It measures active time, not time on the clock (apply-flow.md §7): the seconds each page of the form was open, from
/// the careers server showing it to the candidate posting it, added up. A break between saving a draft and coming back
/// is never counted. Answers the application carried over from its draft were given in an earlier sitting, whose time a
/// draft does not keep, so they are left out of the minimum, and so is the reading time that sitting already spent.
/// Version 2 replaced the time from first showing the form to sending it.
/// </para>
/// </remarks>
public sealed class AutomationAnalyzer : IIntegrityAnalyzer
{
    /// <summary>The fastest anyone plausibly reads and completes even an empty form.</summary>
    public const int BaseSeconds = 15;

    /// <summary>Added per answered question.</summary>
    public const int SecondsPerAnswer = 4;

    /// <summary>Text answers add a second per this many characters, which is fast even for pasting prepared text.</summary>
    public const int CharactersPerSecond = 50;

    public string Name => "automation";

    public int Version => 2;

    public Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var application = context.Application;
        var signals = new List<ObservedSignal>();
        IReadOnlyList<CounterEvidence> passed = application.BotCheck == BotCheckResult.Passed
            ? [new CounterEvidence("The browser passed the bot challenge.", SignalEvidence.Fact("Bot challenge", "Passed"))]
            : [];

        if (application.HoneypotFilled)
        {
            signals.Add(new ObservedSignal(
                SignalCatalogue.Honeypot, SignalConfidence.High, [SignalEvidence.Fact("Hidden field", "Filled in")], CounterEvidence: passed));
        }

        if (context.Observation?.AutomationMarkers is { Count: > 0 } markers)
        {
            signals.Add(new ObservedSignal(
                SignalCatalogue.Webdriver,
                SignalConfidence.High,
                [SignalEvidence.List("Markers the browser reported", markers)],
                new Dictionary<string, string> { ["markers"] = string.Join(", ", markers) },
                passed));
        }

        if (!application.AccommodationRequested && context.Observation is { PageTimes.Count: > 0 } observation)
        {
            var active = observation.PageTimes.Sum(p => p.Seconds);
            var carried = observation.CarriedOverQuestionIds.ToHashSet(StringComparer.Ordinal);
            var timed = application.Answers.Where(a => !carried.Contains(a.QuestionId)).ToList();
            var minimum = PlausibleMinimum(timed, readsTheForm: !observation.ResumedFromDraft);
            if (active < minimum)
            {
                signals.Add(new ObservedSignal(
                    SignalCatalogue.SubmitVelocity,
                    SignalConfidence.Medium,
                    [SignalEvidence.Metric("Active time on the form's pages", active, "seconds", minimum, "Plausible minimum for this form")],
                    new Dictionary<string, string> { ["duration"] = Seconds(active), ["minimum"] = $"at least {Seconds(minimum)}" },
                    passed));
            }
        }

        if (application.BotCheck == BotCheckResult.Failed)
        {
            signals.Add(new ObservedSignal(
                SignalCatalogue.TurnstileFailed, SignalConfidence.Low, [SignalEvidence.Fact("Bot challenge", "Not passed")]));
        }

        return Task.FromResult(new AnalyzerResult(signals));
    }

    /// <summary>
    /// The least time, in whole seconds, a person plausibly needs for a form with these answers:
    /// <see cref="BaseSeconds"/> to read it (unless <paramref name="readsTheForm"/> is false: a resumed application read
    /// it in an earlier sitting), plus <see cref="SecondsPerAnswer"/> per answer, plus a second per
    /// <see cref="CharactersPerSecond"/> characters of text.
    /// </summary>
    public static int PlausibleMinimum(IReadOnlyList<ApplicationAnswer> answers, bool readsTheForm = true)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var characters = answers.Sum(a => a.Value?.Length ?? 0);
        return (readsTheForm ? BaseSeconds : 0) + (SecondsPerAnswer * answers.Count) + (characters / CharactersPerSecond);
    }

    private static string Seconds(int seconds) =>
        seconds == 1 ? "1 second" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
}
