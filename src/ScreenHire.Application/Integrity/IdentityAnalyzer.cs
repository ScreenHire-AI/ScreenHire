using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Identity and contact signals from what the candidate gave (integrity-engine.md §4): a disposable email domain, an
/// email domain that publishes no mail server, and a phone number that is not valid or is in an internet (VoIP) range.
/// A check that cannot be made (no DNS, a number without a country code) is not a signal.
/// </summary>
public sealed class IdentityAnalyzer(DisposableEmailDomains disposable, IMailDomainChecker mail) : IIntegrityAnalyzer
{
    public string Name => "identity";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var signals = new List<ObservedSignal>();
        var notes = new List<string>();

        if (Identifiers.EmailDomain(context.Candidate.Email) is { } domain)
        {
            if (disposable.Match(domain) is { } listed)
            {
                signals.Add(new ObservedSignal(
                    SignalCatalogue.DisposableEmail,
                    SignalConfidence.High,
                    [SignalEvidence.Fact("Email domain", domain), SignalEvidence.Fact("Listed as", $"Disposable email service ({listed})"),
                        SignalEvidence.Fact("Source", "Public disposable-domain list (CC0)")],
                    new Dictionary<string, string> { ["domain"] = domain }));
            }
            else
            {
                switch (await mail.CheckAsync(domain, cancellationToken))
                {
                    case MailDomainStatus.NoMailServer:
                        signals.Add(new ObservedSignal(
                            SignalCatalogue.EmailWithoutMx,
                            SignalConfidence.High,
                            [SignalEvidence.Fact("Email domain", domain), SignalEvidence.Fact("Mail server", "None published")],
                            new Dictionary<string, string> { ["domain"] = domain }));
                        break;
                    case MailDomainStatus.Unknown:
                        notes.Add("The email domain's mail server was not checked.");
                        break;
                }
            }
        }

        var phone = PhoneNumbersCheck.Inspect(context.Candidate.Phone, context.JobCountry);
        if (phone is { Parsed: true, Valid: false })
        {
            var region = phone.Region is { } code ? NetworkAnalyzer.InSentence(code) : "its country";
            signals.Add(new ObservedSignal(
                SignalCatalogue.InvalidPhone,
                SignalConfidence.Medium,
                [SignalEvidence.Fact("Phone number check", $"Not a valid number for {region}")],
                new Dictionary<string, string> { ["region"] = region }));
        }
        else if (phone is { Valid: true, LineType: PhoneLineType.Voip, Region: { } voipRegion })
        {
            var region = NetworkAnalyzer.InSentence(voipRegion);
            signals.Add(new ObservedSignal(
                SignalCatalogue.VoipPhone,
                SignalConfidence.Medium,
                [SignalEvidence.Fact("Number type", "Internet (VoIP) number"), SignalEvidence.Fact("Phone number country", NetworkAnalyzer.CountryName(voipRegion)),
                    SignalEvidence.Fact("Source", "libphonenumber number ranges")],
                new Dictionary<string, string> { ["region"] = region }));
        }
        else if (!string.IsNullOrWhiteSpace(context.Candidate.Phone) && !phone.Parsed)
        {
            notes.Add("The phone number has no country code, so it was not checked.");
        }

        return new AnalyzerResult(signals, notes.Count == 0 ? null : string.Join(" ", notes));
    }
}
