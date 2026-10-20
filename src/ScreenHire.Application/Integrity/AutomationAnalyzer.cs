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
/// Submit velocity is timing, which an accommodation switches off (assistive technology and autofill change how long a
/// form takes), so it is not measured for an application with one.
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

    public int Version => 1;

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

        if (!application.AccommodationRequested
            && context.Observation?.FormStartedAt is { } started
            && application.SubmittedAt - started is { } taken
            && taken >= TimeSpan.Zero && taken < TimeSpan.FromDays(1))
        {
            var minimum = PlausibleMinimum(application.Answers);
            if (taken.TotalSeconds < minimum)
            {
                var seconds = (int)Math.Floor(taken.TotalSeconds);
                signals.Add(new ObservedSignal(
                    SignalCatalogue.SubmitVelocity,
                    SignalConfidence.Medium,
                    [SignalEvidence.Metric("Time from opening the form to sending it", seconds, "seconds", minimum, "Plausible minimum for this form")],
                    new Dictionary<string, string> { ["duration"] = Seconds(seconds), ["minimum"] = $"at least {Seconds(minimum)}" },
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
    /// <see cref="BaseSeconds"/>, plus <see cref="SecondsPerAnswer"/> per answer, plus a second per
    /// <see cref="CharactersPerSecond"/> characters of text.
    /// </summary>
    public static int PlausibleMinimum(IReadOnlyList<ApplicationAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var characters = answers.Sum(a => a.Value?.Length ?? 0);
        return BaseSeconds + (SecondsPerAnswer * answers.Count) + (characters / CharactersPerSecond);
    }

    private static string Seconds(int seconds) =>
        seconds == 1 ? "1 second" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
}
