using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;
using ScreenHire.Domain.Views;

namespace ScreenHire.Api.Mcp;

// What the job, pipeline, scheduling, scorecard, email, task, template and team tools return
// (docs/architecture/mcp-server.md). As in McpContracts.cs: mapped from the hiring API's own contracts, only what an
// assistant needs, candidate-written text as CandidateText, and no verification output anywhere.

/// <summary>
/// A job with its hiring process: the stages in order (their ids are what <c>move_to_stage</c> and
/// <c>set_job_pipeline</c> take), the screening questions, and <c>workflowVersion</c>, which the edits of either expect.
/// <c>problems</c> lists what stops a draft from being published.
/// </summary>
public sealed record McpJobDetail(
    Guid Id,
    string Title,
    JobStatus Status,
    string Description,
    string? Department,
    EmploymentType EmploymentType,
    WorkplaceType WorkplaceType,
    IReadOnlyList<JobLocation> Locations,
    JobSalary? Salary,
    int WorkflowVersion,
    IReadOnlyList<WorkflowStage> Stages,
    Questionnaire Questions,
    int Automations,
    JobTemplateSource? Template,
    IReadOnlyList<DefinitionError> Problems,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ClosedAt)
{
    internal static McpJobDetail From(JobDetail job) => new(
        job.Id, job.Title, job.Status, job.Description, job.Department, job.EmploymentType, job.WorkplaceType, job.Locations, job.Salary,
        job.WorkflowVersion, job.Workflow.Stages, job.Workflow.Questionnaire, job.Workflow.Automations.Count, job.Template,
        job.WorkflowProblems, job.PublishedAt, job.ClosedAt);
}

/// <summary>A job's stages in order, each with the applications in it now, newest first.</summary>
public sealed record McpPipeline(Guid JobId, string JobTitle, IReadOnlyList<McpPipelineStage> Stages);

/// <summary>One stage and who is in it. <c>count</c> is everyone; <c>applications</c> at most the limit asked for.</summary>
public sealed record McpPipelineStage(string Id, string Name, StageKind Kind, int Count, IReadOnlyList<McpPipelineEntry> Applications);

/// <summary>One application in a stage: pass <c>applicationId</c> to <c>get_application</c>.</summary>
public sealed record McpPipelineEntry(Guid ApplicationId, CandidateText CandidateName, IReadOnlyList<string> Tags, DateTimeOffset StageEnteredAt);

/// <summary>An interview offered or booked: the times are in UTC, and <c>timeZone</c> is the one the candidate saw.</summary>
public sealed record McpInterview(
    Guid Id,
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    InterviewStatus Status,
    InterviewOption Option,
    DateTimeOffset? StartsAt,
    int DurationMinutes,
    string TimeZone,
    IReadOnlyList<McpInterviewer> Interviewers);

/// <summary>An interviewer, the first the organizer.</summary>
public sealed record McpInterviewer(Guid UserId, string Name, bool IsOrganizer);

/// <summary>Booked interviews from now, soonest first.</summary>
public sealed record McpInterviews(IReadOnlyList<McpUpcomingInterview> Interviews);

/// <summary>A booked interview, with the candidate's name as they wrote it.</summary>
public sealed record McpUpcomingInterview(
    Guid InterviewId, Guid ApplicationId, CandidateText CandidateName, Guid JobId, string JobTitle, DateTimeOffset StartsAt, int DurationMinutes,
    IReadOnlyList<Guid> InterviewerUserIds);

/// <summary>An application's scorecards, oldest first. Each is the team's judgement, never verification evidence.</summary>
public sealed record McpScorecards(Guid ApplicationId, IReadOnlyList<McpScorecard> Scorecards);

/// <summary>One reviewer's scorecard at a stage: the overall recommendation, the rating, each answer, and their note.</summary>
public sealed record McpScorecard(
    Guid Id,
    string StageId,
    Guid ReviewerUserId,
    Recommendation? Recommendation,
    decimal? Rating,
    IReadOnlyList<ScorecardAnswerEntry> Answers,
    string? Note,
    DateTimeOffset SubmittedAt);

/// <summary>The organization's email templates: pass an <c>id</c> to <c>draft_email</c> or <c>send_email</c>.</summary>
public sealed record McpEmailTemplates(bool SendingEnabled, IReadOnlyList<McpEmailTemplate> Templates);

/// <summary>One email template, by name, with the subject it starts from.</summary>
public sealed record McpEmailTemplate(string Id, string Name, string Subject, bool Archived);

/// <summary>An email as the candidate would receive it now. Nothing is sent: <c>send_email</c> sends it.</summary>
public sealed record McpEmailDraft(Guid ApplicationId, string? TemplateId, string Subject, string Text);

/// <summary>Whether the email is queued (<c>queued</c> is 1), or why not.</summary>
public sealed record McpEmailSent(Guid ApplicationId, bool Queued, string Detail);

/// <summary>Every email between the team and the candidate, newest first: what was sent, and what the candidate wrote back.</summary>
public sealed record McpEmailThread(Guid ApplicationId, IReadOnlyList<McpThreadEmail> Emails);

/// <summary>
/// One email. A sent one has its <c>subject</c> and <c>text</c> as it went (an email sent before copies were kept has its
/// subject alone); a received one has the candidate's subject and words in <c>replySubject</c> and <c>reply</c>, marked
/// as theirs, and its attachments by name only.
/// </summary>
public sealed record McpThreadEmail(
    Guid Id,
    Guid ApplicationId,
    EmailDirection Direction,
    DateTimeOffset At,
    string? Subject,
    Guid? SentByUserId,
    string? Text,
    CandidateText? ReplySubject,
    CandidateText? Reply,
    IReadOnlyList<CandidateText> Attachments);

/// <summary>Tasks, open ones oldest first or done ones newest first.</summary>
public sealed record McpTasks(IReadOnlyList<McpTaskItem> Tasks);

/// <summary>One task about one application. <c>assigneeUserId</c> is null for anyone on the hiring team.</summary>
public sealed record McpTaskItem(
    Guid Id,
    Guid ApplicationId,
    CandidateText CandidateName,
    string JobTitle,
    ApplicationTaskKind Kind,
    string Note,
    Guid? AssigneeUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>The hiring templates a job can start from: pass a <c>latestPublishedVersionId</c> to <c>create_job</c>.</summary>
public sealed record McpHiringTemplates(IReadOnlyList<McpHiringTemplate> Templates);

/// <summary>One hiring template: ScreenHire's own (<c>isSystem</c>) or the organization's.</summary>
public sealed record McpHiringTemplate(Guid Id, string Name, string? Description, bool IsSystem, int? LatestPublishedVersion, Guid? LatestPublishedVersionId);

/// <summary>The team's shared views, by name. <c>canApply</c> says whether <c>apply_view</c> can use one, and <c>why</c> when not.</summary>
public sealed record McpViews(IReadOnlyList<McpView> Views);

/// <summary>
/// One shared view: the jobs, stages (stage names on a candidates view, stage ids on a job's) and tag it filters by.
/// <c>baseView</c> is a built-in view it narrows, which only the dashboard applies.
/// </summary>
public sealed record McpView(
    Guid Id, string Name, ViewScope Scope, Guid? JobId, IReadOnlyList<Guid> JobIds, IReadOnlyList<string> Stages, string? Tag, string? BaseView,
    bool CanApply, string? Why, string OwnerName);

/// <summary>The applications a view holds now, newest first, at most the limit asked for.</summary>
public sealed record McpViewApplications(Guid ViewId, string Name, int Count, IReadOnlyList<McpApplicationMatch> Applications);

/// <summary>The team: user ids are what tasks, scorecard requests and interviews take.</summary>
public sealed record McpTeam(IReadOnlyList<McpTeamMember> Members);

/// <summary>One member, by name and role. <c>seesAllJobs</c> is false for someone who sees only the jobs they are on.</summary>
public sealed record McpTeamMember(Guid UserId, string Name, OrganizationRole Role, bool SeesAllJobs);
