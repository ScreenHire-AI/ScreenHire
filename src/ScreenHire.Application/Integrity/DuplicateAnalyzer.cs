using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Within-organization duplicates (<c>ID_DUPLICATE_IN_ORG</c>): this application shares an email address (ignoring a plus
/// tag), a phone number, a device or a resume file with applications from <em>other</em> candidates of the same
/// organization. One candidate applying to two jobs is normal and never counts. Never across organizations: that is the
/// Signal Network's job, and it is off (ADR-0005).
/// </summary>
/// <remarks>
/// A shared device or file is strong (High); a shared email base or phone alone is Medium, since families and shared
/// inboxes exist. Earlier applications are not revisited when a later one matches them: the later one carries the signal.
/// </remarks>
public sealed class DuplicateAnalyzer(IApplicantDirectory directory) : IIntegrityAnalyzer
{
    public string Name => "duplicates";

    public int Version => 1;

    public async Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var matches = await directory.FindSharedAsync(context, cancellationToken);
        if (matches.Count == 0)
        {
            return AnalyzerResult.Nothing();
        }

        var strong = matches.Any(m => m.Kind is SharedIdentifier.Device or SharedIdentifier.ResumeFile);
        var others = matches.SelectMany(m => m.ApplicationIds).Distinct().Count();
        var evidence = matches
            .OrderBy(m => m.Kind)
            .Select(m => SignalEvidence.Fact(Label(m.Kind), Count(m.ApplicationIds.Count)))
            .ToList();
        return new AnalyzerResult(
        [
            new ObservedSignal(
                SignalCatalogue.DuplicateInOrganization,
                strong ? SignalConfidence.High : SignalConfidence.Medium,
                evidence,
                new Dictionary<string, string>
                {
                    ["shared"] = Join(matches.OrderBy(m => m.Kind).Select(m => Shared(m.Kind)).ToList()),
                    ["others"] = Count(others),
                },
                Weight: strong ? SignalWeight.High : SignalWeight.Medium),
        ]);
    }

    private static string Label(SharedIdentifier kind) => kind switch
    {
        SharedIdentifier.Email => "Same email address (ignoring a plus tag)",
        SharedIdentifier.Phone => "Same phone number",
        SharedIdentifier.Device => "Same device",
        _ => "Same resume file",
    };

    private static string Shared(SharedIdentifier kind) => kind switch
    {
        SharedIdentifier.Email => "an email address",
        SharedIdentifier.Phone => "a phone number",
        SharedIdentifier.Device => "a device",
        _ => "an identical resume file",
    };

    private static string Count(int applications) =>
        applications == 1 ? "1 other application" : $"{applications.ToString(CultureInfo.InvariantCulture)} other applications";

    private static string Join(List<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
}

/// <summary>A kind of hard identifier two applications can share.</summary>
public enum SharedIdentifier
{
    Email,
    Phone,
    Device,
    ResumeFile,
}

/// <summary>Other candidates' applications in the same organization that share <see cref="Kind"/> with this one.</summary>
public sealed record SharedIdentifierMatch(SharedIdentifier Kind, IReadOnlyList<Guid> ApplicationIds);

/// <summary>Looks up an organization's other applicants by hard identifier. The database version scopes by tenant.</summary>
public interface IApplicantDirectory
{
    Task<IReadOnlyList<SharedIdentifierMatch>> FindSharedAsync(IntegrityContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The directory over the database, as the application's organization (the tenant filter and row-level security scope
/// every query). Phone numbers are free text, so recent candidates' numbers are compared in E.164 in memory.
/// </summary>
public sealed class DatabaseApplicantDirectory(ScreenHireDbContext db) : IApplicantDirectory
{
    /// <summary>How far back phone numbers are compared.</summary>
    public static readonly TimeSpan PhoneWindow = TimeSpan.FromDays(180);

    private const int MaxPhoneCandidates = 5000;

    public async Task<IReadOnlyList<SharedIdentifierMatch>> FindSharedAsync(IntegrityContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var candidateId = context.Candidate.Id;
        var applicationId = context.Application.Id;
        var matches = new List<SharedIdentifierMatch>();

        async Task AddAsync(SharedIdentifier kind, IQueryable<Guid> otherCandidates)
        {
            var ids = await db.Applications.AsNoTracking()
                .Where(a => a.Id != applicationId && a.CandidateId != candidateId && otherCandidates.Contains(a.CandidateId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count > 0)
            {
                matches.Add(new SharedIdentifierMatch(kind, ids));
            }
        }

        if (Identifiers.NormalizeEmail(context.Candidate.Email) is { } email)
        {
            var at = email.LastIndexOf('@');
            var plusPrefix = email[..at] + "+";
            var domainSuffix = email[at..];
            await AddAsync(SharedIdentifier.Email, db.Candidates.AsNoTracking()
                .Where(c => c.Id != candidateId
                    && (c.NormalizedEmail == email || (c.NormalizedEmail.StartsWith(plusPrefix) && c.NormalizedEmail.EndsWith(domainSuffix))))
                .Select(c => c.Id));
        }

        if (PhoneNumbersCheck.Inspect(context.Candidate.Phone, context.JobCountry).E164 is { } phone)
        {
            var since = context.Now - PhoneWindow;
            var recent = await db.Candidates.AsNoTracking()
                .Where(c => c.Id != candidateId && c.Phone != null && c.CreatedAt >= since)
                .OrderByDescending(c => c.CreatedAt)
                .Take(MaxPhoneCandidates)
                .Select(c => new { c.Id, c.Phone })
                .ToListAsync(cancellationToken);
            var same = recent.Where(c => PhoneNumbersCheck.Inspect(c.Phone, context.JobCountry).E164 == phone).Select(c => c.Id).ToList();
            if (same.Count > 0)
            {
                await AddAsync(SharedIdentifier.Phone, db.Candidates.AsNoTracking().Where(c => same.Contains(c.Id)).Select(c => c.Id));
            }
        }

        if (context.Observation?.DeviceHash is { } device)
        {
            var ids = await db.SubmissionObservations.AsNoTracking()
                .Where(o => o.DeviceHash == device && o.ApplicationId != applicationId)
                .Join(db.Applications, o => o.ApplicationId, a => a.Id, (o, a) => a)
                .Where(a => a.CandidateId != candidateId)
                .Select(a => a.Id)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (ids.Count > 0)
            {
                matches.Add(new SharedIdentifierMatch(SharedIdentifier.Device, ids));
            }
        }

        var hashes = context.Files.Select(f => f.Sha256).Distinct().ToList();
        if (hashes.Count > 0)
        {
            var ids = await db.ApplicationFiles.AsNoTracking()
                .Where(f => hashes.Contains(f.Sha256) && f.ApplicationId != applicationId)
                .Join(db.Applications, f => f.ApplicationId, a => a.Id, (f, a) => a)
                .Where(a => a.CandidateId != candidateId)
                .Select(a => a.Id)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (ids.Count > 0)
            {
                matches.Add(new SharedIdentifierMatch(SharedIdentifier.ResumeFile, ids));
            }
        }

        return matches;
    }
}
