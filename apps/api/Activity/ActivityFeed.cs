using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Api.Hiring;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Activity;

/// <summary>
/// The workspace's Activity feed (docs/architecture/notifications.md, "Activity"): every application's timeline and every
/// job's change history, newest first, as one list. Only what the reader may see reaches it: the timelines through
/// row-level security's job scope and the <c>JobScope</c> filter, and the job histories, whose table is under the tenant's
/// policy only, through a join to the jobs the reader can see.
/// </summary>
internal static class ActivityFeed
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>The most the "since you were last here" count says; the dashboard shows "99+" past it.</summary>
    public const int UnseenCap = 100;

    /// <summary>Which kind each timeline entry the feed shows is; a type not here never reaches the feed.</summary>
    public static IReadOnlyDictionary<ApplicationEventType, ActivityKind> Kinds { get; } = new Dictionary<ApplicationEventType, ActivityKind>
    {
        [ApplicationEventType.StageChanged] = ActivityKind.Moves,
        [ApplicationEventType.Submitted] = ActivityKind.Applications,
        [ApplicationEventType.InvitedToApply] = ActivityKind.Applications,
        [ApplicationEventType.KeptInMind] = ActivityKind.Applications,
        // That the checks finished, never what they found (non-negotiable 1): the entry carries no level.
        [ApplicationEventType.VerificationAssessed] = ActivityKind.Applications,
        [ApplicationEventType.EmailSent] = ActivityKind.Messages,
        [ApplicationEventType.EmailReceived] = ActivityKind.Messages,
        [ApplicationEventType.QuestionnaireSent] = ActivityKind.Messages,
        [ApplicationEventType.QuestionnaireCompleted] = ActivityKind.Messages,
        [ApplicationEventType.InterviewInvited] = ActivityKind.Interviews,
        [ApplicationEventType.InterviewBooked] = ActivityKind.Interviews,
        [ApplicationEventType.InterviewRescheduled] = ActivityKind.Interviews,
        [ApplicationEventType.InterviewCancelled] = ActivityKind.Interviews,
        [ApplicationEventType.InterviewNewTimesOffered] = ActivityKind.Interviews,
        [ApplicationEventType.ScorecardSubmitted] = ActivityKind.Scorecards,
        [ApplicationEventType.NoteAdded] = ActivityKind.Notes,
        [ApplicationEventType.TagAdded] = ActivityKind.Notes,
        [ApplicationEventType.TagRemoved] = ActivityKind.Notes,
        // Completing or reopening a task writes no timeline entry, so a task is in the feed once, when it is made.
        [ApplicationEventType.TaskCreated] = ActivityKind.Tasks,
    };

    /// <summary>
    /// The timeline entries the feed leaves out: a note's edit or deletion (the note's own line stays) and leaving saved
    /// candidates, which is housekeeping, not news.
    /// </summary>
    public static IReadOnlySet<ApplicationEventType> Left { get; } = new HashSet<ApplicationEventType>
    {
        ApplicationEventType.NoteEdited,
        ApplicationEventType.NoteDeleted,
        ApplicationEventType.LeftTalentPool,
    };

    // The entries whose missing actor is the candidate: the careers page, their reply, their answers, their booking.
    private static readonly HashSet<ApplicationEventType> CandidateTypes =
    [
        ApplicationEventType.Submitted,
        ApplicationEventType.EmailReceived,
        ApplicationEventType.QuestionnaireCompleted,
        ApplicationEventType.InterviewBooked,
        ApplicationEventType.InterviewRescheduled,
    ];

    /// <summary>Who made a timeline entry, as the feed names them.</summary>
    public static ActivityActor ActorOf(ApplicationEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Data.AutomationRunId is not null ? ActivityActor.Automation
            : entry.ActorUserId is not null ? ActivityActor.Member
            : entry.Data.ApiKeyId is not null ? ActivityActor.ApiKey
            : CandidateTypes.Contains(entry.Type) ? ActivityActor.Candidate
            : ActivityActor.ScreenHire;
    }

    /// <summary>
    /// One page of the feed for the signed-in member: up to <paramref name="limit"/> entries older than
    /// <paramref name="cursor"/>, and how many came after <paramref name="seenAt"/>. The cursor and the merge of the two
    /// sources are the verification feed's (<see cref="VerificationActivity"/>) on purpose: both page two tables by time
    /// then id, so a change to that paging changes this feed's too.
    /// </summary>
    public static async Task<ActivityPage> PageAsync(
        ScreenHireDbContext db,
        ITenantContext tenant,
        ActivityScope scope,
        IReadOnlyCollection<ActivityKind> kinds,
        VerificationActivity.Cursor? cursor,
        int limit,
        DateTimeOffset? seenAt,
        CancellationToken cancellationToken)
    {
        var jobs = JobsIn(db, tenant, scope);
        var types = Kinds.Where(k => kinds.Count == 0 || kinds.Contains(k.Value)).Select(k => k.Key).ToList();
        var withJobs = kinds.Count == 0 || kinds.Contains(ActivityKind.Jobs);

        var events = Events(db, jobs, types);
        var changes = Changes(db, jobs, withJobs);
        var eventRows = await After(events, cursor)
            .OrderByDescending(x => x.Event.OccurredAt).ThenByDescending(x => x.Event.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var changeRows = await After(changes, cursor)
            .OrderByDescending(x => x.Record.OccurredAt).ThenByDescending(x => x.Record.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var (page, more) = VerificationActivity.MergePage(
            eventRows.Select(Row.Of), changeRows.Select(Row.Of), limit, r => r.At, r => r.Id);
        var entries = await ToEntriesAsync(db, tenant, page, cancellationToken);
        var next = more && page.Count > 0 ? new VerificationActivity.Cursor(page[^1].At, page[^1].Id).Encode() : null;

        int? unseen = null;
        if (seenAt is { } seen)
        {
            var me = tenant.UserId;
            var newEvents = await events
                .Where(x => x.Event.OccurredAt > seen && (x.Event.ActorUserId == null || x.Event.ActorUserId != me))
                .Take(UnseenCap).CountAsync(cancellationToken);
            var newChanges = newEvents >= UnseenCap ? 0 : await changes
                .Where(x => x.Record.OccurredAt > seen && (x.Record.ActorUserId == null || x.Record.ActorUserId != me))
                .Take(UnseenCap).CountAsync(cancellationToken);
            unseen = Math.Min(UnseenCap, newEvents + newChanges);
        }

        return new ActivityPage(entries, next, seenAt, unseen, scope);
    }

    // My jobs is the jobs whose hiring team holds the reader's membership. Every job the query reads is also behind the
    // job_scope policy and the JobScope filter, so Everything is every job the reader may see.
    private static IQueryable<Job> JobsIn(ScreenHireDbContext db, ITenantContext tenant, ActivityScope scope) =>
        scope == ActivityScope.Everything ? db.Jobs
        : tenant.MemberId is { } memberId ? db.Jobs.Where(j => j.HiringTeam.Contains(memberId))
        : db.Jobs.Where(_ => false);

    private static IQueryable<EventRow> Events(ScreenHireDbContext db, IQueryable<Job> jobs, List<ApplicationEventType> types) =>
        from e in db.ApplicationEvents.AsNoTracking()
        where types.Contains(e.Type)
        join a in db.Applications on e.ApplicationId equals a.Id
        join j in jobs on a.JobId equals j.Id
        join c in db.Candidates on a.CandidateId equals c.Id
        select new EventRow { Event = e, JobId = j.Id, JobTitle = j.Title, CandidateName = c.FullName };

    // change_records is under the tenant's policy only, so the join to the jobs the reader can see is what keeps a hidden
    // job's history out (sign-in-and-tenancy.md, "Job access"). Never read it without that join.
    private static IQueryable<ChangeRow> Changes(ScreenHireDbContext db, IQueryable<Job> jobs, bool wanted) =>
        from r in db.ChangeRecords.AsNoTracking()
        where wanted && r.SubjectKind == ChangeSubjectKind.Job
        join j in jobs on r.SubjectId equals j.Id.ToString()
        select new ChangeRow { Record = r, JobId = j.Id, JobTitle = j.Title };

    private static IQueryable<EventRow> After(IQueryable<EventRow> rows, VerificationActivity.Cursor? cursor) =>
        cursor is { } after
            ? rows.Where(x => x.Event.OccurredAt < after.At || (x.Event.OccurredAt == after.At && x.Event.Id.CompareTo(after.Id) < 0))
            : rows;

    private static IQueryable<ChangeRow> After(IQueryable<ChangeRow> rows, VerificationActivity.Cursor? cursor) =>
        cursor is { } after
            ? rows.Where(x => x.Record.OccurredAt < after.At || (x.Record.OccurredAt == after.At && x.Record.Id.CompareTo(after.Id) < 0))
            : rows;

    // Everything the entries name by id, in one query each: the jobs' stages, the follow-up forms, the API keys, the
    // members a job's change names, and which applications' scorecards the reader may not read yet.
    private static async Task<List<ActivityEntry>> ToEntriesAsync(
        ScreenHireDbContext db, ITenantContext tenant, IReadOnlyList<Row> page, CancellationToken cancellationToken)
    {
        var events = page.Select(r => r.Event).OfType<EventRow>().ToList();
        var records = page.Select(r => r.Change).OfType<ChangeRow>().ToList();

        var jobIds = events.Select(e => e.JobId).Distinct().ToList();
        var workflows = jobIds.Count == 0
            ? []
            : await db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).Select(j => new { j.Id, j.Workflow })
                .ToDictionaryAsync(j => j.Id, j => j.Workflow, cancellationToken);
        var requestIds = events.Select(e => e.Event.Data.QuestionnaireRequestId).OfType<Guid>().Distinct().ToList();
        var forms = requestIds.Count == 0
            ? []
            : await db.QuestionnaireRequests.AsNoTracking().Where(r => requestIds.Contains(r.Id)).Select(r => new { r.Id, r.QuestionnaireName })
                .ToDictionaryAsync(r => r.Id, r => r.QuestionnaireName, cancellationToken);
        var keyIds = events.Select(e => e.Event.Data.ApiKeyId).Concat(records.Select(r => r.Record.ActorApiKeyId)).OfType<Guid>().Distinct().ToList();
        var keyNames = keyIds.Count == 0
            ? []
            : await db.ApiKeys.AsNoTracking().Where(k => keyIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, k => k.Name, cancellationToken);
        var people = HistoryMappings.People(records.Select(r => r.Record));
        var names = people.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => people.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.DisplayName ?? u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Name ?? HistoryMappings.FormerTeammate, cancellationToken);
        var scored = events
            .Where(e => e.Event.Type == ApplicationEventType.ScorecardSubmitted && e.Event.ActorUserId != tenant.UserId)
            .Select(e => e.Event.ApplicationId).Distinct().ToList();
        var withheld = await RatingReads.WithheldAmongAsync(db, scored, tenant.UserId, cancellationToken);

        return [.. page.Select(row => row.Event is { } e ? EventEntry(e) : ChangeEntry(row.Change!))];

        ActivityEntry EventEntry(EventRow row)
        {
            var e = row.Event;
            var keyName = e.Data.ApiKeyId is { } keyId ? keyNames.GetValueOrDefault(keyId) : null;
            // The feed says what happened, not what was written: a note's or a move's text, and a scorecard's
            // recommendation, stay on the application. A scorecard the reader may not read yet has no id to open.
            var entry = e.ToTimelineEntry(null, keyName) with { Note = null, Recommendation = null };
            if (entry.ScorecardId is not null && withheld.Contains(e.ApplicationId))
            {
                entry = entry with { ScorecardId = null };
            }

            var workflow = workflows.GetValueOrDefault(row.JobId);
            string? NameOf(string? stageId) => stageId is null ? null : workflow?.FindStage(stageId)?.Name;
            return new ActivityEntry(
                e.Id, Kinds[e.Type], e.OccurredAt, ActorOf(e), e.ActorUserId, keyName, row.JobId, row.JobTitle, e.ApplicationId, row.CandidateName,
                entry, NameOf(entry.FromStageId), NameOf(entry.ToStageId), NameOf(entry.StageId),
                e.Data.QuestionnaireRequestId is { } requestId ? forms.GetValueOrDefault(requestId) : null, null);
        }

        ActivityEntry ChangeEntry(ChangeRow row)
        {
            var r = row.Record;
            var by = r.ActorUserId is not null ? ActivityActor.Member : r.ActorApiKeyId is not null ? ActivityActor.ApiKey : ActivityActor.ScreenHire;
            var lines = r.ToEntry(names).Lines;
            return new ActivityEntry(
                r.Id, ActivityKind.Jobs, r.OccurredAt, by, r.ActorUserId, r.ActorApiKeyId is { } keyId ? keyNames.GetValueOrDefault(keyId) : null,
                row.JobId, row.JobTitle, null, null, null, null, null, null, null,
                new JobActivity(r.Created, lines.Count > 0 ? lines[0] : null, Math.Max(0, lines.Count - 1)));
        }
    }

    private sealed class EventRow
    {
        public required ApplicationEvent Event { get; init; }

        public Guid JobId { get; init; }

        public required string JobTitle { get; init; }

        public required string CandidateName { get; init; }
    }

    private sealed class ChangeRow
    {
        public required ChangeRecord Record { get; init; }

        public Guid JobId { get; init; }

        public required string JobTitle { get; init; }
    }

    private sealed record Row(DateTimeOffset At, Guid Id, EventRow? Event, ChangeRow? Change)
    {
        public static Row Of(EventRow row) => new(row.Event.OccurredAt, row.Event.Id, row, null);

        public static Row Of(ChangeRow row) => new(row.Record.OccurredAt, row.Record.Id, null, row);
    }
}
