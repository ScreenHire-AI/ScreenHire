import { z } from 'zod';

const id = z.uuid();
const instant = z.iso.datetime({ offset: true });
/**
 * The member who made a change, null when no member did (an API key, an automation, the candidate). Missing from an
 * event an older API queued before the field existed, which reads as no member.
 */
const actor = id.nullish();

/**
 * One Zod schema per tenant event the API pushes, keyed by the C# record's name (the event's `type` on the wire).
 * Properties are the record's, camelCased. `contract/events.json`, written by the .NET contract test, holds a sample of
 * every event, and `events.test.ts` parses each one here, so a C# change that is not mirrored fails a test.
 */
export const realtimeEventSchemas = {
  ApplicationMoved: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    fromStageId: z.string(),
    toStageId: z.string(),
    eventId: id,
    movedAt: instant,
    actorUserId: actor,
  }),
  // An interview was offered, booked, moved or cancelled, or its calendar events were written. status is the request's
  // status name (Open, Booked, Cancelled), or null when only the events changed.
  InterviewChanged: z.object({ organizationId: id, applicationId: id, jobId: id, interviewId: id, status: z.string().nullable() }),
  // An email went to an application's candidate and is on its timeline.
  ApplicationEmailed: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, sentAt: instant, actorUserId: actor }),
  // A candidate answered an email. senderUserId is who sent that email (null when an automation did): their dashboard, or for an
  // automation's email every dashboard that sees the job, shows a card. autoReply (an out-of-office) notifies nobody.
  CandidateReplied: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    inboundEmailId: id,
    receivedAt: instant,
    senderUserId: actor,
    autoReply: z.boolean(),
  }),
  // The email provider flagged a candidate's address (Bounced, SpamComplaint), or the flag was cleared (None).
  CandidateEmailStatusChanged: z.object({ organizationId: id, candidateId: id, issue: z.string(), changedAt: instant }),
  // A note from the hiring team: ids only, the dashboard reads the note through GET /v1/applications/{id}/timeline.
  ApplicationNoteAdded: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, addedAt: instant, actorUserId: actor }),
  // A note's author changed its text, or someone deleted it (and its text): the timeline refetches.
  ApplicationNoteEdited: z.object({ organizationId: id, applicationId: id, jobId: id, noteId: id, editedAt: instant }),
  ApplicationNoteDeleted: z.object({ organizationId: id, applicationId: id, jobId: id, noteId: id, deletedAt: instant }),
  // A note mentioned a teammate; only their dashboard shows a card. authorUserId is null when an API key wrote it.
  TeammateMentioned: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    noteId: id,
    mentionId: id,
    mentionedUserId: id,
    authorUserId: id.nullable(),
    mentionedAt: instant,
  }),
  ApplicationSubmitted: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    stageId: z.string(),
    eventId: id,
    submittedAt: instant,
    actorUserId: actor,
  }),
  // A person or an API key added or removed a tag; lists that filter by tag refetch.
  ApplicationTagsChanged: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, changedAt: instant, actorUserId: actor }),
  ScorecardSubmitted: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    scorecardId: id,
    eventId: id,
    submittedAt: instant,
    actorUserId: actor,
  }),
  QuestionnaireAnswered: z.object({ organizationId: id, applicationId: id, jobId: id, requestId: id, eventId: id, answeredAt: instant }),
  ApplicationTaskCreated: z.object({
    organizationId: id,
    taskId: id,
    applicationId: id,
    jobId: id,
    createdAt: instant,
    actorUserId: actor,
    assigneeUserId: id.nullish(),
  }),
  ApplicationTaskCompleted: z.object({
    organizationId: id,
    taskId: id,
    applicationId: id,
    jobId: id,
    completedAt: instant,
    actorUserId: actor,
    assigneeUserId: id.nullish(),
  }),
  // Undo after ticking a task.
  ApplicationTaskReopened: z.object({
    organizationId: id,
    taskId: id,
    applicationId: id,
    jobId: id,
    reopenedAt: instant,
    actorUserId: actor,
    assigneeUserId: id.nullish(),
  }),
  // Raised for Completed, Failed and Blocked runs; the enum is the C# AutomationRunStatus.
  AutomationRunFinished: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    runId: id,
    automationId: z.string(),
    status: z.enum(['NotMatched', 'Skipped', 'Blocked', 'Running', 'Completed', 'Failed']),
    finishedAt: instant,
  }),
  HiringTemplateVersionPublished: z.object({
    organizationId: id,
    templateId: id,
    versionId: id,
    version: z.number().int(),
    publishedAt: instant,
    actorUserId: actor,
    // The member who changed the template last, when not the publisher, whose dashboard can say so.
    previousEditorUserId: id.nullish(),
  }),
  // Someone changed a template. Kind is the C# TemplateKind. The id is text because a built-in email template or
  // scorecard has a name for an id, not a Guid.
  TemplateChanged: z.object({
    organizationId: id,
    kind: z.enum(['HiringTemplate', 'EmailTemplate', 'ScorecardTemplate']),
    templateId: z.string(),
    changedAt: instant,
    actorUserId: actor,
  }),
  // Levels only, never evidence: the dashboard reads the assessment itself through the API. Enums are the C#
  // AuthenticityLevel and AiAssistanceLevel.
  VerificationAssessed: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    authenticity: z.enum(['NoConcerns', 'ReviewSuggested', 'VerificationRecommended']),
    aiAssistance: z.enum(['NotIndicated', 'SomeIndicators', 'StrongIndicators']),
    rulesetVersion: z.string(),
    eventId: id,
    assessedAt: instant,
  }),
  // Someone joined or left a job's hiring team (ADR-0015): the job's team, the Team page's job counts and the jobs list refetch.
  HiringTeamChanged: z.object({ organizationId: id, jobId: id, changedAt: instant }),
  JobClosed: z.object({ organizationId: id, jobId: id, closedAt: instant }),
  // A job's postings changed (docs/architecture/job-distribution.md): the dashboard refetches GET /v1/jobs/{jobId}/postings.
  JobPostingsChanged: z.object({ organizationId: id, jobId: id, changedAt: instant }),
  // version and jobVersionId came after the first JobPublished; an envelope queued before them carries version 1 and null.
  JobPublished: z.object({
    organizationId: id,
    jobId: id,
    publishedAt: instant,
    version: z.number().int(),
    jobVersionId: id.nullable(),
  }),
  // A job's own workflow was edited or took a template update (workflow-engine.md §7): its stages may have changed.
  JobWorkflowChanged: z.object({ organizationId: id, jobId: id, workflowVersion: z.number().int(), changedAt: instant }),
  LiveUpdatesChecked: z.object({ organizationId: id, checkId: id }),
  // Someone was saved or left the saved candidates, or was invited from them (docs/architecture/talent-pool.md). applicationId is
  // the application they were saved from.
  TalentPoolChanged: z.object({ organizationId: id, applicationId: id, jobId: id, candidateId: id, changedAt: instant }),
  // A candidate and everything about them was erased. Carries the id and the time only (an erased id names no one).
  CandidateErased: z.object({ organizationId: id, candidateId: id, erasedAt: instant }),
  // A candidate asked for a copy of their data or for its erasure (docs/architecture/data-requests.md). Ids, the kind and the time only.
  DataRequestReceived: z.object({ organizationId: id, requestId: id, kind: z.enum(['Copy', 'Erasure']), receivedAt: instant }),
  // A data request was answered, or its copy was built or expired: the Data requests list refetches.
  DataRequestChanged: z.object({ organizationId: id, requestId: id }),
  // The record retention run removed ended applications. A count only, so the lists refetch.
  ApplicationsRemoved: z.object({ organizationId: id, count: z.number().int() }),
  TeamNotified: z.object({ organizationId: id, applicationId: id, jobId: id, runId: id, notifiedAt: instant }),
  ViewsChanged: z.object({ organizationId: id, viewId: id, changedAt: instant, actorUserId: actor }),
} as const;

export type RealtimeEventType = keyof typeof realtimeEventSchemas;

/** A parsed event: its type and its data, typed by that type's schema. */
export type RealtimeEvent = {
  [Type in RealtimeEventType]: { type: Type; data: z.infer<(typeof realtimeEventSchemas)[Type]> };
}[RealtimeEventType];

const envelope = z.object({ type: z.string(), data: z.unknown() });

function isEventType(type: string): type is RealtimeEventType {
  return Object.hasOwn(realtimeEventSchemas, type);
}

/**
 * Parses what the hub sent (`{ type, data }`). Returns null for an event type this client does not know, such as one a
 * newer API added, so an old tab ignores it instead of failing. Throws when a known type's data does not match its
 * schema: that is a contract bug, not something to skip quietly.
 */
export function parseRealtimeEvent(raw: unknown): RealtimeEvent | null {
  const { type, data } = envelope.parse(raw);
  if (!isEventType(type)) {
    return null;
  }

  return { type, data: realtimeEventSchemas[type].parse(data) } as RealtimeEvent;
}
